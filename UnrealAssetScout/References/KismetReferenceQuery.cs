using System.Collections.Generic;

namespace UnrealAssetScout.References;

// Applies exact package-path or object-identity matching to parsed Kismet references.
internal static class KismetReferenceQuery
{
    internal static IEnumerable<KismetReferenceMatch> Find(
        IEnumerable<KismetReferenceEdge> edges,
        string target,
        ReferenceDirection direction)
    {
        foreach (var edge in edges)
        {
            if ((direction is ReferenceDirection.Outgoing or ReferenceDirection.Both) &&
                PackageReferenceQuery.MatchesTarget(target, edge.SourcePath))
            {
                yield return new KismetReferenceMatch(ReferenceDirection.Outgoing, edge);
            }

            if ((direction is ReferenceDirection.Incoming or ReferenceDirection.Both) &&
                (PackageReferenceQuery.MatchesTarget(target, edge.TargetIdentity) ||
                 PackageReferenceQuery.MatchesTarget(target, edge.TargetPath)))
            {
                yield return new KismetReferenceMatch(ReferenceDirection.Incoming, edge);
            }
        }
    }
}
