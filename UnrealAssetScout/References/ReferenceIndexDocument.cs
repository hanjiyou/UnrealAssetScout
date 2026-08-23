using System;
using System.Collections.Generic;

namespace UnrealAssetScout.References;

// Persisted reference-scan state used to resume interrupted scans and reuse unchanged sources.
internal sealed class ReferenceIndexDocument
{
    public int Schema { get; set; } = ReferenceIndexStore.CurrentSchema;
    public string Game { get; set; } = string.Empty;
    public string UsmapSha256 { get; set; } = string.Empty;
    public string ToolFingerprint { get; set; } = string.Empty;
    public Dictionary<string, ReferenceIndexSource> Sources { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
