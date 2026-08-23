using System;
using System.IO;
using System.Security.Cryptography;
using CUE4Parse.FileProvider.Vfs;
using CUE4Parse.UE4.Versions;

namespace UnrealAssetScout.References;

// Fingerprints the exact parser binaries and mappings that determine reference-scan output.
internal sealed record ReferenceIndexIdentity(
    string Game,
    string UsmapSha256,
    string ToolFingerprint)
{
    internal static ReferenceIndexIdentity Create(EGame game, string? usmapPath) => new(
        game.ToString(),
        usmapPath is null ? "none" : Sha256(usmapPath),
        $"uas:{Sha256(typeof(ReferenceIndexIdentity).Assembly.Location)};" +
        $"cue4parse:{Sha256(typeof(AbstractVfsFileProvider).Assembly.Location)}");

    internal bool Matches(ReferenceIndexDocument document) =>
        document.Schema == ReferenceIndexStore.CurrentSchema &&
        string.Equals(Game, document.Game, StringComparison.Ordinal) &&
        string.Equals(UsmapSha256, document.UsmapSha256, StringComparison.Ordinal) &&
        string.Equals(ToolFingerprint, document.ToolFingerprint, StringComparison.Ordinal);

    internal ReferenceIndexDocument NewDocument() => new()
    {
        Game = Game,
        UsmapSha256 = UsmapSha256,
        ToolFingerprint = ToolFingerprint
    };

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
