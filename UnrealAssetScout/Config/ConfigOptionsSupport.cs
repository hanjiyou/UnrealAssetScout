using System;
using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CUE4Parse.UE4.Versions;
using UnrealAssetScout.Export;
using UnrealAssetScout.Logging;
using UnrealAssetScout.References;
using UnrealAssetScout.TypeFiltering;
using UnrealAssetScout.Update;
using Superpower;

namespace UnrealAssetScout.Config;

// Handles CLI argument parsing and CLI-related helper behavior. Used exclusively by Program.Main.
internal static class ConfigOptionsSupport
{
    private const string DocumentationUrl = "https://example.com/unrealassetscout-docs";

    internal static Options? ParseArgs(string[] args)
        => ParseArgsWithExitCode(args).Options;

    internal static ParseArgsResult ParseArgsWithExitCode(string[] args)
    {
        var defaultLogFileName = LogFilePathSupport.GetDefaultLogFileName();
        var rootOptions = CreateRecursiveRootOptions(defaultLogFileName);
        var listOptions = CreateListCommandOptions();
        var exportOptions = CreateExportCommandOptions();
        var referenceOptions = CreateReferenceCommandOptions();

        var root = new RootCommand("Inspect, extract, or list Unreal Engine pak/utoc assets.")
        {
            rootOptions.Paks,
            rootOptions.Game,
            rootOptions.Aes,
            rootOptions.AesFile,
            rootOptions.AesStdin,
            rootOptions.AllowPartialMount,
            rootOptions.Usmap,
            rootOptions.Filter,
            rootOptions.Expression,
            rootOptions.Types,
            rootOptions.MarkUsmap,
            rootOptions.LogCounter,
            rootOptions.Log,
            rootOptions.LogAppend,
            rootOptions.NoLog,
            rootOptions.LogLibraries
        };
        var listCommand = new Command("list", "List files from mounted pak/utoc containers.")
        {
            listOptions.Format,
            listOptions.File
        };
        var exportCommand = new Command("export", "Export files from mounted pak/utoc containers.")
        {
            exportOptions.Mode,
            exportOptions.AssetFormat,
            exportOptions.JsonSkipTypes,
            exportOptions.JsonSkipTypesFile,
            exportOptions.NoSkipTypes,
            exportOptions.ScriptBytecode,
            exportOptions.Output,
            exportOptions.Verbose,
            exportOptions.CompactProgress,
            exportOptions.Rebuild,
            exportOptions.DryRun,
            exportOptions.AcceptToolVersion
        };
        var referenceCommand = new Command("refs", "Find static package or serialized-property references to or from one package.")
        {
            referenceOptions.Target,
            referenceOptions.Direction,
            referenceOptions.Kind,
            referenceOptions.Index,
            referenceOptions.RebuildIndex,
            referenceOptions.File
        };
        var updateCommand = new Command("update", "Replace this executable with the latest published release.");
        root.Subcommands.Add(listCommand);
        root.Subcommands.Add(exportCommand);
        root.Subcommands.Add(referenceCommand);
        root.Subcommands.Add(updateCommand);
        ConfigureHelpOption(root);
        ConfigureVersionOption(root);
        var helpAction = root.Options.OfType<HelpOption>().Single().Action;
        var versionAction = root.Options.OfType<VersionOption>().Single().Action;

        var parseResult = root.Parse(args, new ParserConfiguration());
        if (ReferenceEquals(parseResult.Action, helpAction) ||
            ReferenceEquals(parseResult.Action, versionAction))
        {
            parseResult.Invoke(new InvocationConfiguration
            {
                Output = Console.Out,
                Error = Console.Error
            });
            return new ParseArgsResult(null, 0);
        }

        if (ReferenceEquals(parseResult.CommandResult.Command, updateCommand))
            return RunUpdateCommand(parseResult, rootOptions);

        if (parseResult.Errors.Count > 0)
        {
            parseResult.Invoke(new InvocationConfiguration
            {
                Output = Console.Error,
                Error = Console.Error
            });
            return new ParseArgsResult(null, 1);
        }

        var isExportCommand = ReferenceEquals(parseResult.CommandResult.Command, exportCommand);
        var isListCommand = ReferenceEquals(parseResult.CommandResult.Command, listCommand);
        var isReferenceCommand = ReferenceEquals(parseResult.CommandResult.Command, referenceCommand);
        var outputDirectory = isExportCommand ? parseResult.GetRequiredValue(exportOptions.Output) : null;

        var options = new Options
        {
            PaksDirectory = parseResult.GetRequiredValue(rootOptions.Paks).FullName,
            UsmapPath = parseResult.GetValue(rootOptions.Usmap)?.FullName,
            TypeFilterExpression = parseResult.GetValue(rootOptions.Expression),
            TypeFilterCsvPath = parseResult.GetValue(rootOptions.Types)?.FullName,
            OutputDirectory = outputDirectory,
            ListOutputFilePath = isListCommand ? parseResult.GetValue(listOptions.File) : null,
            ListFormat = isListCommand ? parseResult.GetValue(listOptions.Format) : ListOutputFormat.List,
            ReferenceTarget = isReferenceCommand ? parseResult.GetRequiredValue(referenceOptions.Target) : null,
            ReferenceOutputFilePath = isReferenceCommand ? parseResult.GetValue(referenceOptions.File) : null,
            ReferenceIndexPath = isReferenceCommand ? parseResult.GetValue(referenceOptions.Index) : null,
            RebuildReferenceIndex = isReferenceCommand && parseResult.GetValue(referenceOptions.RebuildIndex),
            ReferenceDirection = isReferenceCommand
                ? parseResult.GetValue(referenceOptions.Direction)
                : ReferenceDirection.Both,
            ReferenceKindScope = isReferenceCommand
                ? parseResult.GetValue(referenceOptions.Kind)
                : ReferenceKindScope.Package,
            Verbose = isExportCommand && parseResult.GetValue(exportOptions.Verbose),
            MarkUsmap = parseResult.GetValue(rootOptions.MarkUsmap),
            CompactProgress = isExportCommand && parseResult.GetValue(exportOptions.CompactProgress),
            ScriptBytecode = isExportCommand && parseResult.GetValue(exportOptions.ScriptBytecode),
            Rebuild = isExportCommand && parseResult.GetValue(exportOptions.Rebuild),
            DryRun = isExportCommand && parseResult.GetValue(exportOptions.DryRun),
            AcceptToolVersion = isExportCommand && parseResult.GetValue(exportOptions.AcceptToolVersion),
            AssetFormat = isExportCommand
                ? parseResult.GetValue(exportOptions.AssetFormat)
                : ConversionAssetFormat.UEFormat,
            LogCounter = parseResult.GetValue(rootOptions.LogCounter),
            Log = parseResult.GetValue(rootOptions.Log) ?? defaultLogFileName,
            LogSpecified = parseResult.GetResult(rootOptions.Log) is not null,
            LogAppend = parseResult.GetValue(rootOptions.LogAppend),
            NoLog = parseResult.GetValue(rootOptions.NoLog),
            LogLibraries = parseResult.GetValue(rootOptions.LogLibraries),
            Game = parseResult.GetRequiredValue(rootOptions.Game),
            AesFromStandardInput = parseResult.GetValue(rootOptions.AesStdin),
            AllowPartialMount = parseResult.GetValue(rootOptions.AllowPartialMount)
        };

        var aesFilePath = parseResult.GetValue(rootOptions.AesFile)?.FullName;
        var aesDirectValue = parseResult.GetValue(rootOptions.Aes);
        var aesSourceCount = (aesFilePath is not null ? 1 : 0) +
                             (aesDirectValue is not null ? 1 : 0) +
                             (options.AesFromStandardInput ? 1 : 0);
        if (aesSourceCount > 1)
        {
            AppLog.Error("Use only one AES source: --aes-stdin, --aes, or --aes-file.");
            return new ParseArgsResult(null, 1);
        }

        if (aesFilePath is not null)
        {
            if (!AesKeyConfigSupport.TryReadKeyFile(aesFilePath, out var aesKey))
                return new ParseArgsResult(null, 1);
            options.AesKey = aesKey;
        }
        else
        {
            options.AesKey = aesDirectValue;
        }

        if (isExportCommand)
        {
            options.Mode = parseResult.GetRequiredValue(exportOptions.Mode);
            var inlineSkipTypesSpecified = parseResult.GetResult(exportOptions.JsonSkipTypes) is not null;
            var fileSkipTypesPath = parseResult.GetValue(exportOptions.JsonSkipTypesFile)?.FullName;
            var noSkipTypes = parseResult.GetValue(exportOptions.NoSkipTypes);

            if (noSkipTypes)
            {
                options.JsonSkipTypeNames = [];
            }
            else if (fileSkipTypesPath is not null)
            {
                if (!JsonSkipTypeConfigSupport.TryReadTypeFile(fileSkipTypesPath, out var jsonSkipTypeNames))
                    return new ParseArgsResult(null, 1);
                options.JsonSkipTypeNames = [.. jsonSkipTypeNames];
            }
            else if (inlineSkipTypesSpecified)
            {
                options.JsonSkipTypeNames = [.. parseResult.GetValue(exportOptions.JsonSkipTypes) ?? []];
            }
            else
            {
                options.JsonSkipTypeNames = [.. JsonSkipTypeConfigSupport.DefaultTypeNames];
            }
        }

        if (options.RebuildReferenceIndex && string.IsNullOrWhiteSpace(options.ReferenceIndexPath))
        {
            AppLog.Error("refs --rebuild-index requires --index.");
            return new ParseArgsResult(null, 1);
        }

        if (string.IsNullOrWhiteSpace(options.TypeFilterExpression) != string.IsNullOrWhiteSpace(options.TypeFilterCsvPath))
        {
            AppLog.Error("Type filtering requires both --expression and --types.");
            return new ParseArgsResult(null, 1);
        }

        if (!string.IsNullOrWhiteSpace(options.TypeFilterExpression))
        {
            try
            {
                options.TypeFilterPredicate = new TypeFilterParser().Parse(options.TypeFilterExpression);
            }
            catch (ParseException e)
            {
                AppLog.Error("Invalid type expression for --expression: {Message}", e.Message);
                return new ParseArgsResult(null, 1);
            }
        }

        var filterValue = parseResult.GetValue(rootOptions.Filter);
        if (!string.IsNullOrWhiteSpace(filterValue))
        {
            try
            {
                options.Filter = new Regex(filterValue, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            catch (ArgumentException e)
            {
                AppLog.Error("Invalid regular expression for --filter: {Message}", e.Message);
                return new ParseArgsResult(null, 1);
            }
        }

        if (options.AllowPartialMount && (!isExportCommand || options.Filter is null))
        {
            AppLog.Error("--allow-partial-mount requires export with an explicit --filter; unmatched or missing dependencies are still errors");
            return new ParseArgsResult(null, 1);
        }
        return new ParseArgsResult(options, 0);
    }
    private static RootOptions CreateRecursiveRootOptions(string defaultLogFileName)
        => new(
            ConfigOptionFactory.CreateExistingDirectoryOption("--paks", "-p", "Path to the game's Paks folder", required: true, recursive: true),
            ConfigOptionFactory.CreateEnumOption<EGame>("--game", "-g", "Game/engine version from the EGame enum, e.g. GAME_UE5_4", required: true, recursive: true),
            ConfigOptionFactory.CreateStringOption("--aes", "-a", "AES-256 encryption key, e.g. 0xABCD1234...", recursive: true),
            ConfigOptionFactory.CreateExistingFileOption("--aes-file", "-A", "Path to a text file whose first line is the AES-256 key", recursive: true),
            ConfigOptionFactory.CreateBoolOption("--aes-stdin", "Read the AES-256 key from the first line of standard input", recursive: true),
            ConfigOptionFactory.CreateBoolOption("--allow-partial-mount", "Export a filtered scope from already mounted containers; unavailable containers remain inaccessible and completeness is not claimed", recursive: true),
            ConfigOptionFactory.CreateExistingFileOption("--usmap", "-u", "Path to a .usmap mappings file", recursive: true),
            ConfigOptionFactory.CreateStringOption("--filter", "-f", "Regular expression; only files whose path matches are processed. On an incremental run, narrowing this deletes every previously exported output outside the new scope", recursive: true),
            ConfigOptionFactory.CreateStringOption("--expression", "-e", "Type filter expression; requires --types", recursive: true),
            ConfigOptionFactory.CreateExistingFileOption("--types", "-T", "Path to a list --format types CSV file; requires --expression", recursive: true),
            ConfigOptionFactory.CreateBoolOption("--mark-usmap", "-m", "Prefix files with [*] when usmap is required", recursive: true),
            ConfigOptionFactory.CreateBoolOption("--log-counter", "-i", "Prefix file-associated log lines in the log file with [current/total]", recursive: true),
            ConfigOptionFactory.CreateStringOption("--log", "-l", $"Log file path (default: .\\{defaultLogFileName}; overwritten each run unless --log-append is set)", recursive: true),
            ConfigOptionFactory.CreateBoolOption("--log-append", "-L", "Append to existing log file instead of overwriting each run", recursive: true),
            ConfigOptionFactory.CreateBoolOption("--no-log", "-z", "Disable file logging", recursive: true),
            ConfigOptionFactory.CreateBoolOption("--log-libs", "-D", "Also log CUE4Parse and other dependency warnings/errors", recursive: true));

    private static ListCommandOptions CreateListCommandOptions()
    {
        var format = ConfigOptionFactory.CreateEnumOption<ListOutputFormat>(
            "--format",
            "-F",
            "list: Output format: List, Tree, or Types.");
        format.HelpName = "format";
        var file = new Option<string>("--file", "-o")
        {
            Description = "list: Also write plain list/tree/types output to this file while keeping console output visible.",
            HelpName = "filename"
        };
        return new(format, file);
    }

    private static ExportCommandOptions CreateExportCommandOptions()
    {
        var output = new Option<string>("--output", "-o")
        {
            Description = "export: Output directory",
            Required = true
        };
        var skipTypes = new Option<string[]>("--skip-types", "-s")
        {
            Description = "export json: Replaces the built-in default skip list with the specified type names",
            HelpName = "types",
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = true
        };
        var skipTypesFile = ConfigOptionFactory.CreateExistingFileOption("--skip-types-file", "-S", "export json: Path to a text file containing skip type names");
        skipTypesFile.HelpName = "filename";
        var noSkipTypes = ConfigOptionFactory.CreateBoolOption("--no-skip-types", "-k", "export json: Disable the built-in skip list entirely");
        var scriptBytecode = ConfigOptionFactory.CreateBoolOption("--script-bytecode", "-b", "export json: Serialize script bytecode into JSON output. Ignored for other export modes.");
        var assetFormat = new Option<ConversionAssetFormat>("--asset-format")
        {
            Description = "export models/animations: Conversion artifact family: UEFormat or ActorX. Ignored for other export modes."
        };
        assetFormat.DefaultValueFactory = _ => ConversionAssetFormat.UEFormat;
        assetFormat.HelpName = "format";

        return new(
            new Argument<ExportMode>("mode")
            {
                Description = "Export mode: Simple, Raw, Json, Textures, Models, Animations, Audio, or Verse"
            },
            assetFormat,
            skipTypes,
            skipTypesFile,
            noSkipTypes,
            scriptBytecode,
            output,
            ConfigOptionFactory.CreateBoolOption("--verbose", "-v", "export: Print skipped files in the log"),
            ConfigOptionFactory.CreateBoolOption("--compact", "-c", "export: Show compact progress and write full logs to a file"),
            ConfigOptionFactory.CreateBoolOption("--rebuild", "-r", "export: Ignore any existing manifest, export everything, and replace the manifest"),
            ConfigOptionFactory.CreateBoolOption("--dry-run", "-n", "export: Report what an incremental run would do, and write nothing"),
            ConfigOptionFactory.CreateBoolOption("--accept-tool-version", "-q", "export: Proceed when the uas or CUE4Parse version is not recorded in the manifest, accepting that output may not match a full rebuild"));
    }

    private static ReferenceCommandOptions CreateReferenceCommandOptions()
    {
        var target = new Argument<string>("target")
        {
            Description = "Exact mounted package path or dependency identity, e.g. PioneerGame/Content/Foo.uasset or /Game/Foo"
        };
        var direction = ConfigOptionFactory.CreateEnumOption<ReferenceDirection>(
            "--direction",
            "-d",
            "Reference direction: Incoming, Outgoing, or Both.");
        direction.DefaultValueFactory = _ => ReferenceDirection.Both;
        direction.HelpName = "direction";
        var kind = ConfigOptionFactory.CreateEnumOption<ReferenceKindScope>(
            "--kind",
            "-k",
            "Reference layer: Package, Properties, Bytecode, or All. Property and bytecode scanning deserialize exports and are slower.");
        kind.DefaultValueFactory = _ => ReferenceKindScope.Package;
        kind.HelpName = "kind";
        var file = new Option<string>("--file", "-o")
        {
            Description = "refs: Also write tab-separated results to this file while keeping console output visible.",
            HelpName = "filename"
        };
        var index = new Option<string>("--index", "-I")
        {
            Description = "refs: Reusable reference index file. Existing compatible entries are resumed automatically.",
            HelpName = "filename"
        };
        var rebuildIndex = ConfigOptionFactory.CreateBoolOption(
            "--rebuild-index",
            "refs: Ignore and atomically replace an existing reference index.");
        return new ReferenceCommandOptions(target, direction, kind, index, rebuildIndex, file);
    }

    private static void ConfigureHelpOption(RootCommand root)
    {
        var helpOption = root.Options.OfType<HelpOption>().Single();
        helpOption.Action = new DocumentationLinkHelpAction(
            (SynchronousCommandLineAction)helpOption.Action!,
            DocumentationUrl);
    }

    private static void ConfigureVersionOption(RootCommand root)
    {
        root.Options.OfType<VersionOption>().Single().Action = new BuildVersionAction();
    }

    private static ParseArgsResult RunUpdateCommand(ParseResult parseResult, RootOptions rootOptions)
    {
        // --paks and --game are recursive, so unspecified they would be reported as errors otherwise
        var unrelatedErrors = parseResult.Errors
            .Where(error => error.SymbolResult is not OptionResult optionResult
                            || (optionResult.Option != rootOptions.Paks && optionResult.Option != rootOptions.Game))
            .ToList();

        if (unrelatedErrors.Count > 0)
        {
            foreach (var error in unrelatedErrors)
                Console.Error.WriteLine(error.Message);
            return new ParseArgsResult(null, 1);
        }

        return new ParseArgsResult(null, UpdateCommand.Run());
    }

    private sealed record RootOptions(
        Option<DirectoryInfo> Paks,
        Option<EGame> Game,
        Option<string> Aes,
        Option<FileInfo> AesFile,
        Option<bool> AesStdin,
        Option<bool> AllowPartialMount,
        Option<FileInfo> Usmap,
        Option<string> Filter,
        Option<string> Expression,
        Option<FileInfo> Types,
        Option<bool> MarkUsmap,
        Option<bool> LogCounter,
        Option<string> Log,
        Option<bool> LogAppend,
        Option<bool> NoLog,
        Option<bool> LogLibraries);

    private sealed record ListCommandOptions(
        Option<ListOutputFormat> Format,
        Option<string> File);

    private sealed record ExportCommandOptions(
        Argument<ExportMode> Mode,
        Option<ConversionAssetFormat> AssetFormat,
        Option<string[]> JsonSkipTypes,
        Option<FileInfo> JsonSkipTypesFile,
        Option<bool> NoSkipTypes,
        Option<bool> ScriptBytecode,
        Option<string> Output,
        Option<bool> Verbose,
        Option<bool> CompactProgress,
        Option<bool> Rebuild,
        Option<bool> DryRun,
        Option<bool> AcceptToolVersion);

    private sealed record ReferenceCommandOptions(
        Argument<string> Target,
        Option<ReferenceDirection> Direction,
        Option<ReferenceKindScope> Kind,
        Option<string> Index,
        Option<bool> RebuildIndex,
        Option<string> File);

    internal sealed record ParseArgsResult(Options? Options, int ExitCode);
}
