// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.CodeSystems;

/// <summary>Facts of <c>folder_code_systems</c> with <c>text</c>.</summary>
/// <param name="Words">The words searched, each a case-insensitive prefix of a word of the display.</param>
/// <param name="Total">Codes matching, in all.</param>
/// <param name="Offset">Codes skipped.</param>
/// <param name="Returned">Codes in this page.</param>
/// <param name="Systems">System alias to system URI, for the systems of the rows.</param>
/// <param name="Codes">This page, most resources first.</param>
public sealed record CodeTextFacts(
    IReadOnlyList<string> Words,
    int Total,
    int Offset,
    int Returned,
    IReadOnlyDictionary<string, string> Systems,
    IReadOnlyList<CodeTextMatch> Codes);
