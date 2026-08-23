using UnrealAssetScout.References;

namespace UnrealAssetScout.Tests;

// Verifies exact incoming and outgoing package-level reference query behavior.
public class PackageReferenceQueryTests
{
    private static readonly PackageReferenceEdge[] Edges =
    [
        new("PioneerGame/Content/SourceA.uasset", "/Game/Target", "PioneerGame/Content/Target.uasset"),
        new("PioneerGame/Content/Target.uasset", "/Game/Other", "PioneerGame/Content/Other.uasset"),
        new("PioneerGame/Content/SourceB.uasset", "packageid:42", null)
    ];

    [Fact]
    public void Find_Incoming_MatchesResolvedPathAndIdentity()
    {
        var byPath = PackageReferenceQuery.Find(
            Edges,
            "PioneerGame\\Content\\Target.uasset",
            ReferenceDirection.Incoming).ToList();
        var byIdentity = PackageReferenceQuery.Find(
            Edges,
            "/Game/Target",
            ReferenceDirection.Incoming).ToList();

        Assert.Single(byPath);
        Assert.Single(byIdentity);
        Assert.Equal("PioneerGame/Content/SourceA.uasset", byPath[0].Edge.SourcePath);
    }

    [Fact]
    public void Find_Both_ReturnsIncomingAndOutgoingMatches()
    {
        var matches = PackageReferenceQuery.Find(
            Edges,
            "PioneerGame/Content/Target.uasset",
            ReferenceDirection.Both).ToList();

        Assert.Equal(2, matches.Count);
        Assert.Contains(matches, match => match.Direction == ReferenceDirection.Incoming);
        Assert.Contains(matches, match => match.Direction == ReferenceDirection.Outgoing);
    }

    [Fact]
    public void Find_Incoming_PreservesUnresolvedIdentity()
    {
        var matches = PackageReferenceQuery.Find(
            Edges,
            "packageid:42",
            ReferenceDirection.Incoming).ToList();

        Assert.Single(matches);
        Assert.Null(matches[0].Edge.TargetPath);
    }
}
