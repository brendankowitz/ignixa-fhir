// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Reading;

/// <summary>The size budget of one response that returns resources: <see cref="EnvelopeJson.DefaultBudget"/> unless the caller asks for more.</summary>
internal static class ResponseBudget
{
    /// <summary>
    /// The budget to use: <paramref name="requested"/>, the default when null, or <see cref="EnvelopeJson.MaxBytes"/> with a
    /// <c>max-bytes-clamped</c> warning when more was asked for.
    /// </summary>
    /// <exception cref="ToolRequestException">A budget below <see cref="EnvelopeJson.MinBudget"/>.</exception>
    public static int Resolve(int? requested, List<IssueItem> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);

        if (requested is < EnvelopeJson.MinBudget)
        {
            throw ToolRequestException.Error("invalid-parameter", $"'maxBytes' must be at least {EnvelopeJson.MinBudget}, not {requested}.");
        }

        if (requested > EnvelopeJson.MaxBytes)
        {
            issues.Add(new IssueItem("warning", "max-bytes-clamped", $"maxBytes {requested} exceeds the maximum of {EnvelopeJson.MaxBytes}, so that was used; narrow the call (elements, a date range, summary=count) instead of asking for more.", Location: null));
            return EnvelopeJson.MaxBytes;
        }

        return requested ?? EnvelopeJson.DefaultBudget;
    }
}
