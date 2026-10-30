using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Readers;
using CUE4Parse.UE4.Exceptions;
using CUE4Parse.UE4.Objects.StructUtils;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnrealAssetScout.Tests;

public sealed class PropertyBagJsonCompletenessTests
{
    private sealed class TestPackage : IPackage
    {
        public string Name { get; set; } = "/Test/PropertyBag";
        public IFileProvider? Provider => null;
        public TypeMappings Mappings { get; } = new();
        public FPackageFileSummary Summary => default;
        public FNameEntrySerialized[] NameMap => [new("None"), new("ExactName")];
        public int ImportMapLength => 0;
        public int ExportMapLength => 0;
        public Lazy<UObject>[] ExportsLazy => [];
        public bool IsFullyLoaded => true;
        public bool CanDeserialize => true;
        public bool HasFlags(EPackageFlags flags) => flags == EPackageFlags.PKG_UnversionedProperties;
        public int GetExportIndex(string name, StringComparison comparisonType = StringComparison.Ordinal) => -1;
        public ResolvedObject? ResolvePackageIndex(FPackageIndex? index) => index?.Index switch
        {
            1 => new ResolvedLoadedObject(new UScriptClass("Vector")),
            2 => new ResolvedLoadedObject(new UScriptClass("ExactSettings")),
            3 => new ResolvedLoadedObject(new UScriptClass("ExactMode")),
            _ => null
        };

        public TestPackage()
        {
            Mappings.Types["ExactSettings"] = new Struct(Mappings, "ExactSettings", null,
                new Dictionary<int, PropertyInfo> { [0] = new(0, "Frequency", new PropertyType("FloatProperty")) }, 1);
            Mappings.Enums["ExactMode"] = new Dictionary<long, string> { [0] = "Off", [1] = "On" };
        }
    }

    private static FPropertyBagPropertyDesc Desc(string name, EPropertyBagPropertyType type,
        TestPackage? owner = null, int objectIndex = 0, params EPropertyBagContainerType[] containers) => new()
    {
        Name = new FName(name), ValueType = type,
        ValueTypeObject = owner == null ? new FPackageIndex() : new FPackageIndex(owner, objectIndex),
        ContainerTypes = new FPropertyBagContainerTypes(containers)
    };

    private static byte[] Bytes(Action<BinaryWriter> write)
    {
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        write(writer);
        return memory.ToArray();
    }

    private static FAssetArchive Archive(byte[] bytes, TestPackage? package = null) =>
        new(new FByteArchive("property_bag_fixture", bytes, new VersionContainer(EGame.GAME_ArcRaiders)), package ?? new());

    private static JObject Decode(FPropertyBagPropertyDesc[] descriptors, byte[] bytes, TestPackage? package = null)
    {
        using var archive = Archive(bytes, package);
        var values = FInstancedPropertyBag.ReadValues(archive, descriptors, bytes.Length);
        Assert.Equal(bytes.Length, archive.Position);
        return JObject.Parse(JsonConvert.SerializeObject(values));
    }

    [Fact]
    public void CompleteBagConstructorExportsDecodedValuesAndLeavesFollowingMemoryUntouched()
    {
        using var versionArchive = Archive([]);
        var version = FPropertyBagCustomVersion.Get(versionArchive);
        var bytes = Bytes(w =>
        {
            w.Write(1); // bHasData
            w.Write(1); // descriptor count
            w.Write(0); // ValueTypeObject
            w.Write(new byte[16]); // ID
            w.Write(1); w.Write(0); // descriptor Name
            w.Write((byte)EPropertyBagPropertyType.Float);
            w.Write((byte)0); // ContainerTypes count
            w.Write(0); // bHasMetaData
            if (version >= FPropertyBagCustomVersion.Type.PropertyFlags) w.Write((ulong)0);
            if (version >= FPropertyBagCustomVersion.Type.KeyTypes) { w.Write((byte)0); w.Write(0); }
            w.Write(6); // SerialSize
            w.Write((ushort)0x300); w.Write(-2.75f);
            w.Write(0x12345678); // next memory domain sentinel
        });
        using var archive = Archive(bytes);
        var bag = new FInstancedPropertyBag(archive);
        var json = JObject.Parse(JsonConvert.SerializeObject(bag));
        Assert.True((bool)json["ValuesDecoded"]!);
        Assert.Equal(-2.75, (double)json["Values"]!["ExactName"]!);
        Assert.Equal(6, bag.SerialSize);
        Assert.Equal(0x12345678, archive.Read<int>());
    }

    [Fact]
    public void DescriptorOrderDecodesScalarsArraysNativeAndMappedStructsEnumsAndNames()
    {
        var package = new TestPackage();
        FPropertyBagPropertyDesc[] descriptors =
        [
            Desc("Gain", EPropertyBagPropertyType.Float),
            Desc("Samples", EPropertyBagPropertyType.Float, containers: [EPropertyBagContainerType.Array]),
            Desc("Position", EPropertyBagPropertyType.Struct, package, 1),
            Desc("Settings", EPropertyBagPropertyType.Struct, package, 2),
            Desc("Mode", EPropertyBagPropertyType.Enum, package, 3),
            Desc("Enabled", EPropertyBagPropertyType.Bool),
            Desc("Label", EPropertyBagPropertyType.Name)
        ];
        var bytes = Bytes(w =>
        {
            w.Write((ushort)(0x100 | (7 << 9)));
            w.Write(1.25f);
            w.Write(2); w.Write(2.5f); w.Write(-4f);
            w.Write(3d); w.Write(4d); w.Write(5d);
            w.Write((ushort)0x300); w.Write(9f);
            w.Write((byte)1); w.Write((byte)1);
            w.Write(1); w.Write(0);
        });
        var result = Decode(descriptors, bytes, package);
        Assert.Equal(descriptors.Select(d => d.Name.Text), result.Properties().Select(p => p.Name));
        Assert.Equal(1.25, (double)result["Gain"]!);
        Assert.Equal(new[] { 2.5, -4 }, result["Samples"]!.Values<double>());
        Assert.Equal(3, (double)result["Position"]!["X"]!);
        Assert.Equal(9, (double)result["Settings"]!["Frequency"]!);
        Assert.Equal("ExactMode::On", (string?)result["Mode"]);
        Assert.True((bool)result["Enabled"]!);
        Assert.Equal("ExactName", (string?)result["Label"]);
        Assert.Single(package.Mappings.Types); // Dynamic bag schema never changes shared mappings.
    }

    [Fact]
    public void ZeroMaskValuesAreDecodedFromHeaderWithoutConsumingValueBytes()
    {
        var result = Decode([Desc("Gain", EPropertyBagPropertyType.Float), Desc("Enabled", EPropertyBagPropertyType.Bool),
            Desc("Items", EPropertyBagPropertyType.Float, containers: [EPropertyBagContainerType.Array])],
            Bytes(w => { w.Write((ushort)(0x180 | (3 << 9))); w.Write((byte)7); }));
        Assert.Equal(0, (double)result["Gain"]!);
        Assert.False((bool)result["Enabled"]!);
        Assert.Empty((JArray)result["Items"]!);
    }

    [Fact]
    public void ZeroMaskIdentityDistinguishesAZeroStructFromAnEmptySerializedStruct()
    {
        var package = new TestPackage();
        var descriptors = new[] { Desc("Settings", EPropertyBagPropertyType.Struct, package, 2) };
        using var zeroArchive = Archive(Bytes(w => { w.Write((ushort)0x380); w.Write((byte)1); }), package);
        var zero = FInstancedPropertyBag.ReadValues(zeroArchive, descriptors, (int)zeroArchive.Length, out var zeroIndices);
        Assert.Empty(JObject.Parse(JsonConvert.SerializeObject(zero)).Value<JObject>("Settings")!.Properties());
        Assert.Equal(new[] { 0 }, zeroIndices);
        using var emptyArchive = Archive(Bytes(w => { w.Write((ushort)0x300); w.Write((ushort)0x100); }), package);
        var empty = FInstancedPropertyBag.ReadValues(emptyArchive, descriptors, (int)emptyArchive.Length, out var emptyIndices);
        Assert.Empty(JObject.Parse(JsonConvert.SerializeObject(empty)).Value<JObject>("Settings")!.Properties());
        Assert.Empty(emptyIndices);
    }

    [Fact]
    public void NestedZeroPathsRetainExplicitHeaderZerosInsideArrayStructs()
    {
        var package = new TestPackage();
        var descriptors = new[] { Desc("Settings/Rows~", EPropertyBagPropertyType.Struct, package, 2,
            EPropertyBagContainerType.Array) };
        var bytes = Bytes(w =>
        {
            w.Write((ushort)0x300); w.Write(2);
            w.Write((ushort)0x380); w.Write((byte)1); // element 0 Frequency explicitly zero
            w.Write((ushort)0x300); w.Write(4f); // element 1 has real bytes
        });
        using var archive = Archive(bytes, package);
        var values = FInstancedPropertyBag.ReadValues(archive, descriptors, bytes.Length, out var roots, out var paths);
        Assert.Empty(roots);
        Assert.Equal(new[] { "/Settings~1Rows~0/0/Frequency" }, paths);
        var json = JObject.Parse(JsonConvert.SerializeObject(values));
        Assert.Equal(0, (double)json["Settings/Rows~"]![0]!["Frequency"]!);
        Assert.Equal(4, (double)json["Settings/Rows~"]![1]!["Frequency"]!);
    }

    [Fact]
    public void NestedArrayContainersPreserveEveryElement()
    {
        var result = Decode([Desc("Rows", EPropertyBagPropertyType.Int32,
            containers: [EPropertyBagContainerType.Array, EPropertyBagContainerType.Array])],
            Bytes(w => { w.Write((ushort)0x300); w.Write(2); w.Write(2); w.Write(17); w.Write(-2); w.Write(0); }));
        Assert.Equal(new[] { 17, -2 }, result["Rows"]![0]!.Values<int>());
        Assert.Empty((JArray)result["Rows"]![1]!);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    public void InvalidSerialSizeFails(int size)
    {
        using var archive = Archive([0, 1]);
        Assert.Throws<ParserException>(() => FInstancedPropertyBag.ReadValues(archive, [], size));
    }

    [Fact]
    public void ExtraPayloadBytesAndTruncatedValuesFail()
    {
        Assert.Throws<ParserException>(() => Decode([Desc("Gain", EPropertyBagPropertyType.Float)],
            Bytes(w => { w.Write((ushort)0x300); w.Write(1f); w.Write((byte)99); })));
        Assert.ThrowsAny<Exception>(() => Decode([Desc("Gain", EPropertyBagPropertyType.Float)],
            Bytes(w => { w.Write((ushort)0x300); w.Write((byte)1); })));
        using var archive = Archive(Bytes(w => { w.Write((ushort)0x300); w.Write(1f); }));
        Assert.ThrowsAny<Exception>(() => FInstancedPropertyBag.ReadValues(archive,
            [Desc("Gain", EPropertyBagPropertyType.Float)], 3));
    }

    [Fact]
    public void MissingAndUnknownRegisterIndicesFailInsteadOfFillingDefaults()
    {
        Assert.Throws<ParserException>(() => Decode([Desc("Gain", EPropertyBagPropertyType.Float)], [0, 1]));
        Assert.Throws<ParserException>(() => Decode([Desc("Gain", EPropertyBagPropertyType.Float)],
            Bytes(w => { w.Write((ushort)0x301); w.Write(1f); })));
    }

    [Fact]
    public void DuplicateNamesAndUnknownTypesOrContainersFail()
    {
        Assert.Throws<ParserException>(() => Decode([Desc("Gain", EPropertyBagPropertyType.Float),
            Desc("GAIN", EPropertyBagPropertyType.Float)], [0, 1]));
        Assert.Throws<ParserException>(() => Decode([Desc("Unknown", (EPropertyBagPropertyType)255)], [0, 1]));
        Assert.Throws<ParserException>(() => Decode([Desc("Unknown", EPropertyBagPropertyType.Float,
            containers: [(EPropertyBagContainerType)255])], [0, 1]));
        Assert.Throws<ParserException>(() => Decode([Desc("Unknown", EPropertyBagPropertyType.Struct)], [0, 1]));
    }

    [Fact]
    public void EmptySerializedBagRequiresAndConsumesItsEmptyHeader()
    {
        Assert.Empty(Decode([], [0, 1]).Properties());
        Assert.ThrowsAny<Exception>(() => Decode([], []));
    }
}
