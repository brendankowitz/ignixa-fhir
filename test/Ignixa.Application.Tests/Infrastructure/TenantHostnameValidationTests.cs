using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Models;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Infrastructure;

public class TenantHostnameValidationTests
{
    private static TenantConfiguration T(int id, params string[] hosts) =>
        new() { TenantId = id, DisplayName = $"T{id}", FhirVersion = "4.0", Hostnames = hosts };

    [Fact]
    public void GivenValidUniqueHostnames_WhenValidated_ThenNoProblems()
    {
        var problems = TenantHostnameValidator.Validate([T(1, "fhir1.example.org"), T(2, "fhir2.example.org")]);
        problems.ShouldBeEmpty();
    }

    [Fact]
    public void GivenAHostnameWithSchemeOrPort_WhenValidated_ThenReportsIt()
    {
        var problems = TenantHostnameValidator.Validate([T(1, "https://fhir1.example.org"), T(2, "fhir2.example.org:8080")]);
        problems.Count.ShouldBe(2);
        problems.ShouldAllBe(p => p.Kind == HostnameProblemKind.Format);
    }

    [Fact]
    public void GivenADuplicateHostnameAcrossTenants_WhenValidated_ThenReportsIt()
    {
        var problems = TenantHostnameValidator.Validate([T(1, "shared.example.org"), T(2, "shared.example.org")]);
        problems.ShouldContain(p => p.Kind == HostnameProblemKind.Duplicate && p.Message.Contains("shared.example.org"));
    }

    [Fact]
    public void GivenALabelLongerThan63Chars_WhenValidated_ThenReportsAFormatProblem()
    {
        var label64 = new string('a', 64);
        var problems = TenantHostnameValidator.Validate([T(1, $"{label64}.example.org")]);
        problems.ShouldContain(p => p.Kind == HostnameProblemKind.Format);
    }

    [Fact]
    public void GivenOnlyFormatProblems_WhenPartitioned_ThenNoneAreDuplicateKind()
    {
        var problems = TenantHostnameValidator.Validate([T(1, "https://fhir1.example.org"), T(2, "fhir2.example.org:8080")]);
        problems.ShouldNotContain(p => p.Kind == HostnameProblemKind.Duplicate);
    }

    [Fact]
    public void GivenADuplicate_WhenPartitioned_ThenAtLeastOneDuplicateKind()
    {
        var problems = TenantHostnameValidator.Validate([T(1, "shared.example.org"), T(2, "shared.example.org")]);
        problems.ShouldContain(p => p.Kind == HostnameProblemKind.Duplicate);
    }
}
