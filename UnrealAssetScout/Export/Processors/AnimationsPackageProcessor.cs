using CUE4Parse.UE4.Assets.Exports;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Runtime.InteropServices;
using CUE4Parse.ACL;
using CUE4Parse.FileProvider.Vfs;
using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse.UE4.Assets.Exports.Animation.ACL;
using UnrealAssetScout.Export.Exporters;
using UnrealAssetScout.Package;
using UnrealAssetScout.Statistics;

namespace UnrealAssetScout.Export.Processors;

// Processes package exports for animations mode.
// Created by ExportProcessor.ProcessFiles for ExportMode.Animations, then passed to
// ExportProcessor.ProcessPackageMode to export supported animation assets from each loaded package.
internal sealed class AnimationsPackageProcessor(
    string outputDir,
    bool verbose,
    ModeStatsAccumulator modeStats,
    ConversionAssetFormat assetFormat)
    : PackageModeProcessorBase(outputDir, verbose, modeStats)
{
    protected override ExportAttemptResult TryExport(UObject export, PackageExportContext packageContext) =>
        ConversionExporter.TryExportAnimation(export, packageContext, OutputDir, assetFormat);

    protected override string NoExportsReason => "no animation exports";

    internal static Dictionary<string, object?> SummarizeCompression(UAnimSequence sequence, string source)
    {
        var result = new Dictionary<string, object?>
        {
            ["source_object"] = source, ["num_frames"] = sequence.NumFrames,
            ["codec"] = sequence.CompressedDataStructure?.GetType().Name,
            ["bone_codec_ddc_handle"] = sequence.BoneCodecDDCHandle,
            ["additive_type"] = sequence.AdditiveAnimType.ToString(),
            ["ref_pose_type"] = sequence.RefPoseType.ToString(),
            ["ref_frame_index"] = sequence.RefFrameIndex,
            ["direct_ref_pose_sequence"] = sequence.RefPoseSeq?.GetPathName(),
            ["direct_affected"] = null
        };
        if (sequence.CompressedDataStructure is FACLCompressedAnimData acl)
        {
            var headerSize = Marshal.SizeOf<RawBufferHeader>();
            if (acl.CompressedByteStream.Length < headerSize + Marshal.SizeOf<TracksHeader>())
                throw new InvalidDataException("Truncated original ACL tracks header.");
            var header = MemoryMarshal.Read<TracksHeader>(acl.CompressedByteStream.AsSpan(headerSize));
            if (header.Tag != 0xAC11AC11 || header.NumTracks != sequence.CompressedTrackToSkeletonMapTable.Length || header.NumSamples < 1)
                throw new InvalidDataException("Original ACL tag/track/sample count is inconsistent.");
            var keys = checked((int)header.NumSamples + (header.GetIsWrapOptimized() ? 1 : 0));
            if (keys > sequence.NumFrames) throw new InvalidDataException("ACL-expanded keys exceed original sequence frame count.");
            result["acl_num_samples"] = header.NumSamples;
            result["acl_wrap_optimized"] = header.GetIsWrapOptimized();
            result["CUE_expanded_channel_keys"] = keys;
            result["direct_affected"] = keys > 1 && keys < sequence.NumFrames;
            result["status"] = keys > 1 && keys < sequence.NumFrames
                ? "direct_uniform_sparse_acl_header_count" : "no_uniform_sparse_acl_dense_or_constant";
            return result;
        }
        if (sequence.CompressedDataStructure is FUECompressedAnimData keyLerp &&
            keyLerp.KeyEncodingFormat is AnimationKeyFormat.AKF_ConstantKeyLerp or AnimationKeyFormat.AKF_VariableKeyLerp)
        {
            result["key_encoding"] = keyLerp.KeyEncodingFormat.ToString();
            if (keyLerp.KeyEncodingFormat == AnimationKeyFormat.AKF_VariableKeyLerp)
            {
                result["status"] = "explicit_time_key_lerp"; result["direct_affected"] = false;
                result["basis"] = "ReadKeyLerpData passes hasTimeTracks=true and reads per-channel key-time arrays; unchanged GetKeyParamsInternal path.";
                return result;
            }
            if (keyLerp.CompressedTrackOffsets.Length % 4 != 0 ||
                keyLerp.CompressedTrackOffsets.Length / 4 != sequence.CompressedTrackToSkeletonMapTable.Length)
                throw new InvalidDataException("Original key-lerp offset/track table is inconsistent.");
            var sparseCounts = new List<Dictionary<string, object?>>();
            for (var track = 0; track < sequence.CompressedTrackToSkeletonMapTable.Length; track++)
            {
                var counts = new[] { keyLerp.CompressedTrackOffsets[track * 4 + 1], keyLerp.CompressedTrackOffsets[track * 4 + 3],
                    keyLerp.CompressedScaleOffsets.IsValid() ? keyLerp.CompressedScaleOffsets.OffsetData[track * 2 + 1] : 0 };
                for (var channel = 0; channel < counts.Length; channel++)
                {
                    var keys = counts[channel];
                    if (keys < 0 || keys > sequence.NumFrames) throw new InvalidDataException("Original constant-key-lerp key count is invalid.");
                    if (keys > 1 && keys < sequence.NumFrames)
                        sparseCounts.Add(new Dictionary<string, object?> { ["track_index"] = track,
                            ["bone_tree_index"] = sequence.CompressedTrackToSkeletonMapTable[track].BoneTreeIndex,
                            ["channel"] = new[] { "translation", "rotation", "scale" }[channel],
                            ["num_keys"] = keys, ["has_time_array"] = false,
                            ["basis"] = "Original constant-key-lerp offset table key count; no packed per-track header." });
                }
            }
            result["uniform_sparse_channels"] = sparseCounts; result["direct_affected"] = sparseCounts.Count > 0;
            result["status"] = sparseCounts.Count > 0 ? "direct_uniform_sparse_constant_key_lerp" : "no_uniform_sparse_constant_key_lerp";
            return result;
        }
        if (sequence.CompressedDataStructure is not FUECompressedAnimData data ||
            data.KeyEncodingFormat != AnimationKeyFormat.AKF_PerTrackCompression)
        {
            result["status"] = "codec_non_per_track";
            result["key_encoding"] = sequence.CompressedDataStructure is FUECompressedAnimData other
                ? other.KeyEncodingFormat.ToString() : null;
            result["uniform_sparse_assessment"] = "not_classified_by_per_track_header_diagnostic";
            return result;
        }
        if (sequence.NumFrames <= 0 || data.CompressedNumberOfFrames != sequence.NumFrames ||
            data.CompressedTrackOffsets is null || data.CompressedTrackOffsets.Length % 2 != 0)
            throw new InvalidDataException("Original per-track frame count/offset table is inconsistent.");
        var tracks = data.CompressedTrackOffsets.Length / 2;
        if (sequence.CompressedTrackToSkeletonMapTable.Length != tracks)
            throw new InvalidDataException("Original track-to-bone table does not cover every compressed track.");
        if (data.CompressedScaleOffsets.IsValid() &&
            data.CompressedScaleOffsets.OffsetData.Length < tracks * data.CompressedScaleOffsets.StripSize)
            throw new InvalidDataException("Original scale offset table is truncated.");
        var sparse = new List<Dictionary<string, object?>>();
        var headers = 0; var identity = 0; var explicitTimes = 0; var constant = 0; var nonSparse = 0;
        void Channel(int track, string kind, int offset)
        {
            if (offset == -1) { identity++; return; }
            if (offset < 0 || data.CompressedByteStream is null || offset > data.CompressedByteStream.Length - 4)
                throw new InvalidDataException($"Original {kind} header offset {offset} is outside the compressed stream.");
            var header = BinaryPrimitives.ReadUInt32LittleEndian(data.CompressedByteStream.AsSpan(offset, 4));
            var keys = (int)(header & 0xFFFFFF);
            var format = (int)(header >> 28);
            var flags = (int)((header >> 24) & 0xF);
            if (keys < 1 || format >= (int)AnimationCompressionFormat.ACF_MAX)
                throw new InvalidDataException($"Invalid original {kind} header 0x{header:X8} at {offset}.");
            headers++;
            if ((flags & 8) != 0) { explicitTimes++; return; }
            if (keys == 1) { constant++; return; }
            if (keys > sequence.NumFrames)
                throw new InvalidDataException("Uniform channel has more keys than source frames; do not classify it as unaffected.");
            if (keys == sequence.NumFrames) { nonSparse++; return; }
            sparse.Add(new Dictionary<string, object?>
            {
                ["track_index"] = track,
                ["bone_tree_index"] = sequence.CompressedTrackToSkeletonMapTable[track].BoneTreeIndex,
                ["channel"] = kind, ["byte_stream_offset"] = offset,
                ["header_hex"] = $"0x{header:X8}", ["num_keys"] = keys,
                ["has_time_array"] = false, ["component_mask"] = flags & 7,
                ["compression_format"] = ((AnimationCompressionFormat)format).ToString()
            });
        }
        for (var track = 0; track < tracks; track++)
        {
            Channel(track, "translation", data.CompressedTrackOffsets[track * 2]);
            Channel(track, "rotation", data.CompressedTrackOffsets[track * 2 + 1]);
            if (data.CompressedScaleOffsets.IsValid())
                Channel(track, "scale", data.CompressedScaleOffsets.GetOffsetData(track, 0));
        }
        result["key_encoding"] = data.KeyEncodingFormat.ToString();
        result["compressed_byte_stream_size"] = data.CompressedByteStream.Length;
        result["track_count"] = tracks; result["headers_examined"] = headers;
        result["identity_offset_channels"] = identity; result["explicit_time_array_channels"] = explicitTimes;
        result["constant_channels"] = constant; result["dense_or_oversampled_uniform_channels"] = nonSparse;
        result["uniform_sparse_channels"] = sparse; result["direct_affected"] = sparse.Count > 0;
        result["status"] = sparse.Count > 0 ? "direct_uniform_sparse" : "no_uniform_sparse_per_track";
        return result;
    }

    internal static int WriteCompressionSummary(AbstractVfsFileProvider provider, string selectionPath, string outputDir)
    {
        using var selection = JsonDocument.Parse(File.ReadAllText(selectionPath));
        var sources = selection.RootElement.GetProperty("source_objects").EnumerateArray()
            .Select(value => value.GetString() ?? "").ToArray();
        if (sources.Length == 0 || sources.Distinct(StringComparer.OrdinalIgnoreCase).Count() != sources.Length ||
            sources.Any(source => !source.StartsWith("/Game/", StringComparison.Ordinal) || source.Contains("..") || source.Contains('.')))
            throw new InvalidDataException("Header diagnostic requires unique exact /Game/ source package paths.");
        var reportName = selection.RootElement.TryGetProperty("report_name", out var reportNameValue)
            ? reportNameValue.GetString()! : "animation_compression_headers.json";
        if (Path.GetFileName(reportName) != reportName || !reportName.EndsWith(".json", StringComparison.Ordinal))
            throw new InvalidDataException("Diagnostic report name must be a plain JSON filename.");
        var output = Path.Combine(outputDir, reportName);
        if (File.Exists(output)) throw new IOException("Existing diagnostic output is immutable; resume its original operation explicitly.");
        Directory.CreateDirectory(outputDir);
        var rows = new List<Dictionary<string, object?>>();
        var clock = Stopwatch.StartNew();
        for (var index = 0; index < sources.Length; index++)
        {
            var source = sources[index];
            var packagePath = "PioneerGame/Content/" + source[6..] + ".uasset";
            try
            {
                // Direct lookup retains the provider's patch/read-order rule.
                // Values enumerate shadowed base entries as well as patches.
                if (!provider.Files.TryGetValue(packagePath, out var file))
                    throw new FileNotFoundException("Selected original package is not mounted", packagePath);
                var context = PackageLoadSupport.PreparePackageForExport(provider, file, false);
                if (context.Package is null) throw new InvalidDataException("Original package could not be loaded.");
                var sequences = context.Package.GetExports().OfType<UAnimSequence>().ToArray();
                if (sequences.Length != 1 || !sequences[0].GetPathName().StartsWith(source + ".", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Original package does not contain exactly the selected animation sequence.");
                rows.Add(SummarizeCompression(sequences[0], source));
            }
            catch (Exception error)
            {
                rows.Add(new Dictionary<string, object?>
                { ["source_object"] = source, ["status"] = "error", ["direct_affected"] = null, ["error"] = error.Message });
            }
            if (index == 0 || (index + 1) % 50 == 0 || index + 1 == sources.Length)
                Console.Error.WriteLine($"HEADER_SUMMARY {index + 1}/{sources.Length} elapsed={clock.Elapsed.TotalSeconds:F1}s current={source}");
        }
        var errors = rows.Count(row => Equals(row["status"], "error"));
        var report = new
        {
            schema = "uas.original_animation_compression_headers.v1", diagnostic_only = true,
            animation_payload_decoded = false, dense_animation_exported = false,
            selection = Path.GetFullPath(selectionPath), selected_count = sources.Length,
            elapsed_seconds = clock.Elapsed.TotalSeconds,
            categories = rows.GroupBy(row => (string)row["status"]!).ToDictionary(group => group.Key, group => group.Count()),
            errors, rows
        };
        File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.Error.WriteLine($"HEADER_SUMMARY saved={output} errors={errors} elapsed={clock.Elapsed.TotalSeconds:F1}s");
        return errors == 0 ? 0 : 1;
    }
}
