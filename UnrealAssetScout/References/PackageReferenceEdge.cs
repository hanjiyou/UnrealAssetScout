namespace UnrealAssetScout.References;

// Represents one package-level hard import discovered without deserializing imported objects.
internal sealed record PackageReferenceEdge(
    string SourcePath,
    string TargetIdentity,
    string? TargetPath);
