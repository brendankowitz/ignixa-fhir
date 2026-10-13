using Ignixa.Abstractions;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using static Ignixa.Application.Tests.BackgroundOperations.ImportActivityRunner;

namespace Ignixa.Application.Tests.BackgroundOperations;

public class BulkImportStorageFailureTests
{
    [Theory]
    [InlineData(true, "allocate")]
    [InlineData(true, "write")]
    [InlineData(true, "commit")]
    [InlineData(false, "allocate")]
    [InlineData(false, "write")]
    [InlineData(false, "commit")]
    public async Task GivenStorageFailure_WhenImporting_ThenItStopsWithoutReportingStoredResourcesAsRejected(
        bool streaming, string phase)
    {
        using var versions = new FhirVersionContext(NullLoggerFactory.Instance,
            new SearchParameterResolutionOptions(), NullFhirBaseUriProvider.Instance);
        var repository = Substitute.For<IFhirRepository>();
        var stored = new List<string>();
        repository.GetNextTransactionIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(_ =>
            phase == "allocate" ? throw new IOException("allocate response unavailable") : new TransactionId(1));
        repository.BatchWriteAsync(Arg.Any<TransactionId>(),
            Arg.Any<IReadOnlyList<(string resourceType, string resourceId, ResourceJsonNode resource, IReadOnlyList<object> searchIndexes, string httpMethod, int entryIndex)>>(),
            Arg.Any<CancellationToken>()).Returns(call =>
        {
            if (phase == "write")
            {
                throw new IOException("write response unavailable");
            }
            var operations = call.Arg<IReadOnlyList<(string resourceType, string resourceId, ResourceJsonNode resource, IReadOnlyList<object> searchIndexes, string httpMethod, int entryIndex)>>();
            stored.AddRange(operations.Select(operation => operation.resourceId));
            return operations.Select(operation => new ResourceKey(operation.resourceType, operation.resourceId)).ToArray();
        });
        repository.CommitTransactionAsync(Arg.Any<TransactionId>(), Arg.Any<CancellationToken>())
            .Returns(_ => phase == "commit" ? throw new IOException("commit response unavailable") : ValueTask.CompletedTask);

        var failure = await Should.ThrowAsync<Exception>(() =>
            RunActivityAsync(streaming, versions, repository, fileLines: 20).WaitAsync(TimeSpan.FromSeconds(10)));

        failure.ToString().ShouldContain($"{phase}");
        if (phase == "commit")
        {
            failure.ToString().ShouldContain("indeterminate");
            stored.ShouldBe(["p1"]);
        }
        else
        {
            stored.ShouldBeEmpty();
        }
    }
}
