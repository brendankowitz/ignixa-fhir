// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Patients;
using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.CodeSystems;

/// <summary>
/// The <c>code-system-not-in-folder</c> information issue: a search, cohort criterion or code-text lookup came back empty and
/// named a code system the folder's resources of that type do not use at all (or a bare code no coding has). It states only
/// what the folder's coding inventory and search index say, never what a code looks like.
/// </summary>
internal static class UnusedCodeSystemHint
{
    public const string Code = "code-system-not-in-folder";

    private const int SystemsListed = 5;
    private const int MaxShown = 80;
    private const string Advice = "Search the folder's codes by words (folder_code_systems text=...) and use those.";

    /// <summary>
    /// The hint for one token of a search over <paramref name="use"/>, or null when it does not hold exactly: the parameter is not
    /// coded, the system is used by the type (in the parameter's index first, else anywhere in the type by the inventory), or the
    /// bare code is held by the parameter or by any coding of the folder. A system-less code written <c>|code</c> has no hint.
    /// </summary>
    public static IssueItem? ForToken(FolderSession session, string resourceType, CodedParameterUse use, string? system, string? code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(use);

        if (!use.IsCoded)
        {
            return null;
        }

        if (system is { Length: > 0 })
        {
            return use.UsesSystem(system) || SystemUsedBy(session, resourceType, system, cancellationToken)
                ? null
                : Issue($"No {resourceType} in this folder is coded in {Cut(system)}; {resourceType}s here use {Listed(use.Systems)}. {Advice}");
        }

        if (system is not null || code is not { Length: > 0 } || use.HasCode(code))
        {
            return null;
        }

        CodeSystemInventory inventory = CodeSystemInventories.For(session.Catalog, session.Schema, cancellationToken);
        return CodeHeld(inventory, code) || OnlyImpliedSystems(inventory, resourceType, use)
            ? null
            : Issue($"No {resourceType} in this folder has the code {Cut(code)} in any coding system; {resourceType}s here use {Listed(use.Systems)}. {Advice}");
    }

    /// <summary>
    /// The hint for a <c>folder_code_systems system=...</c> lookup that matched nothing, or null when the system (or the system
    /// with that alias) is used by <paramref name="resourceType"/> (any type when null).
    /// </summary>
    public static IssueItem? ForSystemArgument(FolderSession session, string? resourceType, string system, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        CodeSystemInventory inventory = CodeSystemInventories.For(session.Catalog, session.Schema, cancellationToken);
        string? prefix = resourceType is null ? null : resourceType + ".";
        bool Matches(string uri) => uri == system || CodeAliases.Of(uri) == system;
        bool InType(string element) => prefix is null || element.StartsWith(prefix, StringComparison.Ordinal);
        bool used = inventory.Systems.Any(tally => Matches(tally.System) && tally.Elements.Any(element => InType(element.Element)))
            || inventory.Implicit.Any(tally => tally.System is { } implied && Matches(implied) && InType(tally.Element));
        if (used)
        {
            return null;
        }

        List<string> systemsHere = [.. inventory.Systems
            .Where(tally => tally.Elements.Any(element => InType(element.Element)))
            .Select(tally => tally.System)];
        string whose = resourceType ?? "resource";
        string where = resourceType is null ? "The folder's resources use" : $"{resourceType}s here use";
        return Issue($"No {whose} in this folder is coded in {Cut(system)}; {where} {Listed(systemsHere)}. {Advice}");
    }

    /// <summary>Whether the inventory has <paramref name="system"/> on an element of <paramref name="resourceType"/>, or as the system its binding implies.</summary>
    public static bool SystemUsedBy(FolderSession session, string resourceType, string system, CancellationToken cancellationToken)
    {
        CodeSystemInventory inventory = CodeSystemInventories.For(session.Catalog, session.Schema, cancellationToken);
        string prefix = resourceType + ".";
        return inventory.Systems.Any(tally => tally.System == system && tally.Elements.Any(element => element.Element.StartsWith(prefix, StringComparison.Ordinal)))
            || inventory.Implicit.Any(tally => tally.System == system && tally.Element.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <summary>Whether any coding of the folder, or any value of a FHIR code element with a binding, is <paramref name="code"/> in some system.</summary>
    private static bool CodeHeld(CodeSystemInventory inventory, string code) =>
        inventory.Systems.Any(tally => tally.Codes.Any(held => held.Code == code))
        || inventory.Implicit.Any(tally => tally.Codes.Any(held => held.Code == code));

    /// <summary>
    /// Whether every system the parameter's codes carry is one a FHIR code element of the type implies (Patient.gender's
    /// administrative-gender): its values are a fixed list the data writes without a system, not a choice of code system.
    /// </summary>
    private static bool OnlyImpliedSystems(CodeSystemInventory inventory, string resourceType, CodedParameterUse use)
    {
        string prefix = resourceType + ".";
        HashSet<string> implied = [.. inventory.Implicit.Where(tally => tally.Element.StartsWith(prefix, StringComparison.Ordinal)).Select(tally => tally.System).OfType<string>()];
        return use.Systems.All(implied.Contains);
    }

    private static IssueItem Issue(string diagnostics) => new("information", Code, diagnostics, Location: null);

    private static string Listed(IReadOnlyList<string> systems) =>
        systems.Count == 0 ? "no coding system" : string.Join(", ", systems.Take(SystemsListed).Select(FolderCodeSystems.SafeSystem));

    private static string Cut(string text) => text.Length > MaxShown ? text[..MaxShown] + "..." : text;
}
