using UnrealAssetScout.References;

namespace UnrealAssetScout.Tests;

// Verifies exact incoming and outgoing serialized property reference query behavior.
public class PropertyReferenceQueryTests
{
    private static readonly PropertyReferenceEdge[] Edges =
    [
        new(
            "PioneerGame/Content/Source.uasset",
            PropertyReferenceReader.SoftObjectProperty,
            "/Game/Target.Target",
            "PioneerGame/Content/Target.uasset",
            "Source.TargetAnimation")
    ];

    [Fact]
    public void Find_Incoming_MatchesResolvedPackagePath()
    {
        var match = Assert.Single(PropertyReferenceQuery.Find(
            Edges,
            "PioneerGame/Content/Target.uasset",
            ReferenceDirection.Incoming));

        Assert.Equal(ReferenceDirection.Incoming, match.Direction);
        Assert.Equal("Source.TargetAnimation", match.Edge.EvidencePath);
    }

    [Fact]
    public void Find_Outgoing_MatchesSourcePackage()
    {
        var match = Assert.Single(PropertyReferenceQuery.Find(
            Edges,
            "PioneerGame/Content/Source.uasset",
            ReferenceDirection.Outgoing));

        Assert.Equal(ReferenceDirection.Outgoing, match.Direction);
    }
}
