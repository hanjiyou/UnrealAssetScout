namespace UnrealAssetScout.Export;

// Selects the CUE4Parse conversion family used by model and animation export modes.
// Kept narrower than CUE4Parse's EMeshFormat because UnrealAssetScout only guarantees the
// ActorX and UEFormat artifact families through its CLI and incremental manifest.
internal enum ConversionAssetFormat
{
    UEFormat,
    ActorX
}
