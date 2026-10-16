// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Reading;

/// <summary>The size budget of one response that returns resources: <see cref="EnvelopeJson.DefaultBudget"/> unless the caller asks for more.</summary>
internal static class ResponseBudget
{
    private const string SearchAlternatives = "fhirpath or summary=count for one value, perPatient for one match per patient, elements or columns for smaller pages";

    /// <summary>
    /// The budget to use: <paramref name="requested"/>, the default when null (never above <paramref name="ceiling"/>), or
    /// <paramref name="ceiling"/> with a <c>max-bytes-clamped</c> warning when more was asked for.
    /// </summary>
    /// <param name="requested">The call's <c>maxBytes</c>, or null.</param>
    /// <param name="ceiling">The session's <see cref="Session.FolderSession.MaxResponseBytes"/>.</param>
    /// <param name="issues">Receives the clamp warning.</param>
    /// <param name="cheaperCalls">What to suggest instead of a larger budget; the search tools' alternatives by default.</param>
    /// <exception cref="ToolRequestException">A budget below <see cref="EnvelopeJson.MinBudget"/>.</exception>
    public static int Resolve(int? requested, int ceiling, List<IssueItem> issues, string cheaperCalls = SearchAlternatives)
    {
        ArgumentNullException.ThrowIfNull(issues);

        if (requested is < EnvelopeJson.MinBudget)
        {
            throw ToolRequestException.Error("invalid-parameter", $"'maxBytes' must be at least {EnvelopeJson.MinBudget}, not {requested}.");
        }

        if (requested > ceiling)
        {
            issues.Add(new IssueItem(
                "warning",
                "max-bytes-clamped",
                $"maxBytes {requested} exceeds this server's maximum of {ceiling}, so that was used; ask for less instead: {cheaperCalls}.",
                Location: null));
            return ceiling;
        }

        return requested ?? Math.Min(EnvelopeJson.DefaultBudget, ceiling);
    }
}
