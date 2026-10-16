// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Catalog;

namespace Ignixa.CliOnFhir.Views;

/// <summary>
/// How a view's problems are worded. A ViewDefinition in the folder is untrusted, and the engine's messages quote it (and the
/// resources it ran on), so for such a view they are summarized by a digest instead of quoted; names taken from a view are
/// shown only when shaped like a name.
/// </summary>
internal static partial class ViewIssues
{
    /// <summary><paramref name="name"/> when it is shaped like a column, constant or resource type name, else its digest.</summary>
    public static string Word(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return Name().IsMatch(name) ? name : FhirIds.Digest(name);
    }

    /// <summary>
    /// <paramref name="what"/> and the engine's message for <paramref name="exception"/>: quoted when the view is the caller's own
    /// text, otherwise only its digest.
    /// </summary>
    public static string Describe(string what, Exception exception, bool viewFromFolder)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return viewFromFolder
            ? $"{what} (the engine's message quotes the folder's ViewDefinition, so only its digest is given: {FhirIds.Digest(exception.Message)}). Open the ViewDefinition in the folder to find the problem."
            : $"{what}: {exception.Message}";
    }

    /// <summary>
    /// A warning when the ViewDefinition uses <c>resolve()</c>. The SQL-on-FHIR evaluator is given no resolver for the folder, so
    /// a path through <c>resolve()</c> reaches no other resource and its cell is null, which would read as data that is not
    /// there. The warning names no text from the view, so it is the same for a view from the folder.
    /// </summary>
    public static IEnumerable<IssueItem> ResolveUse(JsonObject viewDefinition)
    {
        ArgumentNullException.ThrowIfNull(viewDefinition);

        if (ResolveCall().IsMatch(viewDefinition.ToJsonString()))
        {
            yield return new IssueItem(
                "warning",
                "resolve-unsupported",
                "The ViewDefinition uses resolve(). A view run has no resolver for the folder's other resources, so a path through resolve() is empty (null cells) unless it names a contained resource. Select the reference (subject.reference) and join the rows of two views, or read the resolved values with search --columns, whose resolve() reaches the folder.",
                Location: null);
        }
    }

    [GeneratedRegex(@"\bresolve\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex ResolveCall();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex Name();
}
