using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace UnrealAssetScout.References;

// Loads and atomically checkpoints the reusable reference index selected by --index.
internal static class ReferenceIndexStore
{
    internal const int CurrentSchema = 2;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    internal static ReferenceIndexDocument? TryLoad(string path, out string? error)
    {
        error = null;
        if (!File.Exists(path))
            return null;

        try
        {
            var document = JsonSerializer.Deserialize<ReferenceIndexDocument>(
                File.ReadAllText(path),
                SerializerOptions);
            if (document is null)
            {
                error = $"reference index at {path} is empty";
                return null;
            }

            document.Sources = new(
                document.Sources ?? [],
                StringComparer.OrdinalIgnoreCase);
            return document;
        }
        catch (Exception e)
        {
            error = $"reference index at {path} could not be parsed: {e.Message}";
            return null;
        }
    }

    internal static void Save(string path, ReferenceIndexDocument document)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var tempPath = fullPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(document, SerializerOptions));
        File.Move(tempPath, fullPath, overwrite: true);
    }
}
