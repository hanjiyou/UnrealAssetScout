namespace UnrealAssetScout.References;

// Represents one hard or soft object identity encoded in parsed Blueprint/Kismet bytecode.
internal sealed record KismetReferenceEdge(
    string SourcePath,
    string Kind,
    string TargetIdentity,
    string? TargetPath,
    string EvidencePath);
