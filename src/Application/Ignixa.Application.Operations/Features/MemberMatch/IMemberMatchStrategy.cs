// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Application.Operations.Features.MemberMatch;

/// <summary>
/// Strategy interface for member matching logic.
/// Allows custom matching implementations to be plugged in via dependency injection.
/// </summary>
/// <remarks>
/// The default implementation uses identifier-based matching (subscriber ID, member ID).
/// Custom strategies can implement more sophisticated matching logic such as:
/// - Probabilistic matching using demographics
/// - Integration with external identity resolution services
/// - Organization-specific business rules
/// </remarks>
public interface IMemberMatchStrategy
{
    /// <summary>
    /// Attempt to match a member using provided demographics and coverage.
    /// </summary>
    /// <param name="memberPatient">Patient resource containing demographics for matching.</param>
    /// <param name="coverageToMatch">Coverage resource with prior plan information.</param>
    /// <param name="coverageToLink">Optional coverage resource with new plan information.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Match result containing:
    /// - Success=true with MemberIdentifier if unique match found
    /// - Success=false with ErrorCode="no-match" if no match found
    /// - Success=false with ErrorCode="multiple-matches" if multiple matches found
    /// </returns>
    Task<MemberMatchResult> MatchAsync(
        ResourceJsonNode memberPatient,
        ResourceJsonNode coverageToMatch,
        ResourceJsonNode? coverageToLink,
        CancellationToken cancellationToken);
}
