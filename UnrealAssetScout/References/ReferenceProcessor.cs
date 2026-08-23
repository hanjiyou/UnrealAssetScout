using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CUE4Parse.FileProvider.Vfs;
using UnrealAssetScout.Config;
using UnrealAssetScout.Incremental;
using UnrealAssetScout.Logging;
using UnrealAssetScout.Package;

namespace UnrealAssetScout.References;

// Scans mounted packages for package-level hard imports and presents refs command results.
internal static class ReferenceProcessor
{
    internal static int Run(AbstractVfsFileProvider provider, Options options, TextWriter? outputWriter)
    {
        var scanPackageReferences = options.ReferenceKindScope is ReferenceKindScope.Package or ReferenceKindScope.All;
        var scanPropertyReferences = options.ReferenceKindScope is ReferenceKindScope.Properties or ReferenceKindScope.All;
        var scanBytecodeReferences = options.ReferenceKindScope is ReferenceKindScope.Bytecode or ReferenceKindScope.All;

        IEnumerable<CUE4Parse.FileProvider.Objects.GameFile> packageFilesQuery =
            SourceFingerprintIndex.ResolvedFiles(provider)
            .Where(file => file.Path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase) ||
                           file.Path.EndsWith(".umap", StringComparison.OrdinalIgnoreCase));

        if (options.Filter is not null)
        {
            packageFilesQuery = packageFilesQuery.Where(file => options.Filter.IsMatch(file.Path));
            AppLog.Warning(
                "Reference source scanning is restricted by --filter. Incoming results cover only matching source packages.");
        }

        if (options.ReferenceDirection == ReferenceDirection.Outgoing)
        {
            var resolvedTargetPath = IncrementalRunner.ResolvePackagePath(provider, options.ReferenceTarget!);
            packageFilesQuery = packageFilesQuery.Where(file =>
                PackageReferenceQuery.MatchesTarget(options.ReferenceTarget!, file.Path) ||
                PackageReferenceQuery.MatchesTarget(resolvedTargetPath ?? string.Empty, file.Path));
        }

        var packageFiles = packageFilesQuery.ToList();
        var packageEdges = new List<PackageReferenceEdge>();
        var propertyEdges = new List<PropertyReferenceEdge>();
        var bytecodeEdges = new List<KismetReferenceEdge>();
        var loadFailures = 0;
        var propertyReadFailures = 0;
        var bytecodeReadFailures = 0;
        var scannedPackages = 0;
        var reusedPackages = 0;
        var processedPackages = 0;
        var modifiedSinceCheckpoint = 0;
        var progressStopwatch = Stopwatch.StartNew();
        var checkpointStopwatch = Stopwatch.StartNew();

        ReferenceIndexDocument? referenceIndex = null;
        SourceFingerprintIndex? fingerprintIndex = null;
        if (options.ReferenceIndexPath is not null)
        {
            var indexAlreadyExists = File.Exists(options.ReferenceIndexPath);
            var identity = ReferenceIndexIdentity.Create(options.Game!.Value, options.UsmapPath);
            if (options.RebuildReferenceIndex)
            {
                referenceIndex = identity.NewDocument();
            }
            else
            {
                referenceIndex = ReferenceIndexStore.TryLoad(options.ReferenceIndexPath, out var indexError);
                if (indexError is not null)
                {
                    AppLog.Error("{Message}. Use --rebuild-index to replace it.", indexError);
                    return 1;
                }

                if (referenceIndex is not null && !identity.Matches(referenceIndex))
                {
                    AppLog.Error(
                        "Reference index '{Path}' was created for different game, mappings, or parser binaries. " +
                        "Use --rebuild-index to replace it.",
                        options.ReferenceIndexPath);
                    return 1;
                }

                referenceIndex ??= identity.NewDocument();
            }

            AppLog.Information("Building container fingerprint lookup for reference index reuse...");
            fingerprintIndex = SourceFingerprintIndex.Build(provider);
            AppLog.Information(
                "Reference index enabled: {Path}; {Sources:N0} cached source package(s).",
                Path.GetFullPath(options.ReferenceIndexPath),
                referenceIndex.Sources.Count);
            if (options.RebuildReferenceIndex || !indexAlreadyExists)
                modifiedSinceCheckpoint = 1;
        }

        AppLog.Information(
            "Scanning {Count:N0} packages for {Scope} references...",
            packageFiles.Count,
            options.ReferenceKindScope);

        foreach (var file in packageFiles)
        {
            ReferenceIndexSource? cachedSource = null;
            string? currentFingerprint = null;
            if (referenceIndex is not null)
            {
                fingerprintIndex!.ByPath.TryGetValue(file.Path, out currentFingerprint);
                referenceIndex.Sources.TryGetValue(file.Path, out cachedSource);
            }

            var reusePackage = scanPackageReferences &&
                               ReferenceIndexReusePolicy.CanReusePackage(cachedSource, currentFingerprint);
            var reuseProperties = scanPropertyReferences &&
                                  ReferenceIndexReusePolicy.CanReuseProperties(cachedSource, currentFingerprint);
            var reuseBytecode = scanBytecodeReferences &&
                                ReferenceIndexReusePolicy.CanReuseBytecode(cachedSource, currentFingerprint);
            var needsPackageScan = scanPackageReferences && !reusePackage;
            var needsPropertyScan = scanPropertyReferences && !reuseProperties;
            var needsBytecodeScan = scanBytecodeReferences && !reuseBytecode;
            var source = ReferenceIndexReusePolicy.HasMatchingFingerprint(cachedSource, currentFingerprint)
                ? cachedSource!
                : new ReferenceIndexSource { Fingerprint = currentFingerprint };

            if (needsPackageScan || needsPropertyScan || needsBytecodeScan)
            {
                scannedPackages++;
                var context = PackageLoadSupport.PreparePackageForExport(provider, file, markUsmap: false);
                if (context.Package is null)
                {
                    loadFailures++;
                }
                else
                {
                    if (needsPackageScan)
                    {
                        source.PackageEdges = PackageDependencyReader.Read(context.Package, provider)
                            .Select(identity => new PackageReferenceEdge(
                                file.Path,
                                identity,
                                IncrementalRunner.ResolvePackagePath(provider, identity)))
                            .ToList();
                        source.PackageComplete = true;
                    }

                    if (needsPropertyScan)
                    {
                        try
                        {
                            source.PropertyEdges = PropertyReferenceReader.Read(
                                    context.Package,
                                    file.Path,
                                    identity => IncrementalRunner.ResolvePackagePath(provider, identity))
                                .ToList();
                            source.PropertiesComplete = true;
                        }
                        catch
                        {
                            propertyReadFailures++;
                            source.PropertiesComplete = false;
                            source.PropertyEdges = [];
                        }
                    }

                    if (needsBytecodeScan)
                    {
                        try
                        {
                            source.BytecodeEdges = KismetReferenceReader.Read(
                                    context.Package,
                                    file.Path,
                                    identity => IncrementalRunner.ResolvePackagePath(provider, identity))
                                .ToList();
                            source.BytecodeComplete = true;
                        }
                        catch
                        {
                            bytecodeReadFailures++;
                            source.BytecodeComplete = false;
                            source.BytecodeEdges = [];
                        }
                    }
                }

                if (referenceIndex is not null && currentFingerprint is not null)
                {
                    referenceIndex.Sources[file.Path] = source;
                    modifiedSinceCheckpoint++;
                }
            }
            else
            {
                reusedPackages++;
            }

            if (scanPackageReferences && source.PackageComplete)
                packageEdges.AddRange(source.PackageEdges);
            if (scanPropertyReferences && source.PropertiesComplete)
                propertyEdges.AddRange(source.PropertyEdges);
            if (scanBytecodeReferences && source.BytecodeComplete)
                bytecodeEdges.AddRange(source.BytecodeEdges);

            processedPackages++;
            if (referenceIndex is not null &&
                modifiedSinceCheckpoint > 0 &&
                (modifiedSinceCheckpoint >= 250 || checkpointStopwatch.Elapsed >= TimeSpan.FromSeconds(5)))
            {
                if (!TrySaveIndex(options.ReferenceIndexPath!, referenceIndex))
                    return 1;
                modifiedSinceCheckpoint = 0;
                checkpointStopwatch.Restart();
            }

            if (processedPackages % 250 == 0 || progressStopwatch.Elapsed >= TimeSpan.FromSeconds(5))
            {
                LogProgress(
                    processedPackages,
                    packageFiles.Count,
                    scannedPackages,
                    reusedPackages,
                    loadFailures,
                    propertyReadFailures + bytecodeReadFailures);
                progressStopwatch.Restart();
            }
        }

        if (referenceIndex is not null && modifiedSinceCheckpoint > 0 &&
            !TrySaveIndex(options.ReferenceIndexPath!, referenceIndex))
        {
            return 1;
        }

        var packageMatches = PackageReferenceQuery.Find(
                packageEdges,
                options.ReferenceTarget!,
                options.ReferenceDirection)
            .ToList();
        var propertyMatches = PropertyReferenceQuery.Find(
                propertyEdges,
                options.ReferenceTarget!,
                options.ReferenceDirection)
            .ToList();
        var bytecodeMatches = KismetReferenceQuery.Find(
                bytecodeEdges,
                options.ReferenceTarget!,
                options.ReferenceDirection)
            .ToList();

        var rows = packageMatches.Select(match => new ResultRow(
                match.Direction,
                "HARD_PACKAGE_IMPORT",
                match.Edge.SourcePath,
                match.Edge.TargetIdentity,
                match.Edge.TargetPath,
                string.Empty))
            .Concat(propertyMatches.Select(match => new ResultRow(
                match.Direction,
                match.Edge.Kind,
                match.Edge.SourcePath,
                match.Edge.TargetIdentity,
                match.Edge.TargetPath,
                match.Edge.EvidencePath)))
            .Concat(bytecodeMatches.Select(match => new ResultRow(
                match.Direction,
                match.Edge.Kind,
                match.Edge.SourcePath,
                match.Edge.TargetIdentity,
                match.Edge.TargetPath,
                match.Edge.EvidencePath)))
            .OrderBy(row => row.Direction)
            .ThenBy(row => row.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Kind, StringComparer.Ordinal)
            .ThenBy(row => row.TargetIdentity, StringComparer.Ordinal)
            .ThenBy(row => row.EvidencePath, StringComparer.Ordinal)
            .ToList();

        WriteLine("Direction\tKind\tSourcePath\tTargetIdentity\tTargetPath\tStatus\tEvidencePath", outputWriter);
        foreach (var row in rows)
        {
            WriteLine(
                $"{row.Direction}\t{row.Kind}\t{Sanitize(row.SourcePath)}\t{Sanitize(row.TargetIdentity)}\t" +
                $"{Sanitize(row.TargetPath)}\t{(row.TargetPath is null ? "UNRESOLVED" : "RESOLVED")}\t" +
                Sanitize(row.EvidencePath),
                outputWriter);
        }

        AppLog.Information(
            "Reference scan complete: {Packages:N0} packages, {PackageEdges:N0} hard-import edges, " +
            "{PropertyEdges:N0} property edges, {BytecodeEdges:N0} bytecode edges, {Matches:N0} matches, " +
            "{LoadFailures:N0} package load failures, {PropertyFailures:N0} property-read failures, " +
            "{BytecodeFailures:N0} bytecode-read failures, {Scanned:N0} scanned, {Reused:N0} reused from index.",
            packageFiles.Count,
            packageEdges.Count,
            propertyEdges.Count,
            bytecodeEdges.Count,
            rows.Count,
            loadFailures,
            propertyReadFailures,
            bytecodeReadFailures,
            scannedPackages,
            reusedPackages);

        if (loadFailures > 0 || propertyReadFailures > 0 || bytecodeReadFailures > 0)
        {
            AppLog.Warning(
                "The reference result is incomplete because {LoadFailures:N0} package(s) could not be loaded and " +
                "{PropertyFailures:N0} package(s) could not be inspected for serialized property references and " +
                "{BytecodeFailures:N0} package(s) could not be inspected for Kismet bytecode references. " +
                "Check the AES key, game version, mappings, and dependency logs.",
                loadFailures,
                propertyReadFailures,
                bytecodeReadFailures);
        }

        return 0;
    }

    private static void LogProgress(
        int processed,
        int total,
        int scanned,
        int reused,
        int loadFailures,
        int propertyFailures) =>
        AppLog.Information(
            "Reference progress: {Processed:N0}/{Total:N0}; scanned {Scanned:N0}; reused {Reused:N0}; " +
            "failures {Failures:N0}; remaining {Remaining:N0}.",
            processed,
            total,
            scanned,
            reused,
            loadFailures + propertyFailures,
            total - processed);

    private static bool TrySaveIndex(string path, ReferenceIndexDocument document)
    {
        try
        {
            ReferenceIndexStore.Save(path, document);
            AppLog.Information(
                "Reference index checkpoint: {Sources:N0} source package(s) saved to {Path}.",
                document.Sources.Count,
                Path.GetFullPath(path));
            return true;
        }
        catch (Exception e)
        {
            AppLog.Error("Could not save reference index '{Path}': {Message}", path, e.Message);
            return false;
        }
    }

    private static void WriteLine(string line, TextWriter? outputWriter)
    {
        RuntimeLogging.LogPlainOutputLine(line);
        outputWriter?.WriteLine(line);
    }

    private static string Sanitize(string? value) =>
        value?.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ') ?? string.Empty;

    private sealed record ResultRow(
        ReferenceDirection Direction,
        string Kind,
        string SourcePath,
        string TargetIdentity,
        string? TargetPath,
        string EvidencePath);
}
