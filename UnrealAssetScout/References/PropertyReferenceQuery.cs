using System.Collections.Generic;

namespace UnrealAssetScout.References;

// Applies exact package-path or object-identity matching to serialized property references.
internal static class PropertyReferenceQuery
{
    internal static IEnumerable<PropertyReferenceMatch> Find(
        IEnumerable<PropertyReferenceEdge> edges,
        string target,
        ReferenceDirection direction)
    {
        foreach (var edge in edges)
        {
            if ((direction is ReferenceDirection.Outgoing or ReferenceDirection.Both) &&
                PackageReferenceQuery.MatchesTarget(target, edge.SourcePath))
            {
                yield return new PropertyReferenceMatch(ReferenceDirection.Outgoing, edge);
            }

            if ((direction is ReferenceDirection.Incoming or ReferenceDirection.Both) &&
                (PackageReferenceQuery.MatchesTarget(target, edge.TargetIdentity) ||
                 PackageReferenceQuery.MatchesTarget(target, edge.TargetPath)))
            {
                yield return new PropertyReferenceMatch(ReferenceDirection.Incoming, edge);
            }
        }
    }
}
