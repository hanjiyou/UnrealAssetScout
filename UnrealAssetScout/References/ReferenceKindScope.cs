namespace UnrealAssetScout.References;

// Selects which statically serialized reference layers the refs command inspects.
internal enum ReferenceKindScope
{
    Package,
    Properties,
    Bytecode,
    All
}
