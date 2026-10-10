// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Api.Infrastructure;
using static Ignixa.Application.Infrastructure.LogSanitizationExtensions;
using Ignixa.Models;
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.Api.Endpoints;

internal static class SearchRequestHandling
{
    public static IResult? CheckStrictHandling(
        HttpContext context,
        SearchOptions searchOptions,
        string? resourceType,
        ILogger logger)
    {
        if (searchOptions.UnsupportedModifierParams.Count > 0 &&
            !PreferHeaderParser.IsLenientHandling(context.Request.Headers))
        {
            logger.LogWarning(
                "Unsupported search modifier(s) found: {UnsupportedModifierParams}",
                string.Join(", ", searchOptions.UnsupportedModifierParams.Select(parameter => parameter.SanitizeForLog())));

            return BuildUnsupportedParametersResult(searchOptions.UnsupportedModifierParams, resourceType, isModifierRejection: true);
        }

        if (!PreferHeaderParser.IsStrictHandling(context.Request.Headers) ||
            searchOptions.UnsupportedParams.Count == 0)
        {
            return null;
        }

        logger.LogWarning(
            "Rejecting search: unsupported parameters {UnsupportedParams}",
            string.Join(", ", searchOptions.UnsupportedParams.Select(parameter => parameter.SanitizeForLog())));

        return BuildUnsupportedParametersResult(searchOptions.UnsupportedParams, resourceType, isModifierRejection: false);
    }

    private static IResult BuildUnsupportedParametersResult(
        IReadOnlyList<string> unsupportedParams,
        string? resourceType,
        bool isModifierRejection)
    {
        var operationOutcome = new OperationOutcome();
        foreach (var parameter in unsupportedParams)
        {
            var diagnostics = isModifierRejection
                ? (resourceType is not null
                    ? $"Search parameter '{parameter}' uses a modifier that is not supported for resource type '{resourceType}'"
                    : $"Search parameter '{parameter}' uses a modifier that is not supported")
                : (resourceType is not null
                    ? $"Search parameter '{parameter}' is not supported for resource type '{resourceType}'"
                    : $"Search parameter '{parameter}' is not supported");

            operationOutcome.Issue.Add(new OperationOutcomeIssue
            {
                SeverityCode = OperationOutcomeIssue.IssueSeverityCode.Error,
                IssueTypeCode = OperationOutcomeIssue.IssueTypeCommon.NotSupported,
                Diagnostics = diagnostics,
            });
        }

        return Results.BadRequest(operationOutcome.MutableNode);
    }
}
