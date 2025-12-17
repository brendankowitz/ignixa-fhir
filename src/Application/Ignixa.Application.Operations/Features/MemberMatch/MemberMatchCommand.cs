// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.SourceNodes;
using Medino;

namespace Ignixa.Application.Operations.Features.MemberMatch;

/// <summary>
/// Command for FHIR $member-match operation.
/// Matches a member across payer systems using demographics and coverage information.
/// Based on Da Vinci HRex specification.
/// </summary>
/// <param name="MemberPatient">Patient resource with demographics for matching (required).</param>
/// <param name="CoverageToMatch">Coverage resource with prior plan information (required).</param>
/// <param name="CoverageToLink">Optional coverage resource with new plan information.</param>
/// <param name="Consent">Optional consent resource for information sharing authorization.</param>
public record MemberMatchCommand(
    ResourceJsonNode MemberPatient,
    ResourceJsonNode CoverageToMatch,
    ResourceJsonNode? CoverageToLink = null,
    ResourceJsonNode? Consent = null) : IRequest<MemberMatchResult>;
