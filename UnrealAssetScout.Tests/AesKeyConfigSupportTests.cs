using UnrealAssetScout.Config;

namespace UnrealAssetScout.Tests;

// Covers AES input paths that deliberately avoid command-line arguments and key files.
// Exercises AesKeyConfigSupport with in-memory readers so no secret material reaches disk.
[Collection("Logging")]
public class AesKeyConfigSupportTests
{
    [Fact]
    public void TryReadKeyFromStandardInput_ReturnsTrimmedFirstLine()
    {
        using var reader = new StringReader("  test-key  \nignored\n");

        var success = AesKeyConfigSupport.TryReadKeyFromStandardInput(reader, out var value);

        Assert.True(success);
        Assert.Equal("test-key", value);
    }

    [Fact]
    public void TryReadKeyFromStandardInput_EmptyInputFails()
    {
        using var reader = new StringReader(string.Empty);

        var success = AesKeyConfigSupport.TryReadKeyFromStandardInput(reader, out var value);

        Assert.False(success);
        Assert.Null(value);
    }
}
