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
    internal static ExportAttemptResult TryExportModel(UObject export, PackageExportContext packageContext, string outputDir)
    {
        if (export is not (UMaterialInterface or USkeletalMesh or USkeleton or UStaticMesh or ALandscapeProxy))
            return ExportAttemptResult.NotHandled();

        return TryExport(export, packageContext, outputDir);
    }

    internal static ExportAttemptResult TryExportAnimation(UObject export, PackageExportContext packageContext, string outputDir)
    {
        if (export is not (UAnimSequence or UAnimMontage or UAnimComposite))
            return ExportAttemptResult.NotHandled();

        return TryExport(export, packageContext, outputDir);
    }

    private static ExportAttemptResult TryExport(UObject export, PackageExportContext packageContext, string outputDir)
    {

        try
        {
            var session = new ExportSession { MaxDegreeOfParallelism = 1 };
            session.Add(export);

            // Package processors expose a synchronous contract. Keep that contract while using
            // CUE4Parse's current session-based asynchronous exporter internally.
            var results = session.RunAsync(outputDir, new ExportOptions()).GetAwaiter().GetResult();
            var result = results.FirstOrDefault();
            if (result is null)
                return ExportAttemptResult.NotHandled();

            if (!result.Success)
                return ExportAttemptResult.Failure(
                    $"{packageContext.Path}/{export.Name}",
                    result.Error?.ToString() ?? "CUE4Parse conversion failed without an error");

            var savedFilePath = result.DiskFilePaths?.FirstOrDefault();
            if (string.IsNullOrEmpty(savedFilePath))
                return ExportAttemptResult.NotHandled();

            return ExportAttemptResult.Success($"{packageContext.Path}/{export.Name}", savedFilePath);
        }
        catch (Exception e)
        {
            // Preserve the exception type and stack trace. Conversion failures are often
            // asset-format-specific (for example an animation codec or track-layout issue),
            // and the message alone is not enough to identify the failing CUE4Parse layer.
            return ExportAttemptResult.Failure($"{packageContext.Path}/{export.Name}", e.ToString());
        }
    }
}
