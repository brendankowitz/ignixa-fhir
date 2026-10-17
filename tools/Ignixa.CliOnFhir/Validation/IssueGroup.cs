// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Validation;

/// <summary>Validator issues that share severity, profile, rule, constraint, ValueSet and element path.</summary>
/// <param name="Severity"><c>fatal</c>, <c>error</c>, <c>warning</c> or <c>information</c>.</param>
/// <param name="Profile">The StructureDefinition whose checks raised them (the core definition, or a profile).</param>
/// <param name="Rule">The validator's issue code, <c>invariant</c>, or the name of a check that did not conclude.</param>
/// <param name="Constraint">For an invariant or a constraint that could not be evaluated, its key; absent otherwise.</param>
/// <param name="ValueSet">For a binding rule, the bound ValueSet; absent otherwise.</param>
/// <param name="Path">The element path, without repetition indexes; a name the data chose is a digest.</param>
/// <param name="Description">The tool's own description of the rule; absent for a rule it does not know. Never the validator's message.</param>
/// <param name="Count">Issues in the group.</param>
/// <param name="Resources">Distinct resources with at least one.</param>
/// <param name="Samples">Up to three of those resources (<c>Type/id</c>), in validation order.</param>
public sealed record IssueGroup(
    string Severity,
    string Profile,
    string Rule,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Constraint,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ValueSet,
    string Path,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description,
    int Count,
    int Resources,
    IReadOnlyList<string> Samples);
