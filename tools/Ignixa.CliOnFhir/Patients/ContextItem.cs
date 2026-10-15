// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Patients;

/// <summary>
/// One resource (or, for a lab code, one series) in a patient's context, as the patient tools summarize it. Every value is
/// read from the resource with FHIRPath; free text (narrative, notes, attachments, string values) is never copied here.
/// Members without a value are left out.
/// </summary>
/// <param name="Reference">The resource, <c>Type/id</c>; for a lab code, its latest observation.</param>
/// <param name="NodeKey">The <c>context_expand</c> node this item opens, when it has one.</param>
/// <param name="Code">The main code as <c>system|code</c> (the first coding with both).</param>
/// <param name="Display">The main code's text, else its first coding display.</param>
/// <param name="Status">The status code (a Condition's or AllergyIntolerance's clinical status).</param>
/// <param name="Date">The main clinical date, as written (an Encounter's period start).</param>
/// <param name="Active">Whether a problem or medication met the active rule; absent when the rule was not applied.</param>
/// <param name="Count">For a lab code, the observations in its series.</param>
/// <param name="Value">A numeric <c>valueQuantity</c>; for a lab code, the latest one.</param>
/// <param name="Unit">The quantity's unit, else its code.</param>
/// <param name="ValueType">The FHIR type of a value that is not a Quantity (for example <c>CodeableConcept</c>); its content is in the resource.</param>
/// <param name="Trend">For a lab code: <c>up</c>, <c>down</c>, <c>flat</c> or <c>n/a</c> (see the domain's rule).</param>
/// <param name="LinkedBy">In a problem node, how a medication is linked to the problem: <c>reasonReference</c> or <c>reasonCode</c>.</param>
/// <param name="MatchedBy">In <c>context_find</c>, what matched: <c>code</c> or <c>text</c>.</param>
public sealed record ContextItem(
    string Reference,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NodeKey = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Code = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Display = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Status = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Date = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Active = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Count = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Value = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Unit = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ValueType = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Trend = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LinkedBy = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? MatchedBy = null);
