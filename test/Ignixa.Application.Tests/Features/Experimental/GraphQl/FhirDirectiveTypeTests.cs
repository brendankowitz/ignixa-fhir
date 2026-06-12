// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using HotChocolate;
using HotChocolate.Types;
using Ignixa.Application.Features.Experimental.GraphQl.Directives;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Experimental.GraphQl;

public class FhirDirectiveTypeTests
{
    private static ISchema BuildSchema()
        => SchemaBuilder.New()
            .AddQueryType(d => d.Name("Query").Field("ping").Type<StringType>().Resolve("pong"))
            .AddDirectiveType<FhirFlattenDirectiveType>()
            .AddDirectiveType<FhirFirstDirectiveType>()
            .AddDirectiveType<FhirSingletonDirectiveType>()
            .AddDirectiveType<FhirSliceDirectiveType>()
            .Create();

    [Theory]
    [InlineData("flatten")]
    [InlineData("first")]
    [InlineData("singleton")]
    [InlineData("slice")]
    public void GivenSchema_WhenBuilt_ThenDirectiveIsRegisteredOnFieldLocation(string directiveName)
    {
        // Arrange
        var schema = BuildSchema();

        // Act
        var directive = schema.DirectiveTypes.Single(d => d.Name == directiveName);

        // Assert
        directive.ShouldNotBeNull();
        directive.Locations.HasFlag(DirectiveLocation.Field).ShouldBeTrue();
    }

    [Fact]
    public void GivenSchema_WhenBuilt_ThenSliceDirectiveExposesRequiredPathArgument()
    {
        // Arrange
        var schema = BuildSchema();

        // Act
        var slice = schema.DirectiveTypes.Single(d => d.Name == "slice");
        var pathArgument = slice.Arguments.Single(a => a.Name == "path");

        // Assert
        pathArgument.ShouldNotBeNull();
        pathArgument.Type.IsNonNullType().ShouldBeTrue();
    }

    [Fact]
    public void GivenSchema_WhenBuilt_ThenFlattenFirstSingletonHaveNoArguments()
    {
        // Arrange
        var schema = BuildSchema();

        // Act & Assert
        schema.DirectiveTypes.Single(d => d.Name == "flatten").Arguments.Count.ShouldBe(0);
        schema.DirectiveTypes.Single(d => d.Name == "first").Arguments.Count.ShouldBe(0);
        schema.DirectiveTypes.Single(d => d.Name == "singleton").Arguments.Count.ShouldBe(0);
    }
}
