using System;
using System.IO;
using System.Linq;
using UnrealAssetScout.Logging;

namespace UnrealAssetScout.Config;

// Resolves AES key configuration from a dedicated key file or standard input.
// Called by ConfigOptionsSupport and Program.Run before provider mounting.
internal static class AesKeyConfigSupport
{
    internal static bool TryReadKeyFile(string rawValue, out string resolvedValue)
    {
        var keyFilePath = Path.GetFullPath(rawValue);
        if (!File.Exists(keyFilePath))
        {
            AppLog.Error("AES key file not found: {Path}", keyFilePath);
            resolvedValue = string.Empty;
            return false;
        }

        try
        {
            resolvedValue = File.ReadLines(keyFilePath).First().Trim();
            return true;
        }
        catch (Exception e)
        {
            AppLog.Error("Failed to read AES key file '{Path}': {Message}", keyFilePath, e.Message);
            resolvedValue = string.Empty;
            return false;
        }
    }

    internal static bool TryReadKeyFromStandardInput(TextReader reader, out string? resolvedValue)
    {
        try
        {
            resolvedValue = reader.ReadLine()?.Trim();
            if (!string.IsNullOrWhiteSpace(resolvedValue))
                return true;

            AppLog.Error("No AES key was received on standard input.");
            resolvedValue = null;
            return false;
        }
        catch (Exception e)
        {
            AppLog.Error("Failed to read AES key from standard input: {Message}", e.Message);
            resolvedValue = null;
            return false;
        }
    }
}
