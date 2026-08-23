using UnrealAssetScout.References;

namespace UnrealAssetScout.Tests;

// Verifies that reference layers are reused only for the exact current container fingerprint.
public sealed class ReferenceIndexReusePolicyTests
{
    [Fact]
    public void MatchingFingerprint_ReusesOnlyCompletedLayers()
    {
        var source = new ReferenceIndexSource
        {
            Fingerprint = "current",
            PackageComplete = true,
            PropertiesComplete = false,
            BytecodeComplete = true
        };

        Assert.True(ReferenceIndexReusePolicy.CanReusePackage(source, "current"));
        Assert.False(ReferenceIndexReusePolicy.CanReuseProperties(source, "current"));
        Assert.True(ReferenceIndexReusePolicy.CanReuseBytecode(source, "current"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("changed")]
    public void MissingOrChangedFingerprint_NeverReuses(string? currentFingerprint)
    {
        var source = new ReferenceIndexSource
        {
            Fingerprint = "original",
            PackageComplete = true,
            PropertiesComplete = true,
            BytecodeComplete = true
        };

        Assert.False(ReferenceIndexReusePolicy.CanReusePackage(source, currentFingerprint));
        Assert.False(ReferenceIndexReusePolicy.CanReuseProperties(source, currentFingerprint));
        Assert.False(ReferenceIndexReusePolicy.CanReuseBytecode(source, currentFingerprint));
    }
}
