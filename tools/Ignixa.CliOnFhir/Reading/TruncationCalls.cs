// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Reading;

/// <summary>
/// The follow-up calls of a page that was cut: the call that reads the rest, and the calls that make the same page smaller or
/// larger. Built only from the request's own arguments and the resource type's name, never from resource content.
/// </summary>
internal static class TruncationCalls
{
    /// <summary>The call that reads on from <paramref name="offset"/>, keeping the caller's trimming and budget.</summary>
    public static NextCall Continue(NextCall call, int offset, string? elements, int? maxBytes) =>
        With(call, hint: null, offset, elements, maxBytes);

    /// <summary>
    /// The same page with <c>elements</c> set to <paramref name="suggested"/> (or, with none, an unchanged call whose hint asks
    /// for the fields needed), so that more resources fit in a response.
    /// </summary>
    public static NextCall Trim(NextCall call, string? suggested) => With(
        call,
        suggested is null
            ? "same page, elements set to the fields you need"
            : "same page, elements limited to the usual fields",
        offset: null,
        suggested,
        maxBytes: null);

    /// <summary>The same page with the largest response budget, <paramref name="ceiling"/>.</summary>
    public static NextCall Larger(NextCall call, int ceiling) => With(
        call,
        $"same page, largest budget ({ceiling} bytes); narrowing is cheaper",
        offset: null,
        elements: null,
        ceiling);

    /// <summary>A copy of <paramref name="call"/> with <paramref name="hint"/>, and each argument given (not null) set; offset 0 is the default and is left out.</summary>
    public static NextCall With(NextCall call, string? hint, int? offset, string? elements, int? maxBytes)
    {
        Dictionary<string, object> args = new(call.Args, StringComparer.Ordinal);
        if (offset == 0)
        {
            args.Remove("offset");
        }
        else if (offset is int skipped)
        {
            args["offset"] = skipped;
        }

        if (elements is not null)
        {
            args["elements"] = elements;
        }

        if (maxBytes is int budget)
        {
            args["maxBytes"] = budget;
        }

        return new NextCall(call.Tool, args, hint);
    }
}
