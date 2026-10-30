using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse_Conversion.Writers.ActorX.Structs.Animations;

namespace UnrealAssetScout.Tests;

public sealed class CAnimTrackUniformSamplingTests
{
    private static float SampleY(CAnimTrack track, float frame, int frameCount)
    {
        var rotation = FQuat.Identity;
        var position = new FVector(11, 12, 13);
        var scale = FVector.OneVector;
        track.GetBoneTransform(frame, frameCount, ref rotation, ref position, ref scale);
        return position.Y;
    }

    private static CAnimTrack Translation(params float[] values) => new()
    {
        KeyPos = values.Select(y => new FVector(0, y, 0)).ToArray()
    };

    [Theory]
    [InlineData(-5f)]
    [InlineData(0f)]
    [InlineData(14.5f)]
    [InlineData(30f)]
    [InlineData(99f)]
    public void SingleKeyIsConstant(float frame)
    {
        Assert.Equal(7f, SampleY(Translation(7), frame, 31));
    }

    [Fact]
    public void EmptyChannelRetainsTheSuppliedTransform()
    {
        Assert.Equal(12f, SampleY(Translation(), 15, 31));
    }

    [Theory]
    [InlineData(-2f, 0f)]
    [InlineData(0f, 0f)]
    [InlineData(7.5f, 2.5f)]
    [InlineData(15f, 5f)]
    [InlineData(16f, 5.3333335f)]
    [InlineData(29.5f, 9.833333f)]
    [InlineData(30f, 10f)]
    [InlineData(45f, 10f)]
    public void TwoUniformKeysSpanTheWholeInclusiveSequence(float frame, float expected)
    {
        Assert.InRange(Math.Abs(SampleY(Translation(0, 10), frame, 31) - expected), 0, 1e-5);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(3.75f, 3.75f)]
    [InlineData(7.5f, 7.5f)]
    [InlineData(10f, 10f)]
    [InlineData(15f, 15f)]
    [InlineData(30f, 30f)]
    public void MultipleSparseKeysRetainTheirUniformInterval(float frame, float expected)
    {
        Assert.InRange(Math.Abs(SampleY(Translation(0, 10, 20, 30), frame, 31) - expected), 0, 1e-5);
    }

    [Fact]
    public void DenseKeysStillUseTheirOriginalFrameCoordinates()
    {
        var track = Translation(0, 10, 20, 30, 40, 50, 60, 70);
        Assert.Equal(32.5f, SampleY(track, 3.25f, 8));
        Assert.Equal(70f, SampleY(track, 7, 8));
    }

    [Fact]
    public void OneFrameSequenceUsesTheFirstKey()
    {
        Assert.Equal(3f, SampleY(Translation(3, 8), 10, 1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitKeyTimesStillControlInterpolation(bool sharedTimes)
    {
        var track = Translation(0, 12, 30);
        if (sharedTimes) track.KeyTime = [0, 6, 30];
        else track.KeyPosTime = [0, 6, 30];
        Assert.Equal(6f, SampleY(track, 3, 31));
        Assert.Equal(21f, SampleY(track, 18, 31));
        Assert.Equal(30f, SampleY(track, 30, 31));
    }

    [Fact]
    public void MismatchedSharedTimesUseTheUniformChannelDomain()
    {
        var track = Translation(0, 10);
        track.KeyTime = Enumerable.Range(0, 31).Select(i => (float)i).ToArray();
        Assert.Equal(5f, SampleY(track, 15, 31));
        Assert.Equal(10f, SampleY(track, 30, 31));
    }

    [Fact]
    public void F0246RealSparsePelvisKeysMatchIndependentUeUniformTimeToIndex()
    {
        // Original compressed package: offset 0x8AB, header 0x12000002,
        // Float96NoW/Y-only/K=2/no time array. These are its two real float keys.
        const float first = 7.025992393493652f;
        const float last = -7.494391918182373f;
        const int sourceFrames = 31;
        var track = Translation(first, last);

        for (var frame = 0; frame < sourceFrames; frame++)
        {
            // UE AnimEncoding.h: RelativePos in [0,1], KeyPos=RelativePos*(K-1).
            var relativePosition = (double)frame / (sourceFrames - 1);
            var expected = first + ((double)last - first) * relativePosition;
            Assert.InRange(Math.Abs(SampleY(track, frame, sourceFrames) - expected), 0, 2e-6);
        }

        // The authored local f=15 reference is evaluated at f/NumKeys in UE.
        // Convert that normalized time to CAnimTrack's inclusive frame domain.
        var referenceFrameCoordinate = 15f / sourceFrames * (sourceFrames - 1);
        var expectedReferenceDelta = first + ((double)last - first) * (15d / sourceFrames);
        Assert.InRange(Math.Abs(SampleY(track, referenceFrameCoordinate, sourceFrames) - expectedReferenceDelta), 0, 2e-6);
        Assert.InRange(Math.Abs(expectedReferenceDelta), 0, 2e-8);
        Assert.NotEqual(last, SampleY(track, 16, sourceFrames));
    }
}
