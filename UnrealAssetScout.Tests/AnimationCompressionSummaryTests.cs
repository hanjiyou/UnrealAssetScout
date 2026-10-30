using System.Buffers.Binary;
using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse.UE4.Assets.Exports.Animation.ACL;
using UnrealAssetScout.Export.Processors;

namespace UnrealAssetScout.Tests;

public sealed class AnimationCompressionSummaryTests
{
    private static UAnimSequence Sequence(uint header, int frames = 31)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, header);
        return new UAnimSequence
        {
            NumFrames = frames,
            CompressedTrackToSkeletonMapTable = [new() { BoneTreeIndex = 1 }],
            CompressedDataStructure = new FUECompressedAnimData
            {
                CompressedNumberOfFrames = frames,
                KeyEncodingFormat = AnimationKeyFormat.AKF_PerTrackCompression,
                CompressedTrackOffsets = [0, -1],
                CompressedByteStream = bytes
            }
        };
    }

    [Fact]
    public void RealF0246HeaderIsIdentifiedWithoutDecodingAnyKey()
    {
        var result = AnimationsPackageProcessor.SummarizeCompression(Sequence(0x12000002), "/Game/F0246");
        Assert.Equal("direct_uniform_sparse", result["status"]);
        var channel = Assert.Single(Assert.IsType<List<Dictionary<string, object?>>>(result["uniform_sparse_channels"]));
        Assert.Equal("0x12000002", channel["header_hex"]);
        Assert.Equal(2, channel["num_keys"]);
        Assert.Equal(1, channel["bone_tree_index"]);
        Assert.Equal(false, channel["has_time_array"]);
        Assert.Equal(2, channel["component_mask"]);
    }

    [Theory]
    [InlineData(0x1A000002u)] // Explicit time array, despite K<N.
    [InlineData(0x12000001u)] // Constant channel.
    [InlineData(0x1200001Fu)] // Dense uniform K=N.
    public void ExplicitTimedConstantAndDenseHeadersAreNotSparse(uint header)
    {
        var result = AnimationsPackageProcessor.SummarizeCompression(Sequence(header), "/Game/Test");
        Assert.Equal("no_uniform_sparse_per_track", result["status"]);
        Assert.Equal(false, result["direct_affected"]);
    }

    [Fact]
    public void NonPerTrackRemainsUnclassifiedInsteadOfBeingDeclaredUnaffected()
    {
        var sequence = Sequence(0x12000002);
        ((FUECompressedAnimData)sequence.CompressedDataStructure!).KeyEncodingFormat = AnimationKeyFormat.AKF_MAX;
        var result = AnimationsPackageProcessor.SummarizeCompression(sequence, "/Game/Test");
        Assert.Equal("codec_non_per_track", result["status"]);
        Assert.Null(result["direct_affected"]);
    }

    [Fact]
    public void TruncatedHeaderIsRejected()
    {
        var sequence = Sequence(0x12000002);
        ((FUECompressedAnimData)sequence.CompressedDataStructure!).CompressedTrackOffsets[0] = 4;
        Assert.Throws<InvalidDataException>(() => AnimationsPackageProcessor.SummarizeCompression(sequence, "/Game/Test"));
    }

    [Fact]
    public void MoreKeysThanFramesIsNotSilentlyClassifiedUnaffected()
    {
        Assert.Throws<InvalidDataException>(() => AnimationsPackageProcessor.SummarizeCompression(Sequence(0x12000020), "/Game/Test"));
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(1, false)]
    [InlineData(31, false)]
    public void ConstantKeyLerpUsesOriginalOffsetTableCounts(int keys, bool affected)
    {
        var sequence = Sequence(0x12000002);
        var data = (FUECompressedAnimData)sequence.CompressedDataStructure!;
        data.KeyEncodingFormat = AnimationKeyFormat.AKF_ConstantKeyLerp;
        data.CompressedTrackOffsets = [0, keys, 0, 1];
        var result = AnimationsPackageProcessor.SummarizeCompression(sequence, "/Game/Test");
        Assert.Equal(affected, result["direct_affected"]);
    }

    [Theory]
    [InlineData(31, false, false)]
    [InlineData(30, true, false)]
    [InlineData(1, false, false)]
    [InlineData(2, false, true)]
    public void AclSourceHeaderAccountsForTheWrapSampleWithoutPoseDecode(int samples, bool wraps, bool affected)
    {
        var bytes = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 32);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 0xAC11AC11);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), (uint)samples);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), wraps ? 1u << 30 : 0);
        var sequence = Sequence(0x12000002);
        sequence.CompressedDataStructure = new FACLCompressedAnimData
            { CompressedNumberOfFrames = 31, CompressedByteStream = bytes };
        var result = AnimationsPackageProcessor.SummarizeCompression(sequence, "/Game/Test");
        Assert.Equal(affected, result["direct_affected"]);
        Assert.Equal(samples + (wraps ? 1 : 0), result["CUE_expanded_channel_keys"]);
    }

    [Fact]
    public void DiagnosticCliSelectionIsExplicitAndRejectsAnimationExportMode()
    {
        using var directory = new TempDir();
        var selection = directory.File("selection.json");
        string[] Args(string mode) => ["export", mode, "--paks", directory.Path, "--game", "GAME_ArcRaiders",
            "--output", Path.Combine(directory.Path, "output"), "--animation-compression-summary", selection, "--no-log"];
        var valid = ConfigOptionsSupport.ParseArgs(Args("json"));
        Assert.NotNull(valid);
        Assert.Equal(selection, valid.AnimationCompressionSummarySelection);
        Assert.Null(ConfigOptionsSupport.ParseArgs(Args("animations")));
    }
}
