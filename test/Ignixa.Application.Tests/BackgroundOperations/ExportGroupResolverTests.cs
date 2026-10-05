using System.Text;
using Ignixa.Abstractions;
using Ignixa.Application.BackgroundOperations.Export;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Specification.Extensions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations;

public class ExportGroupResolverTests
{
    [Theory]
    [InlineData(FhirVersion.Stu3, "\"actual\":true")]
    [InlineData(FhirVersion.R4, "\"actual\":true")]
    [InlineData(FhirVersion.R4B, "\"actual\":true")]
    [InlineData(FhirVersion.R5, "\"membership\":\"enumerated\"")]
    public async Task GivenAnEnumeratedGroup_WhenResolvingAcrossVersions_ThenOnlyActivePatientsAreIncluded(
        FhirVersion version, string membership)
    {
        var resolver = CreateResolver($$$"""
            {"resourceType":"Group","id":"cohort","type":"person",{{{membership}}},
             "member":[{"entity":{"reference":"Patient/active"}},
                       {"entity":{"reference":"Patient/inactive"},"inactive":true}]}
            """);

        var result = await resolver.ResolvePatientIdsAsync(1, "cohort", version.GetSchemaProvider(), CancellationToken.None);

        result.ShouldBe(["active"]);
    }

    [Theory]
    [InlineData(FhirVersion.R4, "\"actual\":false")]
    [InlineData(FhirVersion.R5, "\"membership\":\"definitional\"")]
    public async Task GivenANonEnumeratedGroup_WhenResolving_ThenItIsRejected(FhirVersion version, string membership)
    {
        var resolver = CreateResolver($$"""{"resourceType":"Group","id":"cohort","type":"person",{{membership}}}""");

        await Should.ThrowAsync<BadRequestException>(() =>
            resolver.ResolvePatientIdsAsync(1, "cohort", version.GetSchemaProvider(), CancellationToken.None));
    }

    [Fact]
    public async Task GivenAnExternalMemberWithACollidingLocalId_WhenResolving_ThenItIsNotTreatedAsLocalMembership()
    {
        var resolver = CreateResolver("""
            {"resourceType":"Group","id":"cohort","type":"person","actual":true,
             "member":[{"entity":{"reference":"https://other.example/fhir/Patient/local-id"}}]}
            """);

        await Should.ThrowAsync<BadRequestException>(() =>
            resolver.ResolvePatientIdsAsync(1, "cohort", FhirVersion.R4.GetSchemaProvider(), CancellationToken.None));
    }

    private static ExportGroupResolver CreateResolver(string json)
    {
        var repository = Substitute.For<IFhirRepository>();
        repository.GetAsync(Arg.Any<ResourceKey>(), Arg.Any<CancellationToken>())
            .Returns(new SearchEntryResult("Group", "cohort", "1", DateTimeOffset.UnixEpoch, Encoding.UTF8.GetBytes(json)));
        var factory = Substitute.For<IFhirRepositoryFactory>();
        factory.GetRepositoryAsync(1, Arg.Any<CancellationToken>()).Returns(repository);
        return new ExportGroupResolver(factory, NullFhirBaseUriProvider.Instance);
    }
}
