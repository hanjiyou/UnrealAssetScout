namespace UnrealAssetScout.References;

// Adds incoming/outgoing query direction to one parsed Kismet bytecode reference edge.
internal sealed record KismetReferenceMatch(
    ReferenceDirection Direction,
    KismetReferenceEdge Edge);
