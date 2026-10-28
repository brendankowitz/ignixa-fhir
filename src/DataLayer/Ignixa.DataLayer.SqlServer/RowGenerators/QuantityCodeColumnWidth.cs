// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;
using Ignixa.Search.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer.RowGenerators;

/// <summary>
/// Checks that a quantity code fits <c>QuantityCode.Value</c>. A Quantity's free-text <c>unit</c> is indexed as a code, and
/// resolving one over the width fails the whole write with SQL error 2628, so it is skipped here instead.
/// </summary>
internal static partial class QuantityCodeColumnWidth
{
    private static readonly int Width = SearchParamColumnWidths.For("QuantityCode", "Value");

    /// <summary>
    /// True when <paramref name="code"/> fits; otherwise logs a warning naming the parameter, the resource and the length
    /// (never the unit text, which is patient-entered) and returns false so the caller writes no row.
    /// </summary>
    public static bool Fit(string code, SearchParameterInfo parameter, ResourceWrapper resource, ILogger logger)
    {
        if (code.Length <= Width)
        {
            return true;
        }

        LogQuantityCodeSkipped(logger, parameter.Url, resource.ResourceType, resource.ResourceId, code.Length, Width);
        return false;
    }

    // The degraded state this reports (a unit missing from quantity and :missing searches) is alertable by EventId.
    [LoggerMessage(EventId = 4002, EventName = "QuantityCodeRowSkipped", Level = LogLevel.Warning, Message = "Quantity code on {SearchParameterUrl} while indexing {ResourceType}/{ResourceId} does not fit its column ({CodeLength} of {CodeWidth} characters) -- row skipped")]
    private static partial void LogQuantityCodeSkipped(ILogger logger, Uri searchParameterUrl, string resourceType, string resourceId, int codeLength, int codeWidth);
}
