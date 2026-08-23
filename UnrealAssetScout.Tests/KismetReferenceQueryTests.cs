using UnrealAssetScout.References;

namespace UnrealAssetScout.Tests;

// Verifies exact incoming and outgoing matching for parsed Kismet edges.
public sealed class KismetReferenceQueryTests
{
    [Fact]
    public void Find_Both_ReturnsIncomingAndOutgoingMatches()
    {
        KismetReferenceEdge[] edges =
        [
            new(
                "PioneerGame/Content/Source.uasset",
                KismetReferenceReader.HardObjectReference,
                "/Game/Target.Target",
                "PioneerGame/Content/Target.uasset",
                "Function.ScriptBytecode[0].Value"),
            new(
                "PioneerGame/Content/Target.uasset",
                KismetReferenceReader.SoftObjectReference,
                "/Game/Other.Other",
                "PioneerGame/Content/Other.uasset",
                "Function.ScriptBytecode[1].Value")
        ];

        var matches = KismetReferenceQuery.Find(
            edges,
            "PioneerGame/Content/Target.uasset",
            ReferenceDirection.Both).ToList();

        Assert.Equal(2, matches.Count);
        Assert.Contains(matches, match => match.Direction == ReferenceDirection.Incoming);
        Assert.Contains(matches, match => match.Direction == ReferenceDirection.Outgoing);
    }
}
