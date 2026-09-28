using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using CUE4Parse_Conversion.Textures.BC;
using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using UnrealAssetScout.Config;
using UnrealAssetScout.Export;
using UnrealAssetScout.Incremental;
using UnrealAssetScout.List;
using UnrealAssetScout.Logging;
using UnrealAssetScout.References;
using UnrealAssetScout.Statistics;
using UnrealAssetScout.TypeFiltering;
using UnrealAssetScout.Update;
using UnrealAssetScout.Utils;

namespace UnrealAssetScout;

public static class Program
{
    public static void Main(string[] args)
    {
        Environment.ExitCode = Run(args);
    }

    internal static int Run(string[] args)
    {
        try
        {
            // This is configuring Serilog for the Command Line parsing output, when we do not know the logging options yet
            RuntimeLogging.ConfigureBootstrapLogger();

            SelfUpdate.SweepLeftovers();

            var parseArgsResult = ConfigOptionsSupport.ParseArgsWithExitCode(args);
            if (parseArgsResult.Options is null)
                return parseArgsResult.ExitCode;

            var options = parseArgsResult.Options;

            var fileLoggingEnabled = !options.NoLog;
            var logFilePath = LogFilePathSupport.ResolveLogFilePath(options.Log);
            bool compactProgressEnabled = options is { CompactProgress: true, Mode: not null };

            if (fileLoggingEnabled)
                RuntimeLogging.PrepareLogFile(logFilePath, options.LogAppend);

            // This is re-configuring Serilog for the rest of the run, taking into account compact progress mode
            var compactCounterSink = RuntimeLogging.ReConfigureLogger(
                compactProgressEnabled,
                fileLoggingEnabled,
                logFilePath,
                options.LogLibraries);

            AppLog.Information("uas {Version}", AppVersion.DisplayText);

            if (fileLoggingEnabled)
            {
                if (compactProgressEnabled)
                    Console.Error.WriteLine($"Progress: compact. Mode: {options.Mode!.Value}. Log: {logFilePath}");
                else if (options.LogAppend)
                    AppLog.Information("Appending log to {LogFile}", logFilePath);
                else
                    AppLog.Information("Writing log to {LogFile}", logFilePath);
            }
            else if (compactProgressEnabled)
            {
                Console.Error.WriteLine($"Progress: compact. Mode: {options.Mode!.Value}. Log: disabled (--no-log).");
            }

            var totalStopwatch = Stopwatch.StartNew();
            RunStats? runStats = null;
            var exeDir = AppContext.BaseDirectory;

            // Match upstream UnrealAssetScout: CUE4Parse resolves/downloads its compression
            // libraries when absent, while Detex is released from the embedded resource.
            ZlibHelper.Initialize(Path.Combine(exeDir, ZlibHelper.DLL_NAME));
            var currentOodlePath = Path.Combine(exeDir, OodleHelper.OODLE_NAME_CURRENT);
            var legacyOodlePath = Path.Combine(exeDir, OodleHelper.OODLE_NAME_OLD);
            OodleHelper.Initialize(File.Exists(currentOodlePath)
                ? currentOodlePath
                : File.Exists(legacyOodlePath)
                    ? legacyOodlePath
                    : currentOodlePath);
            var detexPath = Path.Combine(exeDir, DetexHelper.DLL_NAME);
            if (!File.Exists(detexPath))
                DetexHelper.LoadDllAsync(detexPath).GetAwaiter().GetResult();

            DetexHelper.Initialize(detexPath);

            var provider = ProviderFactory.Create(
                options.PaksDirectory!,
                options.Game!.Value);

            AppLog.Information("Provider: {Provider}; game: {Game}", provider.GetType().Name, options.Game.Value);

            provider.ReadScriptData = options is { Mode: ExportMode.Json, ScriptBytecode: true } ||
                                      options.ReferenceKindScope is ReferenceKindScope.Bytecode or ReferenceKindScope.All;

            if (options.UsmapPath is not null)
                provider.MappingsContainer = new FileUsmapTypeMappingsProvider(options.UsmapPath);

            provider.Initialize();

            // Always submit a key for the zero GUID - this is what triggers mounting.
            // For unencrypted containers any key works; for encrypted ones the real key is required.
            var aesKeyValue = options.AesKey;
            if (options.AesFromStandardInput &&
                !AesKeyConfigSupport.TryReadKeyFromStandardInput(Console.In, out aesKeyValue))
            {
                return 1;
            }

            FAesKey aesKey;
            try
            {
                aesKey = aesKeyValue is not null
                    ? new FAesKey(aesKeyValue)
                    : new FAesKey(new byte[32]);
            }
            catch (ArgumentException e)
            {
                AppLog.Error("Invalid AES key value: {Message}", e.Message);
                return 1;
            }

            provider.SubmitKey(new FGuid(), aesKey);

            if (provider.RequiredKeys.Count > 0)
            {
                if (!options.AllowPartialMount || provider.MountedVfs.Count == 0)
                {
                    AppLog.Error(
                        "{Count} container(s) are encrypted and could not be mounted - provide the correct AES key via --aes-stdin, --aes, or --aes-file",
                        provider.RequiredKeys.Count);
                    return 1;
                }
                AppLog.Warning("Explicit partial mount: {Mounted} containers available, {Missing} encrypted containers unavailable. Only the filtered mounted scope can be exported; missing dependencies still fail.",
                    provider.MountedVfs.Count, provider.RequiredKeys.Count);
            }

            provider.PostMount();
            provider.LoadVirtualPaths();
            RuntimeReporting.WarnIfAesCouldRevealMore(provider, aesKeyValue is not null);

            HashSet<string>? typeFilteredPaths = null;
            if (options.TypeFilterPredicate is not null)
            {
                if (!TypeFilterSupport.TryGetTypeFilteredPaths(
                        options.TypeFilterPredicate,
                        options.TypeFilterCsvPath!,
                        out typeFilteredPaths))
                {
                    return 1;
                }
            }

            if (options.MarkUsmap)
                AppLog.Information("Usmap marker enabled: files that require usmap are prefixed with [*].");

            StreamWriter? auxiliaryOutputWriter = null;
            var auxiliaryOutputPath = options.ReferenceTarget is not null
                ? options.ReferenceOutputFilePath
                : options.ListOutputFilePath;
            if (options.Mode is null &&
                !TryCreateOutputWriter(auxiliaryOutputPath, out auxiliaryOutputWriter))
            {
                return 1;
            }

            using (auxiliaryOutputWriter)
            {
                if (options.ReferenceTarget is not null)
                {
                    var exitCode = ReferenceProcessor.Run(provider, options, auxiliaryOutputWriter);
                    if (exitCode != 0)
                        return exitCode;
                }
                else if (options.Mode is null)
                {
                    ListProcessor.ListFiles(provider, options, auxiliaryOutputWriter, typeFilteredPaths);
                }
                else
                {
                    var (exitCode, incrementalStats) = IncrementalRunner.Run(
                        provider, options, compactCounterSink, typeFilteredPaths);
                    if (exitCode != 0)
                        return exitCode;

                    runStats = incrementalStats;
                }
            }

            RuntimeReporting.WriteCompletionSummary(totalStopwatch.Elapsed, runStats, compactProgressEnabled);
            return 0;
        }
        catch (Exception e)
        {
            var exceptionType = e.GetType().FullName ?? e.GetType().Name;
            Console.Error.WriteLine($"Unhandled exception: {exceptionType}: {e.Message}");
            return 1;
        }
        finally
        {
            RuntimeLogging.CloseAndFlush();
        }
    }

    private static bool TryCreateOutputWriter(string? outputFilePath, out StreamWriter? writer)
    {
        writer = null;
        if (string.IsNullOrWhiteSpace(outputFilePath))
            return true;

        try
        {
            var outputDirectory = Path.GetDirectoryName(outputFilePath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            writer = new StreamWriter(outputFilePath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return true;
        }
        catch (Exception e)
        {
            AppLog.Error("Failed to open list output file '{Path}': {Message}", outputFilePath, e.Message);
            return false;
        }
    }
}
