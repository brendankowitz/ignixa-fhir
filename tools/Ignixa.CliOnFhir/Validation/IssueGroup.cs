// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Validation;

/// <summary>Validator issues that share severity, profile, rule, element path and message template.</summary>
/// <param name="Severity"><c>fatal</c>, <c>error</c>, <c>warning</c> or <c>information</c>.</param>
/// <param name="Profile">The StructureDefinition whose checks raised them (the core definition, or a profile).</param>
/// <param name="Rule">The validator's issue code or constraint key, or the name of a check that did not conclude.</param>
/// <param name="Path">The element path, without repetition indexes.</param>
/// <param name="Message">The validator's message with values from the data replaced by '…'; null when too long to show.</param>
/// <param name="Count">Issues in the group.</param>
/// <param name="Resources">Distinct resources with at least one.</param>
/// <param name="Samples">Up to three of those resources (<c>Type/id</c>), in validation order.</param>
public sealed record IssueGroup(string Severity, string Profile, string Rule, string Path, string? Message, int Count, int Resources, IReadOnlyList<string> Samples);
