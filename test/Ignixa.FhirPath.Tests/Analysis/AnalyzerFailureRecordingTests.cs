// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Analysis;
using Ignixa.FhirPath.Parser;
using Ignixa.Specification.Extensions;
using Shouldly;

namespace Ignixa.FhirPath.Tests.Analysis;

/// <summary>
/// <see cref="FhirPathAnalyzer.Analyze(Expressions.Expression, string)"/> records an ordinary failure during
/// its walk as an issue, but lets cancellation through instead of reporting a partial analysis (#538).
/// </summary>
public class AnalyzerFailureRecordingTests
{
    private readonly FhirPathParser _parser = new();

    [Fact]
    public void GivenASchemaThatFailsMidWalk_WhenAnalyzing_ThenTheFailureIsRecordedAsAnIssue()
    {
        // Arrange
        var analyzer = AnalyzerFailingOn("HumanName", () => new InvalidOperationException("schema defect"));

        // Act
        var result = analyzer.Analyze(_parser.Parse("name.family"), "Patient");

        // Assert
        result.Issues.ShouldContain(issue => issue.Message.Contains("Analysis failed: schema defect", StringComparison.Ordinal));
    }

    [Fact]
    public void GivenCancellationMidWalk_WhenAnalyzing_ThenItPropagates()
    {
        // Arrange
        var analyzer = AnalyzerFailingOn("HumanName", () => new OperationCanceledException());

        // Act & Assert
        Should.Throw<OperationCanceledException>(() => analyzer.Analyze(_parser.Parse("name.family"), "Patient"));
    }

    private static FhirPathAnalyzer AnalyzerFailingOn(string typeName, Func<Exception> failure) =>
        new(new ThrowingSchemaProvider(FhirVersion.R4.GetSchemaProvider(), typeName, failure));
}
