using System.Runtime.CompilerServices;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Kismet;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.UObject;
using UnrealAssetScout.References;

namespace UnrealAssetScout.Tests;

// Verifies hard and soft object extraction from parsed Kismet expression trees.
public sealed class KismetReferenceReaderTests
{
    [Fact]
    public void ReadExports_FindsHardAndSoftReferencesWithExpressionEvidence()
    {
        var package = new StubPackage();
        var target = new UObject { Name = "TargetObject" };
        var hard = Uninitialized<EX_ObjectConst>();
        hard.Value = package.IndexOf(target);
        var stringValue = Uninitialized<EX_StringConst>();
        stringValue.Value = "/Game/SoftTarget.SoftTarget";
        var soft = Uninitialized<EX_SoftObjectConst>();
        soft.Value = stringValue;
        var function = new UStruct
        {
            Name = "ExecuteUbergraph_Test",
            ScriptBytecode = [hard, soft]
        };

        var edges = KismetReferenceReader.ReadExports(
            [function],
            "PioneerGame/Content/Source.uasset",
            identity => identity switch
            {
                "TargetObject" => "PioneerGame/Content/Target.uasset",
                "/Game/SoftTarget.SoftTarget" => "PioneerGame/Content/SoftTarget.uasset",
                _ => null
            });

        Assert.Contains(edges, edge =>
            edge.Kind == KismetReferenceReader.HardObjectReference &&
            edge.TargetPath == "PioneerGame/Content/Target.uasset" &&
            edge.EvidencePath == "ExecuteUbergraph_Test.ScriptBytecode[0].Value");
        Assert.Contains(edges, edge =>
            edge.Kind == KismetReferenceReader.SoftObjectReference &&
            edge.TargetPath == "PioneerGame/Content/SoftTarget.uasset" &&
            edge.EvidencePath == "ExecuteUbergraph_Test.ScriptBytecode[1].Value");
    }

    private static T Uninitialized<T>() where T : class =>
        (T) RuntimeHelpers.GetUninitializedObject(typeof(T));

    private sealed class StubPackage : IPackage
    {
        private readonly List<ResolvedObject?> _resolutions = [null];

        internal FPackageIndex IndexOf(UObject target)
        {
            _resolutions.Add(new ResolvedLoadedObject(target));
            return new FPackageIndex(this, _resolutions.Count - 1);
        }

        public ResolvedObject? ResolvePackageIndex(FPackageIndex? index) =>
            index is { IsNull: false } && index.Index < _resolutions.Count ? _resolutions[index.Index] : null;

        public string Name { get; set; } = "Stub";
        public IFileProvider? Provider => null;
        public TypeMappings? Mappings => null;
        public FPackageFileSummary Summary => throw new NotSupportedException();
        public FNameEntrySerialized[] NameMap => throw new NotSupportedException();
        public int ImportMapLength => 0;
        public int ExportMapLength => 0;
        public Lazy<UObject>[] ExportsLazy => [];
        public bool IsFullyLoaded => true;
        public bool CanDeserialize => true;
        public bool HasFlags(EPackageFlags flags) => false;
        public int GetExportIndex(string name, StringComparison comparisonType = StringComparison.Ordinal) => -1;
    }
}
