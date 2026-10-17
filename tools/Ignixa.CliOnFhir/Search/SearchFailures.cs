// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.Search.Indexing;
using Ignixa.Serialization.Abstractions;

namespace Ignixa.CliOnFhir.Search;

/// <summary>Turns the exceptions Ignixa's search pipeline raises for a bad or unsupported request into reportable issues.</summary>
internal static class SearchFailures
{
    public const string NotSupportedCode = "search-not-supported";

    public const string InvalidRequestCode = "invalid-search-request";

    public static ToolRequestException NotSupported(string diagnostics) => ToolRequestException.Error(NotSupportedCode, diagnostics);

    /// <summary>
    /// Maps a failure of query parsing or evaluation. An operation or modifier the pipeline (or the in-memory
    /// interpreter) does not support is <c>search-not-supported</c>; any other request fault from the pipeline is
    /// <c>invalid-search-request</c>.
    /// </summary>
    /// <param name="exception">The pipeline's failure.</param>
    /// <param name="parameters">The query parameters that fail on their own, named in the diagnostics when known.</param>
    public static ToolRequestException From(FhirException exception, string? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        string detail = exception.Issues.FirstOrDefault()?.Diagnostics ?? exception.Message;
        if (parameters is not null)
        {
            detail = $"{parameters}: {detail}";
        }

        return exception is SearchOperationNotSupportedException or SearchModifierNotSupportedException
            ? NotSupported(detail)
            : ToolRequestException.Error(InvalidRequestCode, detail);
    }
}
