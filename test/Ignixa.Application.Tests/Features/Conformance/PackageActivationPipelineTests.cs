using System.Reflection;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Conformance;

public sealed class PackageActivationPipelineTests
{
    [Fact]
    public async Task GivenEventStoreConcurrencyExceptionWithSensitiveMessage_WhenActivating_ThenConflictIssueDoesNotExposeExceptionDetails()
    {
        // Arrange
        const string sentinel = "token=do-not-return";
        var packageRepository = Substitute.For<IPackageResourceRepository>();
        packageRepository.GetResourcesForActivationAsync("example.ig", "1.0.0", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Array.Empty<PackageResource>()));

        var concurrencyException = new SourceEventConcurrencyException(1, 2);
        typeof(Exception).GetField("_message", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(concurrencyException, sentinel);

        var eventStore = Substitute.For<ISourceEventStore>();
        eventStore.AppendAsync(
                Arg.Any<IEnumerable<NewSourceEvent>>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<SourceEvent>>(concurrencyException));

        using var state = new ConformanceState();
        var pipeline = new PackageActivationPipeline(
            packageRepository,
            eventStore,
            state,
            Substitute.For<IFhirVersionContext>(),
            Options.Create(new SearchParameterResolutionOptions()),
            NullLogger<PackageActivationPipeline>.Instance);

        // Act
        var result = await pipeline.ActivateAsync("example.ig", "1.0.0", CancellationToken.None);

        // Assert
        result.Success.ShouldBeFalse();
        var issue = result.Issues.ShouldHaveSingleItem();
        issue.Code.ShouldBe("CONFORMANCE_CONFLICT");
        issue.Message.ShouldBe("A concurrent conformance update prevented activation.");
        issue.Message.ShouldNotContain(sentinel);
    }
}
