using System;
using System.IO;
using CUE4Parse.FileProvider;
using CUE4Parse.GameTypes.Theia.FileProvider;
using CUE4Parse.UE4.Versions;

namespace UnrealAssetScout;

// Selects the file-provider implementation required by each supported game.
// Called by Program.Run before mappings, keys, and mounted-container options are applied.
internal static class ProviderFactory
{
    internal static DefaultFileProvider Create(string paksDirectory, EGame game)
    {
        var versions = new VersionContainer(game);
        return game switch
        {
            EGame.GAME_ArcRaiders => new TheiaFileProvider(
                paksDirectory,
                SearchOption.TopDirectoryOnly,
                versions,
                StringComparer.OrdinalIgnoreCase),
            _ => new DefaultFileProvider(
                paksDirectory,
                SearchOption.TopDirectoryOnly,
                versions,
                StringComparer.OrdinalIgnoreCase)
        };
    }
}
