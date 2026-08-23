using System;
using System.Collections.Generic;

namespace UnrealAssetScout.References;

// Applies exact path or dependency-identity matching to a prebuilt package reference edge set.
internal static class PackageReferenceQuery
{
    internal static IEnumerable<PackageReferenceMatch> Find(
        IEnumerable<PackageReferenceEdge> edges,
        string target,
        ReferenceDirection direction)
    {
        foreach (var edge in edges)
        {
            if ((direction is ReferenceDirection.Outgoing or ReferenceDirection.Both) &&
                MatchesTarget(target, edge.SourcePath))
            {
                yield return new PackageReferenceMatch(ReferenceDirection.Outgoing, edge);
            }

            if ((direction is ReferenceDirection.Incoming or ReferenceDirection.Both) &&
                (MatchesTarget(target, edge.TargetIdentity) || MatchesTarget(target, edge.TargetPath)))
            {
                yield return new PackageReferenceMatch(ReferenceDirection.Incoming, edge);
            }
        }
    }

    internal static bool MatchesTarget(string target, string? candidate) =>
        candidate is not null &&
        string.Equals(Normalize(target), Normalize(candidate), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string value) => value.Trim().Replace('\\', '/');
}
