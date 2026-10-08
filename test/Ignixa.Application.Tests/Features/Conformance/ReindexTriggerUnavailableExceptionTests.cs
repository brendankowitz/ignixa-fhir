using System.Data.Common;
using Ignixa.Application.Features.Conformance;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Conformance;

public sealed class ReindexTriggerUnavailableExceptionTests
{
    [Fact]
    public void GivenRequestFailedExceptionIsWrapped_WhenClassifyingOperationalFailures_ThenItIsOperational()
    {
        var exception = new Exception(
            "Durable task operation failed.",
            new Azure.RequestFailedException("Storage unavailable."));

        ReindexTriggerUnavailableException.IsOperational(exception).ShouldBeTrue();
    }

    [Fact]
    public void GivenDurableTaskStorageException_WhenClassifyingOperationalFailures_ThenItIsOperational()
    {
        var exception = new DurableTask.AzureStorage.Storage.DurableTaskStorageException(
            "Storage unavailable.");

        ReindexTriggerUnavailableException.IsOperational(exception).ShouldBeTrue();
    }

    [Fact]
    public void GivenAggregateContainsOperationalFailure_WhenClassifyingOperationalFailures_ThenItIsOperational()
    {
        var exception = new AggregateException(new IOException("Database unavailable."));

        ReindexTriggerUnavailableException.IsOperational(exception).ShouldBeTrue();
    }

    [Fact]
    public void GivenInvalidOperationException_WhenClassifyingOperationalFailures_ThenItPropagates()
    {
        var exception = new InvalidOperationException("Unexpected result.");

        ReindexTriggerUnavailableException.IsOperational(exception).ShouldBeFalse();
    }

    [Fact]
    public void GivenNestedCancellation_WhenClassifyingOperationalFailures_ThenItPropagates()
    {
        var exception = new Exception(
            "Durable task operation failed.",
            new OperationCanceledException("Request cancelled."));

        ReindexTriggerUnavailableException.IsOperational(exception).ShouldBeFalse();
    }

    [Fact]
    public void GivenAggregateContainsOperationalFailureAndCancellation_WhenClassifyingOperationalFailures_ThenItPropagates()
    {
        var exception = new AggregateException(
            new IOException("Database unavailable."),
            new OperationCanceledException("Request cancelled."));

        ReindexTriggerUnavailableException.IsOperational(exception).ShouldBeFalse();
    }

    [Fact]
    public void GivenAggregateContainsOperationalFailureAndProgrammerError_WhenClassifyingOperationalFailures_ThenItPropagates()
    {
        var exception = new AggregateException(
            new IOException("Database unavailable."),
            new InvalidOperationException("Unexpected result."));

        ReindexTriggerUnavailableException.IsOperational(exception).ShouldBeFalse();
    }

    [Fact]
    public void GivenOperationalFailureWrapsProgrammerError_WhenClassifyingOperationalFailures_ThenItIsOperational()
    {
        var exception = new TestDbException(
            "Database unavailable.",
            new InvalidOperationException("Provider state is invalid."));

        ReindexTriggerUnavailableException.IsOperational(exception).ShouldBeTrue();
    }

    private sealed class TestDbException(string message, Exception innerException)
        : DbException(message, innerException);
}
