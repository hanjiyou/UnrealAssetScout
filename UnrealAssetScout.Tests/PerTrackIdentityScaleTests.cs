using System.Reflection;
using CUE4Parse_Conversion.Animations;
using CUE4Parse_Conversion.Writers.ActorX.Structs.Animations;
using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse.UE4.Readers;

namespace UnrealAssetScout.Tests;

public sealed class PerTrackIdentityScaleTests
{
    [Theory]
    [InlineData(true, true, 0f)]
    [InlineData(true, false, 0f)]
    [InlineData(false, true, 0f)]
    [InlineData(false, false, 1f)]
    public void IdentityScaleMatchesCompressedTrackAndPoseInitialization(bool hasScaleOffsets, bool additive, float expected)
    {
        // UE's explicit per-track scale offset -1 decodes to zero. With no
        // scale stream, retain the initialized absolute or additive identity.
        var data = new FUECompressedAnimData
        {
            CompressedNumberOfFrames = 1,
            CompressedTrackOffsets = [-1, -1],
            CompressedScaleOffsets = new FCompressedOffsetData(1)
            {
                OffsetData = hasScaleOffsets ? [-1] : []
            },
            TranslationCompressionFormat = AnimationCompressionFormat.ACF_Identity,
            RotationCompressionFormat = AnimationCompressionFormat.ACF_Identity,
            ScaleCompressionFormat = AnimationCompressionFormat.ACF_Identity,
            CompressedByteStream = []
        };
        var sequence = new UAnimSequence
        {
            CompressedDataStructure = data,
            AdditiveAnimType = additive ? EAdditiveAnimationType.AAT_LocalSpaceBase : EAdditiveAnimationType.AAT_None,
            RefPoseType = EAdditiveBasePoseType.ABPT_RefPose
        };
        var track = new CAnimTrack();
        using var reader = new FByteArchive("identity_scale", []);
        typeof(AnimConverter).GetMethod("ReadPerTrackData", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [reader, sequence, track, 0]);
        var scale = Assert.Single(track.KeyScale);
        Assert.Equal(expected, scale.X);
        Assert.Equal(expected, scale.Y);
        Assert.Equal(expected, scale.Z);
    }
}
