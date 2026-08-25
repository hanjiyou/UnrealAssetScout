using System;
using System.Linq;
using CUE4Parse_Conversion;
using CUE4Parse_Conversion.Options;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Actor;
using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using UnrealAssetScout.Package;

namespace UnrealAssetScout.Export.Exporters;

// Exports supported model- and animation-mode Unreal assets to disk via CUE4Parse_Conversion.
// Called by package-mode processors such as ModelsPackageProcessor and AnimationsPackageProcessor
// when a package export matches one of the supported conversion asset types.
internal static class ConversionExporter
{
    internal static ExportAttemptResult TryExportModel(
        UObject export,
        PackageExportContext packageContext,
        string outputDir,
        ConversionAssetFormat assetFormat)
    {
        if (export is not (UMaterialInterface or USkeletalMesh or USkeleton or UStaticMesh or ALandscapeProxy))
            return ExportAttemptResult.NotHandled();

        return TryExport(export, packageContext, outputDir, assetFormat);
    }

    internal static ExportAttemptResult TryExportAnimation(
        UObject export,
        PackageExportContext packageContext,
        string outputDir,
        ConversionAssetFormat assetFormat)
    {
        if (export is not (UAnimSequence or UAnimMontage or UAnimComposite))
            return ExportAttemptResult.NotHandled();

        return TryExport(export, packageContext, outputDir, assetFormat);
    }

    private static ExportAttemptResult TryExport(
        UObject export,
        PackageExportContext packageContext,
        string outputDir,
        ConversionAssetFormat assetFormat)
    {

        try
        {
            var session = new ExportSession { MaxDegreeOfParallelism = 1 };
            session.Add(export);

            // Package processors expose a synchronous contract. Keep that contract while using
            // CUE4Parse's current session-based asynchronous exporter internally.
            var meshFormat = ResolveMeshFormat(assetFormat);
            var results = session.RunAsync(outputDir, new ExportOptions(meshFormat)).GetAwaiter().GetResult();
            var result = results.FirstOrDefault();
            if (result is null)
                return ExportAttemptResult.NotHandled();

            if (!result.Success)
                return ExportAttemptResult.Failure(
                    $"{packageContext.Path}/{export.Name}",
                    result.Error?.ToString() ?? "CUE4Parse conversion failed without an error");

            var savedFilePaths = result.DiskFilePaths?
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (savedFilePaths is not { Count: > 0 })
                return ExportAttemptResult.NotHandled();

            var logPath = $"{packageContext.Path}/{export.Name}";
            return ExportAttemptResult.Success(savedFilePaths
                .Select(path => new ExportedArtifact(logPath, path))
                .ToList());
        }
        catch (Exception e)
        {
            // Preserve the exception type and stack trace. Conversion failures are often
            // asset-format-specific (for example an animation codec or track-layout issue),
            // and the message alone is not enough to identify the failing CUE4Parse layer.
            return ExportAttemptResult.Failure($"{packageContext.Path}/{export.Name}", e.ToString());
        }
    }

    internal static EMeshFormat ResolveMeshFormat(ConversionAssetFormat assetFormat) => assetFormat switch
    {
        ConversionAssetFormat.UEFormat => EMeshFormat.UEFormat,
        ConversionAssetFormat.ActorX => EMeshFormat.ActorX,
        _ => throw new ArgumentOutOfRangeException(nameof(assetFormat), assetFormat, "Unsupported conversion asset format")
    };
}
