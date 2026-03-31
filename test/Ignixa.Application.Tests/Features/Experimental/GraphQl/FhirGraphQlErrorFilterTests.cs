// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using HotChocolate;
using Ignixa.Application.Features.Experimental.GraphQl.Pipeline;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Experimental.GraphQl;

public class FhirGraphQlErrorFilterTests
{
    [Fact]
    public void GivenGenericError_WhenFiltered_ThenAddsOperationOutcomeExtension()
    {
        // Arrange
        var filter = new FhirGraphQlErrorFilter();
        var error = ErrorBuilder.New()
            .SetMessage("Something went wrong")
            .Build();

        // Act
        var result = filter.OnError(error);

        // Assert
        result.Extensions.ShouldNotBeNull();
        result.Extensions.ShouldContainKey("resource");
        var resource = result.Extensions!["resource"] as IDictionary<string, object?>;
        resource.ShouldNotBeNull();
        resource!["resourceType"].ShouldBe("OperationOutcome");
    }

    [Fact]
    public void GivenReferenceNotFoundError_WhenFiltered_ThenMapsToNotFoundIssueCode()
    {
        // Arrange
        var filter = new FhirGraphQlErrorFilter();
        var error = ErrorBuilder.New()
            .SetMessage("Reference could not be resolved")
            .SetCode("FHIR_REFERENCE_NOT_FOUND")
            .Build();

        // Act
        var result = filter.OnError(error);

        // Assert
        var resource = result.Extensions!["resource"] as IDictionary<string, object?>;
        var issues = resource!["issue"] as IEnumerable<object>;
        var issue = issues!.Cast<IDictionary<string, object?>>().First();
        issue["code"].ShouldBe("not-found");
        issue["severity"].ShouldBe("error");
    }

    [Fact]
    public void GivenError_WhenFiltered_ThenPreservesOriginalMessage()
    {
        // Arrange
        var filter = new FhirGraphQlErrorFilter();
        var error = ErrorBuilder.New()
            .SetMessage("Test error message")
            .Build();

        // Act
        var result = filter.OnError(error);

        // Assert
        result.Message.ShouldBe("Test error message");
        var resource = result.Extensions!["resource"] as IDictionary<string, object?>;
        var issues = resource!["issue"] as IEnumerable<object>;
        var issue = issues!.Cast<IDictionary<string, object?>>().First();
        issue["diagnostics"].ShouldBe("Test error message");
    }
}
