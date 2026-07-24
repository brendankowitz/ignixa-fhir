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
    }

    [Fact]
    public void GivenADuplicateHostnameAcrossTenants_WhenValidated_ThenReportsIt()
    {
        var problems = TenantHostnameValidator.Validate([T(1, "shared.example.org"), T(2, "shared.example.org")]);
        problems.ShouldContain(p => p.Contains("shared.example.org"));
    }
}
