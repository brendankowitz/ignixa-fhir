// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Experimental.GraphQl.Resolvers;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Experimental.GraphQl;

public class ReferenceResolutionTests
{
    [Theory]
    [InlineData("Patient/123", "Patient", true)]
    [InlineData("Patient/123", "Observation", false)]
    [InlineData("Observation/456", "Observation", true)]
    public void GivenTypeFilter_WhenCheckingReference_ThenFiltersCorrectly(
        string reference, string typeFilter, bool shouldMatch)
    {
        var key = FieldResolver.ParseFhirReference(reference);
        key.ShouldNotBeNull();

        var matches = string.Equals(key.ResourceType, typeFilter, StringComparison.Ordinal);
        matches.ShouldBe(shouldMatch);
    }
}
