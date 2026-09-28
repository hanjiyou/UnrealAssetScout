using UnrealAssetScout.Config;

namespace UnrealAssetScout.Tests;

[Collection("Logging")]
public class PartialMountOptionsTests
{
    private static string InputDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "UnrealAssetScout.Tests", "PartialMount", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void ExplicitFilteredExportAllowsPartialMount()
    {
        var directory = InputDirectory();
        var result = ConfigOptionsSupport.ParseArgsWithExitCode([
            "export", "models", "--paks", directory, "--game", "GAME_UE5_8",
            "--output", Path.Combine(directory, "output"), "--filter", "^Game/Content/Sample\\.uasset$",
            "--allow-partial-mount"]);
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Options!.AllowPartialMount);
    }

    [Fact]
    public void UnfilteredExportCannotEnablePartialMount()
    {
        var directory = InputDirectory();
        var result = ConfigOptionsSupport.ParseArgsWithExitCode([
            "export", "models", "--paks", directory, "--game", "GAME_UE5_8",
            "--output", Path.Combine(directory, "output"), "--allow-partial-mount"]);
        Assert.Equal(1, result.ExitCode);
        Assert.Null(result.Options);
    }

    [Fact]
    public void DefaultExportRemainsStrict()
    {
        var directory = InputDirectory();
        var result = ConfigOptionsSupport.ParseArgsWithExitCode([
            "export", "models", "--paks", directory, "--game", "GAME_UE5_8",
            "--output", Path.Combine(directory, "output"), "--filter", "Sample"]);
        Assert.Equal(0, result.ExitCode);
        Assert.False(result.Options!.AllowPartialMount);
    }
}
