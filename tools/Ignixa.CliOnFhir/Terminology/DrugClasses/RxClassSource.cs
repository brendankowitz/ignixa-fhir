// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology.DrugClasses;

/// <summary>Where a drug-class answer came from.</summary>
/// <param name="Kind">Always <c>rxclass</c>.</param>
/// <param name="Server">The RxNav base URL (--rxclass-server).</param>
/// <param name="RetrievedAt">When the oldest RxClass response the answer used was retrieved from the server.</param>
/// <param name="Responses">RxClass responses used.</param>
/// <param name="FromCache">How many of them came from the cache rather than the network.</param>
public sealed record RxClassSource(string Kind, string Server, DateTimeOffset RetrievedAt, int Responses, int FromCache)
{
    public const string RxClassKind = "rxclass";
}
