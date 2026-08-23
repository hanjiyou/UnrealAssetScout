using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.UObject;
using UnrealAssetScout.References;

namespace UnrealAssetScout.Tests;

// Verifies serialized soft-reference discovery and nested property evidence paths.
public class PropertyReferenceReaderTests
{
    [Fact]
    public void ReadExports_FindsDirectAndNestedSoftObjectPaths()
    {
        var export = new UObject { Name = "TestExport" };
        export.Properties.Add(Tag(
            "Direct",
            "SoftObjectProperty",
            new SoftObjectProperty(new FSoftObjectPath(new FName("/Game/Target.Target"), string.Empty))));
        export.Properties.Add(Tag(
            "Nested",
            "ArrayProperty",
            new ArrayProperty(new UScriptArray(
            [
                new SoftObjectProperty(new FSoftObjectPath(new FName("/Game/Other.Other"), string.Empty))
            ],
            "SoftObjectProperty"))));

        var edges = PropertyReferenceReader.ReadExports(
            [export],
            "PioneerGame/Content/Source.uasset",
            identity => identity switch
            {
                "/Game/Target.Target" => "PioneerGame/Content/Target.uasset",
                "/Game/Other.Other" => "PioneerGame/Content/Other.uasset",
                _ => null
            });

        Assert.Equal(2, edges.Count);
        Assert.Contains(edges, edge =>
            edge.TargetPath == "PioneerGame/Content/Target.uasset" &&
            edge.EvidencePath == "TestExport.Direct");
        Assert.Contains(edges, edge =>
            edge.TargetPath == "PioneerGame/Content/Other.uasset" &&
            edge.EvidencePath == "TestExport.Nested[0]");
    }

    [Fact]
    public void ReadExports_SkipsReferencesBackIntoTheSourcePackage()
    {
        var export = new UObject { Name = "TestExport" };
        export.Properties.Add(Tag(
            "Self",
            "SoftObjectProperty",
            new SoftObjectProperty(new FSoftObjectPath(new FName("/Game/Source.Source"), string.Empty))));

        var edges = PropertyReferenceReader.ReadExports(
            [export],
            "PioneerGame/Content/Source.uasset",
            _ => "PioneerGame/Content/Source.uasset");

        Assert.Empty(edges);
    }

    private static FPropertyTag Tag(string name, string type, FPropertyTagType value) =>
        new(new FName(name), new FName(type), 0, 0, null, false, null, value);
}
