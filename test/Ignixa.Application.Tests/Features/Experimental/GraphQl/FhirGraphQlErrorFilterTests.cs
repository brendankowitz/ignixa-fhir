// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using HotChocolate;
using Ignixa.Application.Features.Experimental.Configuration;
using Ignixa.Application.Features.Experimental.GraphQl.Pipeline;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Experimental.GraphQl;

public class FhirGraphQlErrorFilterTests
{
    private static FhirGraphQlErrorFilter BuildFilter(bool includeExceptionDetails = false)
    {
        var options = new ExperimentalOptions();
        options.Features.GraphQl.IncludeExceptionDetails = includeExceptionDetails;
        return new FhirGraphQlErrorFilter(
            Substitute.For<ILogger<FhirGraphQlErrorFilter>>(),
            Options.Create(options));
    }

    [Theory]
    [InlineData("FHIR_REFERENCE_NOT_FOUND", "not-found")]
    [InlineData("FHIR_NOT_FOUND", "not-found")]
    [InlineData("FHIR_REFERENCE_NOT_SUPPORTED", "not-supported")]
    [InlineData("FHIR_VERSION_CONFLICT", "conflict")]
    [InlineData("INVALID_RESOURCE", "invalid")]
    [InlineData("FHIRPATH_INVALID", "invalid")]
    [InlineData("FHIR_OPERATION_FAILED", "exception")]
    [InlineData("FHIR_SINGLETON_VIOLATION", "multiple-matches")]
    [InlineData("FHIR_SYNTAX_ERROR", "invalid")]
    [InlineData("FHIR_INVALID_INSTANCE_QUERY", "invalid")]
    [InlineData("FHIR_UNKNOWN_RESOURCE_TYPE", "not-supported")]
    [InlineData("FHIR_INVALID_ID", "invalid")]
    [InlineData("FHIR_POST_PROCESSING_FAILED", "exception")]
    [InlineData("HC0013", "too-costly")]
    [InlineData("HC0014", "too-costly")]
    [InlineData("AUTH_NOT_AUTHORIZED", "forbidden")]
    [InlineData("SOME_UNKNOWN_CODE", "exception")]
    public void GivenErrorCode_WhenFiltered_ThenMapsToExpectedIssueType(string errorCode, string expectedIssueCode)
    {
        // Arrange
        var filter = BuildFilter();
        var error = ErrorBuilder.New()
            .SetMessage("Mapping test")
            .SetCode(errorCode)
            .Build();

        // Act
        var result = filter.OnError(error);

        // Assert
        var resource = result.Extensions!["resource"] as JsonObject;
        var issue = (resource!["issue"] as JsonArray)![0] as JsonObject;
        issue!["code"]!.GetValue<string>().ShouldBe(expectedIssueCode);
    }

    [Fact]
    public void GivenNoErrorCode_WhenFiltered_ThenMapsToExceptionIssueType()
    {
        // Arrange
        var filter = BuildFilter();
        var error = ErrorBuilder.New()
            .SetMessage("No code supplied")
            .Build();

        // Act
        var result = filter.OnError(error);

        // Assert
        var resource = result.Extensions!["resource"] as JsonObject;
        var issue = (resource!["issue"] as JsonArray)![0] as JsonObject;
        issue!["code"]!.GetValue<string>().ShouldBe("exception");
    }

    [Fact]
    public void GivenGenericError_WhenFiltered_ThenAddsOperationOutcomeExtension()
    {
        // Arrange
        var filter = BuildFilter();
        var error = ErrorBuilder.New()
            .SetMessage("Something went wrong")
            .Build();

        // Act
        var result = filter.OnError(error);

        // Assert
        result.Extensions.ShouldNotBeNull();
        result.Extensions.ShouldContainKey("resource");
        var resource = result.Extensions!["resource"] as JsonObject;
        resource.ShouldNotBeNull();
        resource!["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
    }

    [Fact]
    public void GivenReferenceNotFoundError_WhenFiltered_ThenMapsToNotFoundIssueCode()
    {
        // Arrange
        var filter = BuildFilter();
        var error = ErrorBuilder.New()
            .SetMessage("Reference could not be resolved")
            .SetCode("FHIR_REFERENCE_NOT_FOUND")
            .Build();

        // Act
        var result = filter.OnError(error);

        // Assert
        var resource = result.Extensions!["resource"] as JsonObject;
        var issues = resource!["issue"] as JsonArray;
        issues.ShouldNotBeNull();
        var issue = issues![0] as JsonObject;
        issue!["code"]!.GetValue<string>().ShouldBe("not-found");
        issue["severity"]!.GetValue<string>().ShouldBe("error");
    }

    [Fact]
    public void GivenSingletonViolationError_WhenFiltered_ThenMapsToMultipleMatchesIssueCode()
    {
        // Arrange
        var filter = BuildFilter();
        var error = ErrorBuilder.New()
            .SetMessage("@singleton assertion failed")
            .SetCode("FHIR_SINGLETON_VIOLATION")
            .Build();

        // Act
        var result = filter.OnError(error);

        // Assert
        var resource = result.Extensions!["resource"] as JsonObject;
        var issue = (resource!["issue"] as JsonArray)![0] as JsonObject;
        issue!["code"]!.GetValue<string>().ShouldBe("multiple-matches");
    }

    [Fact]
    public void GivenError_WhenFiltered_ThenPreservesOriginalMessage()
    {
        // Arrange
        var filter = BuildFilter();
        var error = ErrorBuilder.New()
            .SetMessage("Test error message")
            .Build();

        // Act
        var result = filter.OnError(error);

        // Assert
        result.Message.ShouldBe("Test error message");
        var resource = result.Extensions!["resource"] as JsonObject;
        var issues = resource!["issue"] as JsonArray;
        var issue = issues![0] as JsonObject;
        issue!["diagnostics"]!.GetValue<string>().ShouldBe("Test error message");
    }

    [Fact]
    public void GivenExceptionAndDetailsDisabled_WhenFiltered_ThenDiagnosticsOmitInternalDetails()
    {
        // Arrange
        var filter = BuildFilter(includeExceptionDetails: false);
        var error = ErrorBuilder.New()
            .SetMessage("Public message")
            .SetException(new InvalidOperationException("internal detail"))
            .Build();

        // Act
        var result = filter.OnError(error);

        // Assert
        var resource = result.Extensions!["resource"] as JsonObject;
        var issue = (resource!["issue"] as JsonArray)![0] as JsonObject;
        issue!["diagnostics"]!.GetValue<string>().ShouldBe("Public message");
    }

    [Fact]
    public void GivenExceptionAndDetailsEnabled_WhenFiltered_ThenDiagnosticsIncludeExceptionDetails()
    {
        // Arrange
        var filter = BuildFilter(includeExceptionDetails: true);
        var error = ErrorBuilder.New()
            .SetMessage("Public message")
            .SetException(new InvalidOperationException("internal detail"))
            .Build();

        // Act
        var result = filter.OnError(error);

        // Assert
        var resource = result.Extensions!["resource"] as JsonObject;
        var issue = (resource!["issue"] as JsonArray)![0] as JsonObject;
        var diagnostics = issue!["diagnostics"]!.GetValue<string>();
        diagnostics.ShouldContain("InvalidOperationException");
        diagnostics.ShouldContain("internal detail");
    }
}
