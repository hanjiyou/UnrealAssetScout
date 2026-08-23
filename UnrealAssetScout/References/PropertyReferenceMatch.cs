namespace UnrealAssetScout.References;

// Adds incoming/outgoing query direction to a serialized property reference edge.
internal sealed record PropertyReferenceMatch(
    ReferenceDirection Direction,
    PropertyReferenceEdge Edge);
