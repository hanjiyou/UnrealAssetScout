using UnrealAssetScout.References;

namespace UnrealAssetScout.Tests;

// Verifies the game, mappings, and parser identity gate for persisted reference data.
public sealed class ReferenceIndexIdentityTests
{
    [Fact]
    public void Matches_RequiresEveryIdentityFieldAndSchema()
    {
        var identity = new ReferenceIndexIdentity("GAME_ArcRaiders", "usmap", "tool");
        var document = identity.NewDocument();

        Assert.True(identity.Matches(document));

        document.UsmapSha256 = "changed";
        Assert.False(identity.Matches(document));
    }
}
