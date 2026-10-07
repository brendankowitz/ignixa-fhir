using Ignixa.Domain.Abstractions;
using Ignixa.DataLayer.FileSystem.FileSystem;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Conformance;

public class FhirRepositoryDefinitionsEventIdContractTests
{
    [Fact]
    public void GivenTheRepositoryWriteContract_WhenInspectingTransactionAllocation_ThenDefinitionsEventIdIsRequired()
    {
        var overloads = typeof(IFhirRepository).GetMethods()
            .Where(method => method.Name == nameof(IFhirRepository.GetNextTransactionIdAsync))
            .ToArray();

        overloads.Length.ShouldBe(1);
        overloads[0].GetParameters()[0].ParameterType.ShouldBe(typeof(long));
    }

    [Fact]
    public void GivenTheRepositoryWriteContract_WhenInspectingDelete_ThenDefinitionsEventIdIsRequired()
    {
        var overloads = typeof(IFhirRepository).GetMethods()
            .Where(method => method.Name == nameof(IFhirRepository.DeleteAsync))
            .ToArray();

        overloads.Length.ShouldBe(1);
        overloads[0].GetParameters()[2].ParameterType.ShouldBe(typeof(long));
    }

    [Fact]
    public void GivenTheFileRepository_WhenInspectingWriteOverloads_ThenNoZeroStampCompatibilityOverloadExists()
    {
        typeof(FileBasedFhirRepository).GetMethods()
            .Count(method => method.Name == nameof(IFhirRepository.GetNextTransactionIdAsync))
            .ShouldBe(1);
        typeof(FileBasedFhirRepository).GetMethods()
            .Count(method => method.Name == nameof(IFhirRepository.DeleteAsync))
            .ShouldBe(1);
    }
}
