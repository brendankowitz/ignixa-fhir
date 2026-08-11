// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Ignixa.Search.Models;

namespace Ignixa.Search.Indexing;

/// <summary>
/// Thrown when a search parameter carries a modifier the server does not support for that parameter,
/// for example <c>_id:above</c> or <c>birthdate:exact</c>.
/// </summary>
/// <remarks>
/// <para>
/// FHIR R4 treats this as a different class of failure from an unknown or unsupported search
/// <i>parameter</i>. For a parameter, the spec says servers "SHOULD ignore unknown or unsupported
/// parameters" because proxies and HTTP stacks inject parameters the client never sent. For a modifier
/// it is a SHALL: "Server SHALL reject any search request that ... is suffixed by a modifier that the
/// server does not support for that parameter ... using an HTTP 400 error with an OperationOutcome with
/// a clear error message."
/// </para>
/// <para>
/// The distinction matters because the two failure modes are not equally recoverable. Dropping an
/// unrecognised parameter narrows nothing the client asked for, and the self link tells them what was
/// used. Dropping a modifier silently <i>widens</i> the result set — <c>_id:above=abc</c> becomes an
/// unfiltered search — and a client reading only the entry list cannot distinguish "no filter applied"
/// from "filter applied, everything matched".
/// </para>
/// <para>
/// Deriving from <see cref="InvalidSearchOperationException"/> keeps every existing handler that catches
/// the base type working unchanged; callers that want the stricter treatment catch this type first.
/// </para>
/// </remarks>
public class SearchModifierNotSupportedException : InvalidSearchOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SearchModifierNotSupportedException"/> class.
    /// </summary>
    /// <param name="message">The message describing the unsupported modifier and parameter.</param>
    public SearchModifierNotSupportedException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Throws when <paramref name="options"/> recorded a client search modifier the server does not
    /// support for that parameter. Every caller that runs a client-supplied search against the
    /// compiler -- not just the HTTP search endpoints -- must call this, because <see
    /// cref="Parsing.SearchOptionsBuilder"/> never throws for this case on its own: it records the
    /// modifier in <see cref="SearchOptions.UnsupportedModifierParams"/> and keeps building, so a
    /// caller that skips this check silently executes the widened, unfiltered search instead of the
    /// SHALL-mandated rejection.
    /// </summary>
    /// <param name="options">The options produced by <see cref="Parsing.SearchOptionsBuilder"/>.</param>
    public static void ThrowIfAny(SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.UnsupportedModifierParams.Count == 0)
        {
            return;
        }

        var parameterList = string.Join(", ", options.UnsupportedModifierParams.Select(p => $"'{p}'"));
        var message = string.IsNullOrEmpty(options.ResourceType)
            ? $"Search parameter(s) use a modifier that is not supported: {parameterList}"
            : $"Search parameter(s) use a modifier that is not supported for resource type '{options.ResourceType}': {parameterList}";

        throw new SearchModifierNotSupportedException(message);
    }
}
