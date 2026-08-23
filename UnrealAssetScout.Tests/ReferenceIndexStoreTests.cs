using UnrealAssetScout.References;

namespace UnrealAssetScout.Tests;

// Verifies lossless, atomic persistence of resumable reference scan state.
public sealed class ReferenceIndexStoreTests
{
    [Fact]
    public void SaveThenLoad_RoundTripsLayersAndLeavesNoTemporaryFile()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "references.json");
        var document = new ReferenceIndexDocument
        {
            Game = "GAME_ArcRaiders",
            UsmapSha256 = "usmap",
            ToolFingerprint = "tool"
        };
        document.Sources["PioneerGame/Content/A.uasset"] = new ReferenceIndexSource
        {
            Fingerprint = "source",
            PackageComplete = true,
            PropertiesComplete = true,
            BytecodeComplete = true,
            PackageEdges =
            [
                new PackageReferenceEdge("PioneerGame/Content/A.uasset", "/Game/B", "PioneerGame/Content/B.uasset")
            ],
            PropertyEdges =
            [
                new PropertyReferenceEdge(
                    "PioneerGame/Content/A.uasset",
                    "HARD_OBJECT_PROPERTY",
                    "/Game/C",
                    "PioneerGame/Content/C.uasset",
                    "Export.Value")
            ],
            BytecodeEdges =
            [
                new KismetReferenceEdge(
                    "PioneerGame/Content/A.uasset",
                    KismetReferenceReader.HardObjectReference,
                    "/Game/D",
                    "PioneerGame/Content/D.uasset",
                    "Function.ScriptBytecode[0].Value")
            ]
        };

        ReferenceIndexStore.Save(path, document);
        var loaded = ReferenceIndexStore.TryLoad(path, out var error);

        Assert.Null(error);
        Assert.NotNull(loaded);
        var source = loaded.Sources["pioneergame/content/a.uasset"];
        Assert.True(source.PackageComplete);
        Assert.True(source.PropertiesComplete);
        Assert.True(source.BytecodeComplete);
        Assert.Equal("/Game/B", Assert.Single(source.PackageEdges).TargetIdentity);
        Assert.Equal("Export.Value", Assert.Single(source.PropertyEdges).EvidencePath);
        Assert.Equal("Function.ScriptBytecode[0].Value", Assert.Single(source.BytecodeEdges).EvidencePath);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Save_OverExistingIndexAtomicallyReplacesContent()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "references.json");
        ReferenceIndexStore.Save(path, new ReferenceIndexDocument { Game = "old" });

        ReferenceIndexStore.Save(path, new ReferenceIndexDocument { Game = "new" });

        Assert.Equal("new", ReferenceIndexStore.TryLoad(path, out _)!.Game);
        Assert.Equal(["references.json"], Directory.GetFiles(dir.Path).Select(Path.GetFileName));
    }
}
