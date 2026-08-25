using UnrealAssetScout.Config;
using UnrealAssetScout.Export;

namespace UnrealAssetScout.Tests;

public sealed class AssetFormatOptionTests
{
    private static ConfigOptionsSupport.ParseArgsResult Parse(params string[] extra)
    {
        string[] baseArgs =
        [
            "export", "animations", "--paks", Path.GetTempPath(), "--game", "GAME_UE5_1",
            "--output", Path.GetTempPath()
        ];

        return ConfigOptionsSupport.ParseArgsWithExitCode([.. baseArgs, .. extra]);
    }

    [Fact]
    public void Parse_DefaultsConversionExportsToUEFormat()
    {
        var result = Parse();

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(ConversionAssetFormat.UEFormat, result.Options!.AssetFormat);
    }

    [Theory]
    [InlineData("ueformat", "UEFormat")]
    [InlineData("actorx", "ActorX")]
    public void Parse_AssetFormat_IsCaseInsensitive(string value, string expectedName)
    {
        var result = Parse("--asset-format", value);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expectedName, result.Options!.AssetFormat.ToString());
    }

    [Fact]
    public void Parse_UnknownAssetFormat_IsRejected()
    {
        var result = Parse("--asset-format", "fbx");

        Assert.Equal(1, result.ExitCode);
        Assert.Null(result.Options);
    }
}
