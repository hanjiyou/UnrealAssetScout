using System.Collections.Generic;

namespace UnrealAssetScout.References;

// Stores reusable reference layers for one source package and its container fingerprint.
internal sealed class ReferenceIndexSource
{
    public string? Fingerprint { get; set; }
    public bool PackageComplete { get; set; }
    public bool PropertiesComplete { get; set; }
    public bool BytecodeComplete { get; set; }
    public List<PackageReferenceEdge> PackageEdges { get; set; } = [];
    public List<PropertyReferenceEdge> PropertyEdges { get; set; } = [];
    public List<KismetReferenceEdge> BytecodeEdges { get; set; } = [];
}
