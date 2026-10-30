using System.Reflection;
using CUE4Parse.UE4.Objects.ControlRig;
using CUE4Parse.UE4.Objects.UObject;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnrealAssetScout.Tests;

public sealed class ControlRigJsonCompletenessTests
{
    [Fact]
    public void OfflineParentKeysSurviveWithoutRuntimeParentPointers()
    {
        var first = new FRigElementKey { Type = ERigElementType.Bone, Name = new FName("pelvis") };
        var second = new FRigElementKey { Type = ERigElementType.Bone, Name = new FName("spine_01") };
        var source = new FRigNullElement
        {
            ParentConstraints = [new() { ParentKey = first }, new() { ParentKey = second }],
            IndexLookup = new() { [first] = 0, [second] = 1 }
        };
        var value = JObject.Parse(JsonConvert.SerializeObject(source));
        Assert.Equal("pelvis", (string?)value["ParentConstraints"]![0]!["ParentKey"]!["Name"]);
        Assert.Equal("spine_01", (string?)value["ParentConstraints"]![1]!["ParentKey"]!["Name"]);
        Assert.Null(value["IndexLookup"]); // Runtime dictionary keys previously collapsed to a type name.
    }

    [Fact]
    public void LoadedPrivateControlSettingsAndTransformsAreExported()
    {
        var source = new FRigControlElement();
        typeof(FRigControlElement).GetField("Settings", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(source, new FRigControlSettings
            {
                ControlType = ERigControlType.Float,
                PrimaryAxis = ERigControlAxis.Y,
                AnimationType = ERigControlAnimationType.ProxyControl,
                ControlEnumPathName = "/Script/Test.ExactEnum"
            });
        var value = JObject.Parse(JsonConvert.SerializeObject(source));
        Assert.Equal((int)ERigControlType.Float, (int)value["Settings"]!["ControlType"]!);
        Assert.Equal((int)ERigControlAxis.Y, (int)value["Settings"]!["PrimaryAxis"]!);
        Assert.Equal("/Script/Test.ExactEnum", (string?)value["Settings"]!["ControlEnumPathName"]);
        Assert.NotNull(value["Offset"]);
        Assert.NotNull(value["Shape"]);
        Assert.NotNull(value.Property("PreferredEulerAngles"));
    }
}
