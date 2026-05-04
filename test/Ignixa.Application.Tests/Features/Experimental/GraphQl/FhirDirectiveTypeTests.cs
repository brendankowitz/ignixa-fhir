// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Experimental.GraphQl.Directives;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Experimental.GraphQl;

public class FhirDirectiveTypeTests
{
    [Fact]
    public void GivenFlattenDirective_WhenCreated_ThenHasCorrectName()
    {
        var directive = new FhirFlattenDirectiveType();
        directive.ShouldNotBeNull();
    }

    [Fact]
    public void GivenFirstDirective_WhenCreated_ThenHasCorrectName()
    {
        var directive = new FhirFirstDirectiveType();
        directive.ShouldNotBeNull();
    }

    [Fact]
    public void GivenSingletonDirective_WhenCreated_ThenHasCorrectName()
    {
        var directive = new FhirSingletonDirectiveType();
        directive.ShouldNotBeNull();
    }

    [Fact]
    public void GivenSliceDirective_WhenCreated_ThenHasCorrectName()
    {
        var directive = new FhirSliceDirectiveType();
        directive.ShouldNotBeNull();
    }
}
