using System;

namespace UnrealAssetScout.References;

// Decides which cached reference layers are safe to reuse for one unchanged source package.
internal static class ReferenceIndexReusePolicy
{
    internal static bool HasMatchingFingerprint(
        ReferenceIndexSource? source,
        string? currentFingerprint) =>
        source is not null &&
        currentFingerprint is not null &&
        string.Equals(source.Fingerprint, currentFingerprint, StringComparison.Ordinal);

    internal static bool CanReusePackage(
        ReferenceIndexSource? source,
        string? currentFingerprint) =>
        HasMatchingFingerprint(source, currentFingerprint) && source!.PackageComplete;

    internal static bool CanReuseProperties(
        ReferenceIndexSource? source,
        string? currentFingerprint) =>
        HasMatchingFingerprint(source, currentFingerprint) && source!.PropertiesComplete;

    internal static bool CanReuseBytecode(
        ReferenceIndexSource? source,
        string? currentFingerprint) =>
        HasMatchingFingerprint(source, currentFingerprint) && source!.BytecodeComplete;
}
