namespace UnrealAssetScout.References;

// Represents one hard or soft object reference found in a serialized export property.
internal sealed record PropertyReferenceEdge(
    string SourcePath,
    string Kind,
    string TargetIdentity,
    string? TargetPath,
    string EvidencePath);
