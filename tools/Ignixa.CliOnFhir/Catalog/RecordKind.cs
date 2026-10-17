// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Catalog;

/// <summary>
/// The kind of a record within its resource type, read from the resource itself: its <c>meta.profile</c> canonicals, or, when it
/// declares none, an Encounter's <c>class</c> code or an Observation's <c>category</c> codes. Data sets keep different kinds of
/// one type apart this way (MIMIC-IV on FHIR: hospital, emergency and ICU encounters; chart, lab and output events), and a
/// search over the type mixes them unless it filters on what tells them apart.
/// </summary>
/// <remarks>
/// A kind is a string so the catalog can hold one shared instance per distinct kind: the profiles separated by spaces (a
/// canonical has none), <c>class:</c> and the code, or <c>category:</c> and the codes separated by commas. Nothing is inferred:
/// a resource with no profile, and no class or category where those apply, has no kind.
/// </remarks>
public static class RecordKind
{
    private const string ClassPrefix = "class:";
    private const string CategoryPrefix = "category:";

    /// <summary>
    /// The kind of a <paramref name="resourceType"/> resource with these values, in the order the resource lists them; null when
    /// there is nothing to tell it by.
    /// </summary>
    public static string? Of(string resourceType, IReadOnlyList<string>? profiles, string? classCode, IReadOnlyList<string>? categoryCodes)
    {
        if (profiles is { Count: 1 })
        {
            return profiles[0];
        }

        if (profiles is { Count: > 1 })
        {
            return string.Join(' ', profiles.Distinct(StringComparer.Ordinal));
        }

        return resourceType switch
        {
            "Encounter" when !string.IsNullOrWhiteSpace(classCode) => ClassPrefix + classCode,
            "Observation" when categoryCodes is { Count: > 0 } => CategoryPrefix + string.Join(',', categoryCodes.Distinct(StringComparer.Ordinal)),
            _ => null,
        };
    }

    /// <summary>
    /// A kind's short name: each profile's last path segment (without a <c>|version</c>), joined by <c>+</c>; a class or category
    /// kind as it is. Short names are only unambiguous within a type when <see cref="FolderKinds"/> says so.
    /// </summary>
    public static string ShortName(string kind)
    {
        ArgumentNullException.ThrowIfNull(kind);

        return IsProfile(kind) ? string.Join('+', kind.Split(' ').Select(LastSegment)) : kind;
    }

    /// <summary>
    /// The search parameters that keep only resources of this kind (and of kinds sharing its first profile or category):
    /// <c>_profile=</c>, <c>class=</c> or <c>category=</c>, and <c>identifier=system|</c> for a kind split by identifier system;
    /// null when neither is given.
    /// </summary>
    public static string? Filter(string? kind, string? identifierSystem)
    {
        string? byKind = kind switch
        {
            null => null,
            _ when kind.StartsWith(ClassPrefix, StringComparison.Ordinal) => "class=" + kind[ClassPrefix.Length..],
            _ when kind.StartsWith(CategoryPrefix, StringComparison.Ordinal) => "category=" + kind[CategoryPrefix.Length..].Split(',')[0],
            _ => "_profile=" + kind.Split(' ')[0],
        };
        string? bySystem = identifierSystem is null ? null : $"identifier={identifierSystem}|";
        return byKind is null ? bySystem : bySystem is null ? byKind : $"{byKind}&{bySystem}";
    }

    /// <summary>The profile canonicals a kind is made of; empty for a class or category kind.</summary>
    public static IReadOnlyList<string> Profiles(string kind) => IsProfile(kind) ? kind.Split(' ') : [];

    private static bool IsProfile(string kind) =>
        !kind.StartsWith(ClassPrefix, StringComparison.Ordinal) && !kind.StartsWith(CategoryPrefix, StringComparison.Ordinal);

    private static string LastSegment(string profile)
    {
        string url = profile.Split('|')[0].TrimEnd('/');
        int slash = url.LastIndexOf('/');
        return slash >= 0 && slash < url.Length - 1 ? url[(slash + 1)..] : url;
    }
}
