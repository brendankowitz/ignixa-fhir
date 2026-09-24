// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Ignixa.Api.E2ETests.Operations.Terminology;

/// <summary>
/// Proves the system partition reaches its database through an <em>inherited</em> connection string,
/// over HTTP, on the real composition root.
/// <para>
/// This exists because of a specific escape. <c>SqlExecutionService</c> read
/// <c>tenant.Storage.ConnectionString</c> raw, applying neither the system partition's inheritance rule
/// nor the legacy "SqlEntityFramework" storage alias, so every terminology operation failed on the
/// shipped configuration -- and the suite stayed green. It stayed green because the fixtures configured
/// around it: <see cref="IgnixaApiFixture"/> handed tenant 0 its own connection string, and no E2E test
/// called a terminology operation at all. Both are fixed; these tests are what keeps them fixed.
/// </para>
/// <para>
/// The operations chosen are the ones with no in-memory fallback -- $subsumes and $translate route
/// unconditionally to <c>SqlServerTerminologyService</c>, which addresses
/// <see cref="SystemConstants.SystemPartitionId"/>. Answering either at all requires a connection that
/// opened. $expand is deliberately not used: its endpoint maps every <c>InvalidOperationException</c> to
/// 404, so an unresolvable connection string and a ValueSet that is merely not imported produce the same
/// response, and it could not tell the two apart.
/// </para>
/// <para>
/// A second property of the fixture change, worth knowing before anyone tries to "simplify" it: with the
/// inheritance rule removed from <c>SqlExecutionService</c>, the host does not start. The failure runs
/// from <c>Program.InitializeDatabasesAsync</c> through <c>CompositeRepositoryFactory</c>,
/// <c>SqlServerTenantServiceFactory</c>, <c>SqlServerTenantInitializer</c> and the search-index reference
/// data cache to <c>OpenConnectionAsync</c>, and it takes the whole E2E suite with it. That converts a
/// regression that used to be an invisible runtime failure on deployed configurations into a startup
/// failure in CI, which is a strictly better failure mode.
/// </para>
/// </summary>
public class SystemPartitionConnectionInheritanceTests : CapabilityDrivenTestBase
{
    // Tenant-explicit routes: the fixture runs in Isolated mode, where TenantResolutionMiddleware
    // rejects the tenant-agnostic forms.
    private const string SubsumesRoute = "/tenant/1/CodeSystem/$subsumes";
    private const string TranslateRoute = "/tenant/1/ConceptMap/$translate";

    private readonly IgnixaApiFixture _fixture;

    public SystemPartitionConnectionInheritanceTests(IgnixaApiFixture fixture) : base(fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// The precondition the other two depend on, and the single assertion that closes the blind spot.
    /// <para>
    /// This is not a hypothesis. It was measured: with the inheritance rule removed from
    /// <c>SqlExecutionService</c> AND this fixture reverted to also setting
    /// <c>Tenants:Configurations:0:Storage:ConnectionString</c>, the two operation tests below both
    /// PASSED and only this one failed. That is the condition that let six Criticals through a fully
    /// green CI, reproduced on demand. Nothing else in the suite distinguishes "the system partition
    /// inherits its connection string" from "the fixture handed it one", so this assertion is the only
    /// thing standing between that configuration and a silent regression.
    /// </para>
    /// <para>
    /// So: do not simplify the fixture by giving tenant 0 its own connection string again. It would make
    /// the suite pass faster and see less.
    /// </para>
    /// </summary>
    [Fact]
    public async Task GivenSqlServerMode_WhenInspectingTheSystemPartition_ThenItHasNoConnectionStringOfItsOwn()
    {
        var store = _fixture.Services.GetRequiredService<ITenantConfigurationStore>();

        var systemPartition = await store.GetTenantConfigurationAsync(SystemConstants.SystemPartitionId);
        systemPartition.ShouldNotBeNull();

        if (systemPartition.Storage.Type == "FileSystem")
        {
            // TEST_USE_FILESYSTEM run: no connection string exists to inherit, nothing to assert.
            return;
        }

        systemPartition.Storage.Type.ShouldBeOneOf("SqlServer", "SqlEntityFramework");

        systemPartition.Storage.ConnectionString.ShouldBeNullOrWhiteSpace(
            "The system partition must reach its database through inheritance for these tests to mean anything. " +
            "Giving tenant 0 its own connection string in IgnixaApiFixture is exactly the blind spot that let " +
            "SqlExecutionService ship without the inheritance rule.");

        var inheritFrom = systemPartition.Storage.InheritConnectionStringFromTenant;
        var source = await store.GetTenantConfigurationAsync(inheritFrom);
        source.ShouldNotBeNull($"Tenant {inheritFrom} is the inheritance source and must exist.");
        source.Storage.ConnectionString.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// $subsumes has no fallback: answering it means querying the system partition's CodeSystem and
    /// Concept tables. Nothing is imported in the E2E database, so "not-subsumed" is the correct answer --
    /// and one only a connection that actually opened can produce.
    /// </summary>
    [Fact]
    public async Task GivenTheSystemPartitionInheritsItsConnectionString_WhenSubsumesRunsOverHttp_ThenPartitionZeroIsQueried()
    {
        var response = await PostFhirJsonAsync(
            SubsumesRoute,
            """{"codeA":"female","codeB":"male","system":"http://hl7.org/fhir/administrative-gender"}""");

        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"$subsumes did not reach the system partition. Body: {body}");

        var parameters = JsonNode.Parse(body).ShouldNotBeNull();
        parameters["resourceType"]?.GetValue<string>().ShouldBe("Parameters");
    }

    /// <summary>
    /// $translate is the second unconditional SQL path, and it reaches the system partition through a
    /// different query than $subsumes does. With no ConceptMap imported the answer is "no match", which
    /// again is only reachable once the inherited connection string has opened a connection.
    /// </summary>
    [Fact]
    public async Task GivenTheSystemPartitionInheritsItsConnectionString_WhenTranslateRunsOverHttp_ThenPartitionZeroIsQueried()
    {
        var response = await PostFhirJsonAsync(
            TranslateRoute,
            """{"code":"female","system":"http://hl7.org/fhir/administrative-gender"}""");

        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"$translate did not reach the system partition. Body: {body}");

        var parameters = JsonNode.Parse(body).ShouldNotBeNull();
        parameters["resourceType"]?.GetValue<string>().ShouldBe("Parameters");
    }

    // The terminology endpoints declare Accepts<T>("application/fhir+json"); posting application/json
    // gets a 415 before any handler runs.
    private Task<HttpResponseMessage> PostFhirJsonAsync(string route, string json)
        => Client.PostAsync(route, new StringContent(json, Encoding.UTF8, "application/fhir+json"));
}
