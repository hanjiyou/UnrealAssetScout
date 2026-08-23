namespace UnrealAssetScout.References;

// Adds the query-relative direction to a package reference edge for presentation and tests.
internal sealed record PackageReferenceMatch(
    ReferenceDirection Direction,
    PackageReferenceEdge Edge);
