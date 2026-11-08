// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Validation.Checks;

/// <summary>
/// Roots the issues of a resource validated inside another (a contained resource, a Bundle or Parameters entry) at the
/// nested resource's position in the outer one.
/// </summary>
internal static class NestedResourceIssues
{
    /// <summary>
    /// <paramref name="issues"/> with each path rooted at <paramref name="prefix"/> (e.g. <c>Bundle.entry[3].resource</c>): a
    /// path already under <paramref name="prefix"/> is kept, a path the nested validation stated relative to its own type
    /// (<c>Patient.name</c>, or <c>Patient</c>) is rebased, and any other path is prefixed.
    /// </summary>
    /// <remarks>
    /// The prefix test comes first: an Observation contained in an Observation reports absolute paths such as
    /// <c>Observation.contained[0].subject</c>, which also start with the nested type's name and would otherwise be rebased
    /// a second time.
    /// </remarks>
    public static IEnumerable<ValidationIssue> Rebase(IEnumerable<ValidationIssue> issues, string resourceType, string prefix)
    {
        foreach (var issue in issues)
        {
            var path = issue.Path;
            if (path == prefix || path.StartsWith($"{prefix}.", StringComparison.Ordinal))
            {
                yield return issue;
                continue;
            }

            if (path.StartsWith($"{resourceType}.", StringComparison.Ordinal))
            {
                path = $"{prefix}.{path[(resourceType.Length + 1)..]}";
            }
            else if (path == resourceType)
            {
                path = prefix;
            }
            else
            {
                path = $"{prefix}.{path}";
            }

            yield return issue with { Path = path };
        }
    }
}
