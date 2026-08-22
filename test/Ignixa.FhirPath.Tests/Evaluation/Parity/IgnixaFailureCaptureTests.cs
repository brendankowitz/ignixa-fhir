using Microsoft.Extensions.Logging;

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

/// <summary>
/// Pins the capture mechanism itself.
/// </summary>
/// <remarks>
/// Every assertion built on this capture expects a specific set of failures, and one of them expects
/// none at all. A capture that recorded nothing would satisfy that one silently, which is the same
/// shape as the defect the capture was written to close - so the mechanism is asserted here rather
/// than only through what the harness happens to observe.
/// </remarks>
public class IgnixaFailureCaptureTests
{
    [Fact]
    public void GivenALoggedFailure_WhenInsideACaptureScope_ThenItIsRecordedAsAValue()
    {
        // Arrange
        var logger = IgnixaFailureCapture.Instance.CreateLogger("test");

        // Act
        var failures = IgnixaFailureCapture.While(() => LogExtractionFailure(logger));

        // Assert
        var failure = failures.ShouldHaveSingleItem();
        failure.Stage.ShouldBe("FailedToExtractValues");
        failure.ParameterUrl.ShouldBe("http://example.org/SearchParameter/probe");
        failure.FailingExpression.ShouldBe("Patient.name");
        failure.ElementType.ShouldBe("Patient");
        failure.ExceptionType.ShouldBe("NotSupportedException");
        failure.ContainedAThrow.ShouldBeTrue();
    }

    [Fact]
    public void GivenALoggedSkip_WhenInsideACaptureScope_ThenItIsNotCountedAsAContainedThrow()
    {
        // Arrange
        var logger = IgnixaFailureCapture.Instance.CreateLogger("test");

        // Act
        var failures = IgnixaFailureCapture.While(() => LogElementTypeSkip(logger));

        // Assert
        var failure = failures.ShouldHaveSingleItem();
        failure.Stage.ShouldBe("FhirElementTypeNotSupported");
        failure.ElementType.ShouldBe("canonical");
        failure.ExceptionType.ShouldBeEmpty();
        failure.ContainedAThrow.ShouldBeFalse();
    }

    [Fact]
    public void GivenTwoCaptureScopes_WhenRunInSequence_ThenNeitherSeesTheOthersFailures()
    {
        // Arrange
        var logger = IgnixaFailureCapture.Instance.CreateLogger("test");

        // Act
        var first = IgnixaFailureCapture.While(() => LogExtractionFailure(logger));
        var second = IgnixaFailureCapture.While(() => { });

        // Assert
        first.ShouldHaveSingleItem();
        second.ShouldBeEmpty();
    }

    [Fact]
    public void GivenAFailureLoggedOutsideAnyScope_WhenCaptured_ThenItThrowsRatherThanVanishing()
    {
        // Arrange
        var logger = IgnixaFailureCapture.Instance.CreateLogger("test");

        // Act
        var act = () => LogExtractionFailure(logger);

        // Assert
        Should.Throw<InvalidOperationException>(act);
    }

    private static void LogExtractionFailure(ILogger logger) =>
        logger.Log(
            LogLevel.Warning,
            new EventId(1, "FailedToExtractValues"),
            new[]
            {
                new KeyValuePair<string, object?>("FhirPathExpression", "Patient.name"),
                new KeyValuePair<string, object?>("ElementType", "Patient"),
                new KeyValuePair<string, object?>("SearchParameterUrl", "http://example.org/SearchParameter/probe"),
            },
            new NotSupportedException(),
            static (state, exception) => "failed");

    private static void LogElementTypeSkip(ILogger logger) =>
        logger.Log(
            LogLevel.Warning,
            new EventId(2, "FhirElementTypeNotSupported"),
            new[] { new KeyValuePair<string, object?>("ElementType", "canonical") },
            null,
            static (state, exception) => "skipped");
}
