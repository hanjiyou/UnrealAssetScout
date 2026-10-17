using CUE4Parse.MappingsProvider;
using CUE4Parse.MappingsProvider.Usmap;

namespace UnrealAssetScout.Tests;

public sealed class UsmapSchemaCompatibilityTests
{
    private static Struct Add(TypeMappings mappings, string name, string? parent, PropertyType type)
    {
        var s = new Struct(mappings, name, parent,
            new Dictionary<int, PropertyInfo> { [0] = new(0, "Value", type, 1) }, 1);
        mappings.Types[name] = s;
        return s;
    }

    [Fact]
    public void KnownDump_RemovesRepeatedDeclarationsAcrossConsecutiveInheritanceLevels()
    {
        var m = new TypeMappings();
        Add(m, "Base", null, new("IntProperty"));
        var middle = Add(m, "Middle", "Base", new("IntProperty"));
        var leaf = Add(m, "Leaf", "Middle", new("IntProperty"));

        Assert.Equal(new[] { "Leaf", "Middle" }, UsmapSchemaCompatibility.Apply(m, UsmapSchemaCompatibility.ArcRaiders200Sha256));
        Assert.Equal(0, middle.PropertyCount);
        Assert.Empty(leaf.Properties);
        Assert.True(leaf.TryGetValue(0, out var inherited));
        Assert.Equal("Value", inherited.Name);
        Assert.Equal(1, leaf.CountProperties(true));
        Assert.Empty(UsmapSchemaCompatibility.Apply(m, UsmapSchemaCompatibility.ArcRaiders200Sha256));
    }

    [Fact]
    public void UnrecognizedDump_IsNotChangedEvenWhenFieldsMatch()
    {
        var m = new TypeMappings();
        Add(m, "Base", null, new("IntProperty"));
        var derived = Add(m, "Derived", "Base", new("IntProperty"));

        Assert.Empty(UsmapSchemaCompatibility.Apply(m, new string('0', 64)));
        Assert.Equal(1, derived.PropertyCount);
        Assert.Single(derived.Properties);
    }

    [Fact]
    public void SamePropertyNameWithDifferentNestedType_IsPreserved()
    {
        var m = new TypeMappings();
        Add(m, "Base", null, new("ArrayProperty", innerType: new("StructProperty", "PoseLink")));
        var derived = Add(m, "Derived", "Base", new("ArrayProperty", innerType: new("StructProperty", "ComponentSpacePoseLink")));

        Assert.Empty(UsmapSchemaCompatibility.Apply(m, UsmapSchemaCompatibility.ArcRaiders200Sha256));
        Assert.Equal(1, derived.PropertyCount);
    }

    [Fact]
    public void DifferentEnumOrArrayDimension_IsPreserved()
    {
        var m = new TypeMappings();
        Add(m, "Base", null, new("EnumProperty", enumName: "First", innerType: new("ByteProperty")));
        var derived = Add(m, "Derived", "Base", new("EnumProperty", enumName: "Second", innerType: new("ByteProperty")));
        Assert.Empty(UsmapSchemaCompatibility.Apply(m, UsmapSchemaCompatibility.ArcRaiders200Sha256));
        derived.Properties[0].MappingType.EnumName = "First";
        derived.Properties[0].ArraySize = 2;
        Assert.Empty(UsmapSchemaCompatibility.Apply(m, UsmapSchemaCompatibility.ArcRaiders200Sha256));
    }

    [Fact]
    public void MissingParentAndSelfReference_ArePreserved()
    {
        var m = new TypeMappings();
        Add(m, "Missing", "Absent", new("IntProperty"));
        Add(m, "Self", "Self", new("IntProperty"));
        Assert.Empty(UsmapSchemaCompatibility.Apply(m, UsmapSchemaCompatibility.ArcRaiders200Sha256));
    }

    [Fact]
    public void DifferentDeclaredPropertySpan_IsPreserved()
    {
        var m = new TypeMappings();
        Add(m, "Base", null, new("IntProperty"));
        var derived = Add(m, "Derived", "Base", new("IntProperty"));
        derived.PropertyCount = 2;
        Assert.Empty(UsmapSchemaCompatibility.Apply(m, UsmapSchemaCompatibility.ArcRaiders200Sha256));
    }
}
