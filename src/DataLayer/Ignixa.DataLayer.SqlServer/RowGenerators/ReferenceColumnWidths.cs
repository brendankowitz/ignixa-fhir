// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;
using Ignixa.Search.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer.RowGenerators;

/// <summary>
/// Checks that a reference's base URI and resource id fit their columns, which have no overflow companion.
/// </summary>
internal static partial class ReferenceColumnWidths
{
    /// <summary>
    /// True when both values fit; otherwise logs a warning naming the parameter, the resource and the lengths, and
    /// returns false so the caller writes no row rather than a truncated one.
    /// </summary>
    public static bool Fit(string? baseUri, int baseUriWidth, string resourceId, int resourceIdWidth, SearchParameterInfo parameter, ResourceWrapper resource, ILogger logger)
    {
        if ((baseUri?.Length ?? 0) <= baseUriWidth && resourceId.Length <= resourceIdWidth)
        {
            return true;
        }

        LogReferenceSkipped(logger, parameter.Url, resource.ResourceType, resource.ResourceId, baseUri?.Length ?? 0, baseUriWidth, resourceId.Length, resourceIdWidth);
        return false;
    }

    // The degraded state this reports (a reference missing from :missing/identifier searches) is alertable by EventId.
    [LoggerMessage(EventId = 4001, EventName = "ReferenceRowSkipped", Level = LogLevel.Warning, Message = "Reference on {SearchParameterUrl} while indexing {ResourceType}/{ResourceId} does not fit its columns (base URI {BaseUriLength} of {BaseUriWidth} characters, id {IdLength} of {IdWidth}) -- row skipped")]
    private static partial void LogReferenceSkipped(ILogger logger, Uri searchParameterUrl, string resourceType, string resourceId, int baseUriLength, int baseUriWidth, int idLength, int idWidth);
}
