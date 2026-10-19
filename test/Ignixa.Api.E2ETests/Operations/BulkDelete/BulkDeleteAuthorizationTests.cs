// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Ignixa.Api.E2ETests.Operations.BulkDelete;

/// <summary>
/// Pipeline-level guard tests for <c>$bulk-delete</c> authorization (finding F2): with
/// <c>Authorization:Enabled=true</c>, drives real HTTP requests through the route-level
/// <c>FhirAuthorizationFilter</c> and <c>BulkDeleteKickoffAuthorizer</c> together, proving the kickoff
/// endpoint actually invokes the authorizer, returns 403 when denied, and creates no job -- the thing
/// <c>BulkDeleteKickoffAuthorizerTests</c> (unit-level, authorizer called directly) cannot show.
/// </summary>
/// <remarks>
/// <see cref="BulkDeleteApiFixture"/> runs with <c>Authorization:Enabled=false</c> (see its
/// <c>ConfigureWebHost</c>), so every test here builds its own derived host via
/// <see cref="WebApplicationFactory{TEntryPoint}.WithWebHostBuilder"/>, the same pattern
/// <c>BulkDeleteCancellationTests</c> uses: enable authorization for that host alone, and register
/// <see cref="BulkDeleteTestAuthenticationHandler"/> as its default authentication scheme (production
/// registers JwtBearer as default whenever authorization is enabled; a derived host can override a
/// registration the base host never made, without touching production code). Resources are seeded
/// through <see cref="BulkDeleteApiFixture.Client"/> -- the shared fixture's own client, which always
/// talks to an authorization-disabled host -- so seeding needs no privileged scope of its own.
/// </remarks>
[Collection(BulkDeleteTestCollection.Name)]
public class BulkDeleteAuthorizationTests(BulkDeleteApiFixture fixture)
{
    /// <summary>
    /// Unconstrained CRUDS on every type: enough to kick off and poll any bulk-delete request this suite
    /// issues, and to read back a resource afterwards, regardless of what the request under test needed.
    /// </summary>
    private const string PrivilegedScope = "system/*.cruds";

    [Fact]
    public async Task GivenAReadOnlyScope_WhenKickingOffATypeLevelBulkDelete_ThenItIsForbiddenAndNoJobIsCreated()
    {
        await using var application = CreateAuthorizedApplication();
        var tag = NewTag();
        await PutPatientAsync(tag);

        using var deniedClient = CreateScopedClient(application, "system/Patient.rs");
        using var kickoff = await new BulkDeleteClient(deniedClient)
            .KickoffAsync($"/tenant/1/Patient/$bulk-delete?_tag={tag}&_hardDelete=true");

        kickoff.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await kickoff.Content.ReadAsStringAsync());
        kickoff.Content.Headers.Contains("Content-Location").ShouldBeFalse();

        using var privilegedClient = CreateScopedClient(application, PrivilegedScope);
        await AssertPatientExistsAsync(privilegedClient, tag);
    }

    [Fact]
    public async Task GivenAnUnconstrainedDeleteScope_WhenKickingOffATypeLevelBulkDelete_ThenItSucceedsAndThePatientIsDeleted()
    {
        await using var application = CreateAuthorizedApplication();
        var tag = NewTag();
        var id = await PutPatientAsync(tag);

        using var kickoffClient = CreateScopedClient(application, "system/Patient.cruds");
        using var kickoff = await new BulkDeleteClient(kickoffClient).KickoffAsync($"/tenant/1/Patient/$bulk-delete?_tag={tag}");
        kickoff.StatusCode.ShouldBe(HttpStatusCode.Accepted, await kickoff.Content.ReadAsStringAsync());
        var location = kickoff.Content.Headers.GetValues("Content-Location").Single();

        // The status route has no resourceType, so FhirAuthorizationFilter classifies it as
        // FhirInteraction.SearchSystem: polling needs search on `*` in addition to the kickoff's delete scope.
        using var pollingClient = CreateScopedClient(application, "system/*.rs system/Patient.cruds");
        var (statusCode, body, _) = await new BulkDeleteClient(pollingClient).PollToCompletionAsync(location);
        statusCode.ShouldBe(HttpStatusCode.OK, body.ToJsonString());
        BulkDeleteClient.GetCounts(body)["Patient"].ShouldBe(1);

        using var privilegedClient = CreateScopedClient(application, PrivilegedScope);
        using var afterDelete = await privilegedClient.GetAsync($"/tenant/1/Patient/{id}");
        afterDelete.StatusCode.ShouldBe(HttpStatusCode.Gone);
    }

    [Fact]
    public async Task GivenDeleteWithoutWildcard_WhenTheKickoffCascadesThroughRevInclude_ThenItIsForbiddenAndNoJobIsCreated()
    {
        await using var application = CreateAuthorizedApplication();
        var tag = NewTag();
        await PutPatientAsync(tag);

        // Delete on Patient plus read/search on everything else still lacks delete on `*`, which a
        // _revinclude cascade requires because it can reach resources of any type.
        using var deniedClient = CreateScopedClient(application, "system/Patient.cruds system/*.rs");
        using var kickoff = await new BulkDeleteClient(deniedClient).KickoffAsync(
            $"/tenant/1/Patient/$bulk-delete?_tag={tag}&_revinclude=Observation:subject");

        kickoff.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await kickoff.Content.ReadAsStringAsync());
        kickoff.Content.Headers.Contains("Content-Location").ShouldBeFalse();

        using var privilegedClient = CreateScopedClient(application, PrivilegedScope);
        await AssertPatientExistsAsync(privilegedClient, tag);
    }

    [Fact]
    public async Task GivenAPatientContextToken_WhenKickingOffATypeLevelBulkDelete_ThenItIsForbiddenEvenWithDeleteScope()
    {
        await using var application = CreateAuthorizedApplication();
        var tag = NewTag();
        await PutPatientAsync(tag);

        using var deniedClient = CreateScopedClient(application, "patient/*.cruds", patient: "p1");
        using var kickoff = await new BulkDeleteClient(deniedClient).KickoffAsync($"/tenant/1/Patient/$bulk-delete?_tag={tag}");

        kickoff.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await kickoff.Content.ReadAsStringAsync());
        kickoff.Content.Headers.Contains("Content-Location").ShouldBeFalse();

        using var privilegedClient = CreateScopedClient(application, PrivilegedScope);
        await AssertPatientExistsAsync(privilegedClient, tag);
    }

    [Fact]
    public async Task GivenSystemLevelWildcardDelete_WhenKickingOffWithATypeFilter_ThenItSucceeds()
    {
        await using var application = CreateAuthorizedApplication();
        var tag = NewTag();
        await PutPatientAsync(tag);

        using var client = CreateScopedClient(application, PrivilegedScope);
        var bulkDelete = new BulkDeleteClient(client);
        using var kickoff = await bulkDelete.KickoffAsync($"/tenant/1/$bulk-delete?_type=Patient&_tag={tag}");
        kickoff.StatusCode.ShouldBe(HttpStatusCode.Accepted, await kickoff.Content.ReadAsStringAsync());
        var location = kickoff.Content.Headers.GetValues("Content-Location").Single();

        var (statusCode, body, _) = await bulkDelete.PollToCompletionAsync(location);
        statusCode.ShouldBe(HttpStatusCode.OK, body.ToJsonString());
        BulkDeleteClient.GetCounts(body)["Patient"].ShouldBe(1);
    }

    [Fact]
    public async Task GivenNoAuthenticationCredentials_WhenKickingOffABulkDelete_ThenItIsForbidden()
    {
        await using var application = CreateAuthorizedApplication();
        using var anonymousClient = application.CreateClient();

        using var kickoff = await new BulkDeleteClient(anonymousClient)
            .KickoffAsync($"/tenant/1/Patient/$bulk-delete?_tag={NewTag()}");

        kickoff.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await kickoff.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Builds a derived host with authorization enabled and
    /// <see cref="BulkDeleteTestAuthenticationHandler"/> registered as its default authentication scheme.
    /// </summary>
    /// <remarks>
    /// <c>Authorization:Enabled</c> is set through <i>both</i> <c>UseSetting</c> and
    /// <c>ConfigureAppConfiguration</c>, confirmed necessary by exercising each alone while writing this
    /// suite:
    /// <list type="bullet">
    /// <item><c>CoreServicesRegistration.ConfigureJwtAuthentication</c> reads <c>Authorization:Enabled</c>
    /// itself, eagerly, while <c>Program</c> is still registering services -- before a
    /// <c>WebApplicationFactory</c>'s <c>ConfigureAppConfiguration</c> sources (added via
    /// <c>WithWebHostBuilder</c>) are merged in. <c>ConfigureAppConfiguration</c> alone leaves that eager
    /// read seeing the base fixture's disabled default, so production code never calls
    /// <c>IServiceCollection.AddAuthorization()</c> -- while <c>MiddlewareRegistration.UseIgnixaMiddleware</c>'s
    /// own, later, post-build read of the same key does see the override and still adds the authorization
    /// middleware, which then throws for a missing service it needs.</item>
    /// <item><c>UseSetting</c> alone fixes that eager read (it is visible from the start, same reasoning as
    /// <see cref="IgnixaApiFixture.ConfigureWebHost"/>'s remark for its GraphQL flag), but it layers in as
    /// host-level configuration with lower precedence than the base fixture's own
    /// <c>ConfigureAppConfiguration</c>-sourced "disabled" override: the <em>final</em>, request-time
    /// configuration -- read both by the middleware-registration check and by every
    /// <c>IOptions&lt;AuthorizationOptions&gt;</c> consumer, including <see cref="BulkDeleteKickoffAuthorizer"/>
    /// and <see cref="FhirAuthorizationFilter"/> -- stays disabled, so nothing in this suite would ever be
    /// denied (confirmed with a throwaway diagnostic test that resolved <c>IOptions&lt;AuthorizationOptions&gt;</c>
    /// directly from the host and found <c>Enabled=false</c> despite the fixed startup).</item>
    /// </list>
    /// Using both covers the early read <c>UseSetting</c> satisfies and the final value
    /// <c>ConfigureAppConfiguration</c> wins. <c>Authentication:Authority</c> is set the same way so
    /// production's JwtBearer registration (still added alongside the test scheme, just never selected as
    /// default) does not throw while building its own options, even though this suite never exercises it.
    /// </remarks>
    private WebApplicationFactory<Program> CreateAuthorizedApplication() =>
        fixture.WithWebHostBuilder(webHost =>
        {
            webHost.UseSetting("Authorization:Enabled", "true");
            webHost.UseSetting("Authentication:Authority", "https://bulk-delete-authz-tests.invalid/");
            webHost.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Authorization:Enabled"] = "true",
                    ["Authentication:Authority"] = "https://bulk-delete-authz-tests.invalid/",
                }));

            webHost.ConfigureServices(services =>
                services.AddAuthentication(BulkDeleteTestAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, BulkDeleteTestAuthenticationHandler>(
                        BulkDeleteTestAuthenticationHandler.SchemeName, _ => { }));
        });

    private static HttpClient CreateScopedClient(WebApplicationFactory<Program> application, string scope, string? patient = null)
    {
        var client = application.CreateClient();
        client.DefaultRequestHeaders.Add(BulkDeleteTestAuthenticationHandler.ScopeHeaderName, scope);
        if (patient is not null)
        {
            client.DefaultRequestHeaders.Add(BulkDeleteTestAuthenticationHandler.PatientHeaderName, patient);
        }

        return client;
    }

    private static string NewTag() => Guid.NewGuid().ToString("N");

    /// <summary>Seeds a tagged Patient through the shared fixture's own (authorization-disabled) client.</summary>
    private async Task<string> PutPatientAsync(string tag)
    {
        var id = Guid.NewGuid().ToString("N");
        using var content = new StringContent(
            new JsonObject
            {
                ["resourceType"] = "Patient",
                ["id"] = id,
                ["meta"] = new JsonObject { ["tag"] = new JsonArray(new JsonObject { ["code"] = tag }) },
            }.ToJsonString(),
            Encoding.UTF8, "application/fhir+json");
        using var response = await fixture.Client.PutAsync($"/tenant/1/Patient/{id}", content);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return id;
    }

    private static async Task AssertPatientExistsAsync(HttpClient privilegedClient, string tag)
    {
        using var search = await privilegedClient.GetAsync($"/tenant/1/Patient?_tag={tag}");
        search.StatusCode.ShouldBe(HttpStatusCode.OK, await search.Content.ReadAsStringAsync());
        var bundle = JsonNode.Parse(await search.Content.ReadAsStringAsync())!;
        (bundle["total"]?.GetValue<int>() ?? bundle["entry"]?.AsArray().Count ?? 0).ShouldBe(1);
    }
}
