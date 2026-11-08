using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Ignixa.Api.E2ETests.Operations.BulkDelete;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Ignixa.Api.E2ETests.Security;

/// <summary>
/// Drives real requests through an authorization-enabled host to prove that grants the server cannot enforce are
/// refused: compartment-limited scopes, tokens with no resource scope or role, operations a scope does not cover,
/// and routes that previously had no authorization at all.
/// </summary>
/// <remarks>Uses the same derived-host pattern as <see cref="BulkDeleteAuthorizationTests"/>.</remarks>
[Collection(E2ETestCollection.Name)]
public class SmartAuthorizationFailClosedTests(IgnixaApiFixture fixture)
{
    [Fact]
    public async Task GivenAPatientScopedToken_WhenReadingOrSearchingAnotherPatient_ThenItIsForbidden()
    {
        await using var application = CreateAuthorizedApplication();
        var otherPatient = await PutPatientAsync();
        using var client = CreateScopedClient(application, "patient/*.rs", patient: Guid.NewGuid().ToString("N"));

        using var read = await client.GetAsync($"/tenant/1/Patient/{otherPatient}");
        using var search = await client.GetAsync("/tenant/1/Patient");

        read.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await read.Content.ReadAsStringAsync());
        search.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await search.Content.ReadAsStringAsync());
    }

    // Each bundle entry runs through its endpoint's authorization with the caller's principal.
    [Fact]
    public async Task GivenAReadOnlyWildcardScope_WhenPostingATransactionThatWrites_ThenTheEntryIsRefusedAndNothingIsWritten()
    {
        await using var application = CreateAuthorizedApplication();
        var id = Guid.NewGuid().ToString("N");
        using var client = CreateScopedClient(application, "system/*.rs");

        using var response = await PostTransactionAsync(client, id);

        response.IsSuccessStatusCode.ShouldBeFalse(await response.Content.ReadAsStringAsync());
        using var readBack = await fixture.Client.GetAsync($"/tenant/1/Patient/{id}");
        readBack.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("GET", "/tenant/1/Patient")]
    [InlineData("GET", "/tenant/1/admin/packages")]
    public async Task GivenATokenWithNoResourceScopeOrRole_WhenCallingAProtectedRoute_ThenItIsForbidden(string method, string path)
    {
        await using var application = CreateAuthorizedApplication();
        using var client = CreateScopedClient(application, "openid profile");

        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GivenEveryPermissionOnEveryType_WhenPostingATransaction_ThenItSucceeds()
    {
        await using var application = CreateAuthorizedApplication();
        using var client = CreateScopedClient(application, "system/*.cruds");

        using var response = await PostTransactionAsync(client, Guid.NewGuid().ToString("N"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("system/Patient.rs", HttpStatusCode.Forbidden)]
    [InlineData("system/*.rs", HttpStatusCode.OK)]
    public async Task GivenAScope_WhenCallingPatientEverything_ThenOnlyAWildcardSearchGrantIsAccepted(
        string scope,
        HttpStatusCode expected)
    {
        await using var application = CreateAuthorizedApplication();
        var id = await PutPatientAsync();
        using var client = CreateScopedClient(application, scope);

        using var response = await client.GetAsync($"/tenant/1/Patient/{id}/$everything");

        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/tenant/1/$graphql")]
    [InlineData("/$graphql")]
    public async Task GivenASearchOnlyWildcardScope_WhenPostingAGraphQlMutation_ThenItIsForbiddenAndNothingIsDeleted(string path)
    {
        await using var application = CreateAuthorizedApplication();
        var id = await PutPatientAsync();
        using var client = CreateScopedClient(application, "system/*.s");

        using var response = await PostGraphQlAsync(client, path, $$"""mutation { PatientDelete(id: "{{id}}") }""");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        using var readBack = await fixture.Client.GetAsync($"/tenant/1/Patient/{id}");
        readBack.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // The IDE's native executor would run mutations outside the endpoint filters, so it is not mapped; the path
    // then falls through to the generic create route, which refuses it.
    [Fact]
    public async Task GivenTheGraphQlIdeIsEnabled_WhenAuthorizationIsEnabled_ThenTheMutationDoesNotRunThere()
    {
        await using var application = CreateAuthorizedApplication(
            ("Experimental:Features:GraphQl:EnableGraphQlIde", "true"));
        var id = await PutPatientAsync();
        using var client = CreateScopedClient(application, "system/*.rs");

        using var response = await PostGraphQlAsync(client, "/graphql/", $$"""mutation { PatientDelete(id: "{{id}}") }""");

        response.IsSuccessStatusCode.ShouldBeFalse(await response.Content.ReadAsStringAsync());
        using var readBack = await fixture.Client.GetAsync($"/tenant/1/Patient/{id}");
        readBack.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GivenASearchOnlyWildcardScope_WhenPostingAGraphQlQuery_ThenItSucceeds()
    {
        await using var application = CreateAuthorizedApplication();
        var id = await PutPatientAsync();
        using var client = CreateScopedClient(application, "system/*.s");

        using var response = await PostGraphQlAsync(client, "/tenant/1/$graphql", $$"""{ Patient(id: "{{id}}") { id } }""");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("GET", "/tenant/1/_history")]
    [InlineData("GET", "/_history")]
    [InlineData("POST", "/tenant/1/$export")]
    [InlineData("POST", "/$de-identify")]
    [InlineData("GET", "/tenant/1/admin/packages")]
    public async Task GivenNoCredentials_WhenCallingAPreviouslyUnauthorizedRoute_ThenItIsForbidden(string method, string path)
    {
        await using var application = CreateAuthorizedApplication();
        using var client = application.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/fhir+json");
        }

        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    private static Task<HttpResponseMessage> PostTransactionAsync(HttpClient client, string id)
    {
        var bundle = new JsonObject
        {
            ["resourceType"] = "Bundle",
            ["type"] = "transaction",
            ["entry"] = new JsonArray(new JsonObject
            {
                ["resource"] = new JsonObject { ["resourceType"] = "Patient", ["id"] = id },
                ["request"] = new JsonObject { ["method"] = "PUT", ["url"] = $"Patient/{id}" },
            }),
        };
        return client.PostAsync(
            "/tenant/1",
            new StringContent(bundle.ToJsonString(), Encoding.UTF8, "application/fhir+json"));
    }

    private static Task<HttpResponseMessage> PostGraphQlAsync(HttpClient client, string path, string query) =>
        client.PostAsync(
            path,
            new StringContent(new JsonObject { ["query"] = query }.ToJsonString(), Encoding.UTF8, "application/json"));

    private async Task<string> PutPatientAsync()
    {
        var id = Guid.NewGuid().ToString("N");
        using var content = new StringContent(
            new JsonObject { ["resourceType"] = "Patient", ["id"] = id }.ToJsonString(),
            Encoding.UTF8,
            "application/fhir+json");
        using var response = await fixture.Client.PutAsync($"/tenant/1/Patient/{id}", content);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return id;
    }

    // Authorization:Enabled is set through both UseSetting and ConfigureAppConfiguration; see
    // BulkDeleteAuthorizationTests.CreateAuthorizedApplication for why each alone is insufficient.
    private WebApplicationFactory<Program> CreateAuthorizedApplication(params (string Key, string Value)[] extraSettings) =>
        fixture.WithWebHostBuilder(webHost =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["Authorization:Enabled"] = "true",
                ["Authentication:Authority"] = "https://smart-fail-closed-tests.invalid/",
            };
            foreach (var (key, value) in extraSettings)
            {
                settings[key] = value;
            }

            foreach (var (key, value) in settings)
            {
                webHost.UseSetting(key, value);
            }

            webHost.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
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
}
