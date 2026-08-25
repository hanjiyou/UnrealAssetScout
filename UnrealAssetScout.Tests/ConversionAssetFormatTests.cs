using CUE4Parse_Conversion.Options;
using UnrealAssetScout.Export;
using UnrealAssetScout.Export.Exporters;

namespace UnrealAssetScout.Tests;

public sealed class ConversionAssetFormatTests
{
    [Fact]
    public void UEFormat_MapsToCUE4ParseUEFormat()
    {
        Assert.Equal(EMeshFormat.UEFormat, ConversionExporter.ResolveMeshFormat(ConversionAssetFormat.UEFormat));
    }

    [Fact]
    public void ActorX_MapsToCUE4ParseActorX()
    {
        Assert.Equal(EMeshFormat.ActorX, ConversionExporter.ResolveMeshFormat(ConversionAssetFormat.ActorX));
    }
}
