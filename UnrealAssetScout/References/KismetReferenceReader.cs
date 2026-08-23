using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CUE4Parse.UE4;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Kismet;
using CUE4Parse.UE4.Objects.UObject;

namespace UnrealAssetScout.References;

// Walks parsed Blueprint/Kismet expression trees without claiming that their branches execute.
internal static class KismetReferenceReader
{
    internal const string HardObjectReference = "KISMET_HARD_OBJECT_REFERENCE";
    internal const string SoftObjectReference = "KISMET_SOFT_OBJECT_REFERENCE";

    internal static IReadOnlyList<KismetReferenceEdge> Read(
        IPackage package,
        string sourcePath,
        Func<string, string?> resolvePackagePath) =>
        ReadExports(package.GetExports(), sourcePath, resolvePackagePath);

    internal static IReadOnlyList<KismetReferenceEdge> ReadExports(
        IEnumerable<UObject> exports,
        string sourcePath,
        Func<string, string?> resolvePackagePath)
    {
        var visitor = new Visitor(sourcePath, resolvePackagePath);
        foreach (var export in exports.OfType<UStruct>())
        {
            if (export.ScriptBytecode is not { Length: > 0 })
                continue;

            for (var index = 0; index < export.ScriptBytecode.Length; index++)
            {
                visitor.VisitExpression(
                    export.ScriptBytecode[index],
                    $"{export.Name}.ScriptBytecode[{index}]",
                    0,
                    softContext: false);
            }
        }

        return visitor.Edges;
    }

    private sealed class Visitor(string sourcePath, Func<string, string?> resolvePackagePath)
    {
        private const int MaximumDepth = 32;
        private readonly HashSet<KismetReferenceEdge> _edges = [];
        private readonly HashSet<object> _visited = new(ReferenceEqualityComparer.Instance);

        internal IReadOnlyList<KismetReferenceEdge> Edges => [.. _edges];

        internal void VisitExpression(
            KismetExpression? expression,
            string evidencePath,
            int depth,
            bool softContext)
        {
            if (expression is null || depth > MaximumDepth || !_visited.Add(expression))
                return;

            if (expression is EX_SoftObjectConst softObject)
            {
                VisitExpression(softObject.Value, $"{evidencePath}.Value", depth + 1, softContext: true);
                return;
            }

            if (softContext && expression is KismetExpression<string> stringExpression)
            {
                AddSoft(stringExpression.Value, evidencePath);
                return;
            }

            VisitFields(expression, evidencePath, depth + 1, softContext, trackVisited: false);
        }

        private void VisitValue(object? value, string evidencePath, int depth, bool softContext)
        {
            if (value is null || depth > MaximumDepth)
                return;

            switch (value)
            {
                case FPackageIndex packageIndex:
                    AddPackageIndex(packageIndex, evidencePath, softContext);
                    return;
                case KismetExpression expression:
                    VisitExpression(expression, evidencePath, depth + 1, softContext);
                    return;
                case IEnumerable enumerable when value is not string:
                    var itemIndex = 0;
                    foreach (var item in enumerable)
                    {
                        VisitValue(item, $"{evidencePath}[{itemIndex}]", depth + 1, softContext);
                        itemIndex++;
                    }
                    return;
                case FFieldPath:
                    VisitFields(value, evidencePath, depth + 1, softContext);
                    return;
            }

            if (value.GetType().Namespace?.StartsWith("CUE4Parse.UE4.Kismet", StringComparison.Ordinal) == true)
                VisitFields(value, evidencePath, depth + 1, softContext);
        }

        private void VisitFields(
            object value,
            string evidencePath,
            int depth,
            bool softContext,
            bool trackVisited = true)
        {
            if (trackVisited && value.GetType().IsClass && !_visited.Add(value))
                return;

            foreach (var field in value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                VisitValue(field.GetValue(value), $"{evidencePath}.{field.Name}", depth + 1, softContext);
        }

        private void AddPackageIndex(FPackageIndex packageIndex, string evidencePath, bool softContext)
        {
            if (packageIndex.IsNull)
                return;

            var resolvedObject = packageIndex.ResolvedObject;
            var identity = resolvedObject?.GetPathName() ?? $"packageindex:{packageIndex.Index}";
            var targetPath = Resolve(identity) ??
                             (resolvedObject is null ? null : Resolve(resolvedObject.Package.Name));
            Add(
                softContext ? SoftObjectReference : HardObjectReference,
                identity,
                targetPath,
                evidencePath);
        }

        private void AddSoft(string? identity, string evidencePath)
        {
            if (string.IsNullOrWhiteSpace(identity) || identity.Equals("None", StringComparison.OrdinalIgnoreCase))
                return;

            Add(SoftObjectReference, identity, Resolve(identity), evidencePath);
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
            if (targetPath is not null && targetPath.Equals(sourcePath, StringComparison.OrdinalIgnoreCase))
                return;

            _edges.Add(new KismetReferenceEdge(sourcePath, kind, identity, targetPath, evidencePath));
        }
    }
}
