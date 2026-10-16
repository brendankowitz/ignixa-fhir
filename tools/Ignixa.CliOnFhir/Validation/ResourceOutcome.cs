// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;

namespace Ignixa.CliOnFhir.Validation;

/// <summary>One validated resource's full result, returned only on request (<c>detail</c>).</summary>
/// <param name="Reference">The resource (<c>Type/id</c>, the id as its digest when not a FHIR id).</param>
/// <param name="Verdict"><c>valid</c>, <c>invalid</c> or <c>unknown</c>.</param>
/// <param name="Rules">The rule of each issue, in the outcome's order, as the summary names them.</param>
/// <param name="OperationOutcome">The validator's OperationOutcome as it produced it: its texts may quote the resource, so it is resource content.</param>
public sealed record ResourceOutcome(string Reference, string Verdict, IReadOnlyList<string> Rules, JsonNode OperationOutcome);
