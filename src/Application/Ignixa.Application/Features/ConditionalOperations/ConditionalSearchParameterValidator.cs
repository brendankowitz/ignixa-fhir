// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Exceptions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;

namespace Ignixa.Application.Features.ConditionalOperations;

/// <summary>
/// Rejects conditional criteria that would otherwise run after their unsupported predicate was removed.
/// </summary>
internal static class ConditionalSearchParameterValidator
{
    public static void ThrowIfInvalid(SearchOptions searchOptions)
    {
        ArgumentNullException.ThrowIfNull(searchOptions);

        SearchModifierNotSupportedException.ThrowIfAny(searchOptions);

        if (searchOptions.UnsupportedParams.Count > 0)
        {
            string? pendingReindexDiagnostic = searchOptions.BundleIssues
                .Select(issue => issue.Diagnostics)
                .FirstOrDefault(diagnostic => diagnostic?.Contains("pending reindex", StringComparison.OrdinalIgnoreCase) == true);

            throw new BadRequestException(
                pendingReindexDiagnostic
                ?? $"Conditional operations cannot use unsupported search parameter '{searchOptions.UnsupportedParams[0]}'.");
        }

        SearchParameterInfo? partiallyIndexed = searchOptions.ResolvedSearchParameters
            .FirstOrDefault(parameter => !parameter.IsSearchable && parameter.IsSupported);
        if (partiallyIndexed is not null)
        {
            throw new BadRequestException(
                $"Conditional operations cannot use partially indexed search parameters ('{partiallyIndexed.Code}').");
        }
    }
}
