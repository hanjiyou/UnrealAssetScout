namespace UnrealAssetScout.References;

// Selects which side of a package-level dependency query is returned by the refs command.
internal enum ReferenceDirection
{
    Incoming,
    Outgoing,
    Both
}
