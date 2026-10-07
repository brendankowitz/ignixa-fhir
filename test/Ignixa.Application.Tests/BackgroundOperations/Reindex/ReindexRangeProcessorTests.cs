using Ignixa.Abstractions;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.BackgroundOperations.Reindex.Workers;
using Ignixa.Application.Features.Search;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Specification.ValueSets.Normative;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public class ReindexRangeProcessorTests
{
    [Fact]
    public async Task GivenDefinitionsBehindTargetEvent_WhenRangeIsProcessed_ThenRetryableFailureIsThrown()
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>())
            .Returns(new TenantConfiguration
            {
                TenantId = 1,
                DisplayName = "Tenant",
                FhirVersion = "4.0"
            });
        var repository = Substitute.For<IFhirRepository, IReindexStore>();
        var repositories = Substitute.For<IFhirRepositoryFactory>();
        repositories.GetRepositoryAsync(1, Arg.Any<CancellationToken>())
            .Returns((IFhirRepository)repository);
        var versions = Substitute.For<IFhirVersionContext>();
        versions.GetDefinitionsHandle(Arg.Any<FhirVersion>(), 1)
            .Returns(new DefinitionsHandle(
                Substitute.For<ISearchIndexer>(),
                Substitute.For<IFhirSchemaProvider>(),
                41));
        var processor = new ReindexRangeProcessor(repositories, tenants, versions);

        await Should.ThrowAsync<ReindexDefinitionsNotReadyException>(() => processor.ProcessAsync(
            new ReindexRangeInput("job", 1, "Patient", 1, 100, 42, 1000, 0),
            CancellationToken.None));
    }
}
