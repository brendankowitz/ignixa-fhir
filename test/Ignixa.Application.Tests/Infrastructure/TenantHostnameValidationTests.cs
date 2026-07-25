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

    [Fact]
    public void GivenTheSameMalformedHostnameOnTwoTenants_WhenValidated_ThenReportsFormatNotDuplicate()
    {
        // Two Format problems, zero Duplicate: the malformed host never reaches the "seen" dictionary
        // (Validate continues before recording it), so it can never collide there. This is acceptable
        // because AppSettingsTenantConfigurationStore.BuildHostIndex independently excludes any hostname
        // that fails IsValidHostname from the routing index, so neither tenant's malformed host ever routes
        // -- there is no runtime cross-tenant confusion to catch here.
        var problems = TenantHostnameValidator.Validate([T(1, "fhir1.example.org:8080"), T(2, "fhir1.example.org:8080")]);

        problems.Count.ShouldBe(2);
        problems.ShouldAllBe(p => p.Kind == HostnameProblemKind.Format);
    }

    [Theory]
    [InlineData("fhir1.example.org", true)]
    [InlineData("https://fhir1.example.org", false)]
    [InlineData("fhir1.example.org:8080", false)]
    [InlineData("FHIR1.EXAMPLE.ORG", false)]
    public void GivenAHostname_WhenCheckingIsValidHostname_ThenMatchesTheDnsShapeRule(string host, bool expected)
    {
        TenantHostnameValidator.IsValidHostname(host).ShouldBe(expected);
    }
}
