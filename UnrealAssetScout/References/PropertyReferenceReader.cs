using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CUE4Parse.UE4;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.UObject;

namespace UnrealAssetScout.References;

// Walks deserialized export properties without loading referenced objects and records object paths.
internal static class PropertyReferenceReader
{
    internal const string HardObjectProperty = "HARD_OBJECT_PROPERTY";
    internal const string SoftObjectProperty = "SOFT_OBJECT_PROPERTY";

    internal static IReadOnlyList<PropertyReferenceEdge> Read(
        IPackage package,
        string sourcePath,
        Func<string, string?> resolvePackagePath)
    {
        var visitor = new Visitor(sourcePath, resolvePackagePath);
        foreach (var export in package.GetExports())
            visitor.VisitHolder(export, export.Name);

        return visitor.Edges;
    }

    internal static IReadOnlyList<PropertyReferenceEdge> ReadExports(
        IEnumerable<UObject> exports,
        string sourcePath,
        Func<string, string?> resolvePackagePath)
    {
        var visitor = new Visitor(sourcePath, resolvePackagePath);
        foreach (var export in exports)
            visitor.VisitHolder(export, export.Name);

        return visitor.Edges;
    }

    private sealed class Visitor(string sourcePath, Func<string, string?> resolvePackagePath)
    {
        private const int MaximumDepth = 24;
        private readonly HashSet<PropertyReferenceEdge> _edges = [];
        private readonly HashSet<object> _visited = new(ReferenceEqualityComparer.Instance);

        internal IReadOnlyList<PropertyReferenceEdge> Edges => [.. _edges];

        internal void VisitHolder(IPropertyHolder holder, string evidencePath)
        {
            foreach (var property in holder.Properties)
            {
                var indexedName = property.ArrayIndex > 0
                    ? $"{property.Name.Text}[{property.ArrayIndex}]"
                    : property.Name.Text;
                VisitValue(property.Tag, $"{evidencePath}.{indexedName}", 0);
            }
        }

        private void VisitValue(object? value, string evidencePath, int depth)
        {
            if (value is null || depth > MaximumDepth)
                return;

            switch (value)
            {
                case FPackageIndex packageIndex:
                    AddHard(packageIndex, evidencePath);
                    return;
                case FSoftObjectPath softObjectPath:
                    AddSoft(softObjectPath.ToString(), evidencePath);
                    return;
                case AssetObjectProperty assetObjectProperty:
                    AddSoft(assetObjectProperty.Value, evidencePath);
                    return;
                case FPropertyTagType propertyTagType:
                    VisitValue(propertyTagType.GenericValue, evidencePath, depth + 1);
                    return;
                case UScriptArray array:
                    for (var index = 0; index < array.Properties.Count; index++)
                        VisitValue(array.Properties[index], $"{evidencePath}[{index}]", depth + 1);
                    return;
                case UScriptSet set:
                    for (var index = 0; index < set.Properties.Count; index++)
                        VisitValue(set.Properties[index], $"{evidencePath}[{index}]", depth + 1);
                    return;
                case UScriptMap map:
                    var entryIndex = 0;
                    foreach (var pair in map.Properties)
                    {
                        VisitValue(pair.Key, $"{evidencePath}[{entryIndex}].Key", depth + 1);
                        VisitValue(pair.Value, $"{evidencePath}[{entryIndex}].Value", depth + 1);
                        entryIndex++;
                    }
                    return;
                case FScriptStruct scriptStruct:
                    VisitValue(scriptStruct.StructType, evidencePath, depth + 1);
                    return;
                case IPropertyHolder propertyHolder:
                    VisitHolder(propertyHolder, evidencePath);
                    return;
                case IUStruct:
                    VisitStruct(value, evidencePath, depth + 1);
                    return;
                case IDictionary dictionary:
                    entryIndex = 0;
                    foreach (DictionaryEntry pair in dictionary)
                    {
                        VisitValue(pair.Key, $"{evidencePath}[{entryIndex}].Key", depth + 1);
                        VisitValue(pair.Value, $"{evidencePath}[{entryIndex}].Value", depth + 1);
                        entryIndex++;
                    }
                    return;
                case IEnumerable enumerable when value is not string:
                    var itemIndex = 0;
                    foreach (var item in enumerable)
                    {
                        VisitValue(item, $"{evidencePath}[{itemIndex}]", depth + 1);
                        itemIndex++;
                    }
                    return;
            }
        }

        private void VisitStruct(object value, string evidencePath, int depth)
        {
            if (value.GetType().IsClass && !_visited.Add(value))
                return;

            foreach (var field in value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (field.Name.Equals("Owner", StringComparison.OrdinalIgnoreCase))
                    continue;

                VisitValue(field.GetValue(value), $"{evidencePath}.{field.Name}", depth + 1);
            }

            foreach (var property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0 ||
                    property.Name.Equals("Owner", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                object? propertyValue;
                try
                {
                    propertyValue = property.GetValue(value);
                }
                catch
                {
                    continue;
                }

                VisitValue(propertyValue, $"{evidencePath}.{property.Name}", depth + 1);
            }
        }

        private void AddHard(FPackageIndex packageIndex, string evidencePath)
        {
            if (packageIndex.IsNull)
                return;

            var resolvedObject = packageIndex.ResolvedObject;
            var identity = resolvedObject?.GetPathName() ?? $"packageindex:{packageIndex.Index}";
            var targetPath = Resolve(identity) ??
                             (resolvedObject is null ? null : Resolve(resolvedObject.Package.Name));
            Add(HardObjectProperty, identity, targetPath, evidencePath);
        }

        private void AddSoft(string? identity, string evidencePath)
        {
            if (string.IsNullOrWhiteSpace(identity) || identity.Equals("None", StringComparison.OrdinalIgnoreCase))
                return;

            Add(SoftObjectProperty, identity, Resolve(identity), evidencePath);
        }

        private string? Resolve(string identity)
        {
            try
            {
                return resolvePackagePath(identity);
            }
            catch
            {
                return null;
            }
        }

        private void Add(string kind, string identity, string? targetPath, string evidencePath)
        {
            if (targetPath is not null &&
                targetPath.Equals(sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _edges.Add(new PropertyReferenceEdge(sourcePath, kind, identity, targetPath, evidencePath));
        }
    }
}
