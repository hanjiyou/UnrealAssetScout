using CUE4Parse.FileProvider;
using CUE4Parse.GameTypes.Theia.FileProvider;
using CUE4Parse.UE4.Versions;

namespace UnrealAssetScout.Tests;

// Verifies that game-specific provider selection does not silently fall back to the generic provider.
// Exercises ProviderFactory without mounting containers or starting a game.
public class ProviderFactoryTests
{
    [Fact]
    public void Create_ArcRaiders_ReturnsTheiaProvider()
    {
        var provider = ProviderFactory.Create("C:\\Paks", EGame.GAME_ArcRaiders);

        Assert.IsType<TheiaFileProvider>(provider);
        Assert.Equal(EGame.GAME_UE5_7.GetVersion(), provider.Versions.Ver);
        Assert.True(EGame.GAME_ArcRaiders >= EGame.GAME_UE5_7 && EGame.GAME_ArcRaiders < EGame.GAME_UE5_8);
    }

    [Fact]
    public void Create_GenericGame_ReturnsDefaultProvider()
    {
        var provider = ProviderFactory.Create("C:\\Paks", EGame.GAME_UE5_3);

        Assert.IsType<DefaultFileProvider>(provider);
    }
}
