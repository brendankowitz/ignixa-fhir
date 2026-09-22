using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Shouldly;

namespace Ignixa.Api.E2ETests;

[Collection(E2ETestCollection.Name)]
public class R4HttpInteractionContractTests(IgnixaApiFixture fixture)
{
    private static readonly string[] RoutePrefixes = ["", "tenant/1/"];

    [Theory]
    [InlineData("6ba7b810-9dad-11d1-80b4-00c04fd430c8")]
    [InlineData("release-A")]
    [InlineData("1.2")]
    [InlineData("01")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("2147483648")]
    public async Task GivenALegalUnavailableVersionId_WhenVersionRead_ThenReturnsNotFoundInsteadOfLatest(string version)
    {
        var id = Guid.NewGuid().ToString();
        using var created = await PutAsync(id, false);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        foreach (var prefix in RoutePrefixes)
        {
            using var get = await fixture.Client.GetAsync($"/{prefix}Patient/{id}/_history/{version}");
            get.StatusCode.ShouldBe(HttpStatusCode.NotFound, await get.Content.ReadAsStringAsync());
            using var head = await fixture.Client.SendAsync(new HttpRequestMessage(
                HttpMethod.Head, $"/{prefix}Patient/{id}/_history/{version}"));
            head.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        using var latest = await fixture.Client.GetAsync($"/Patient/{id}");
        latest.Headers.ETag!.ToString().ShouldBe("W/\"1\"");
    }

    [Theory]
    [InlineData("invalid_version")]
    [InlineData("invalid version")]
    [InlineData("version:1")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task GivenAMalformedVersionId_WhenVersionRead_ThenReturnsClientError(string version)
    {
        var id = Guid.NewGuid().ToString();
        using var created = await PutAsync(id, false);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var response = await fixture.Client.GetAsync(
            $"/Patient/{id}/_history/{Uri.EscapeDataString(version)}");
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["resourceType"]!
            .GetValue<string>().ShouldBe("OperationOutcome");
    }

    [Fact]
    public async Task GivenTheVersionIdLengthBoundary_WhenVersionRead_ThenOnlyOverlongIdsAreMalformed()
    {
        var id = Guid.NewGuid().ToString();
        using var created = await PutAsync(id, false);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var maximum = new string('a', 64);
        using var legal = await fixture.Client.GetAsync($"/Patient/{id}/_history/{maximum}");
        legal.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var tooLong = await fixture.Client.GetAsync($"/Patient/{id}/_history/{maximum}a");
        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GivenAnOpaqueVersionInAReadOnlyTransaction_WhenUnavailable_ThenReturnsNotFoundOutcome()
    {
        var id = Guid.NewGuid().ToString();
        using var created = await PutAsync(id, false);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var bundle = new JsonObject
        {
            ["resourceType"] = "Bundle", ["type"] = "transaction",
            ["entry"] = new JsonArray(new JsonObject
            {
                ["request"] = new JsonObject
                {
                    ["method"] = "GET",
                    ["url"] = $"Patient/{id}/_history/{Guid.NewGuid()}"
                }
            })
        };
        using var response = await fixture.Client.PostAsync("/", Content(bundle.ToJsonString()));
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["resourceType"]!
            .GetValue<string>().ShouldBe("OperationOutcome");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenConditionalDeleteWithNoMatches_WhenExecuted_ThenSucceedsAsANoOp(bool countMode)
    {
        var query = $"_id={Guid.NewGuid()}";
        if (countMode)
        {
            query += "&_count=2";
        }
        using var response = await fixture.Client.DeleteAsync($"/Patient?{query}");
        response.StatusCode.ShouldBe(countMode ? HttpStatusCode.OK : HttpStatusCode.NoContent,
            await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadAsStringAsync();
        if (countMode)
        {
            var outcome = JsonNode.Parse(body)!;
            outcome["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
            outcome["issue"]![0]!["diagnostics"]!.GetValue<string>().ShouldContain("Deleted 0");
        }
        else
        {
            body.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task GivenConditionalPatchWithNoMatches_WhenExecuted_ThenStillReturnsNotFound()
    {
        using var response = await fixture.Client.SendAsync(new HttpRequestMessage(
            HttpMethod.Patch, $"/Patient?_id={Guid.NewGuid()}")
        {
            Content = Content("""{"resourceType":"Parameters","parameter":[{"name":"operation","part":[{"name":"type","valueCode":"replace"},{"name":"path","valueString":"Patient.active"},{"name":"value","valueBoolean":true}]}]}""")
        });
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("", true)]
    [InlineData("tenant/1/", false)]
    [InlineData("tenant/1/", true)]
    public async Task GivenPutAsCreate_WhenSuccessful_ThenLocationIdentifiesTheCreatedVersion(string prefix, bool minimal)
    {
        var id = Guid.NewGuid().ToString();
        using var created = await PutAsync(id, false, prefix, minimal);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var location = created.Headers.Location;
        location.ShouldNotBeNull();
        var absolute = location!.IsAbsoluteUri ? location : new Uri(fixture.Client.BaseAddress!, location);
        absolute.AbsolutePath.ShouldEndWith($"/Patient/{id}/_history/1");
        created.Headers.ETag!.ToString().ShouldBe("W/\"1\"");
        if (minimal)
        {
            (await created.Content.ReadAsStringAsync()).ShouldBeEmpty();
        }

        using var updated = await PutAsync(id, true, prefix);
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var original = await fixture.Client.GetAsync(absolute);
        original.StatusCode.ShouldBe(HttpStatusCode.OK);
        var resource = JsonNode.Parse(await original.Content.ReadAsStringAsync())!;
        resource["meta"]!["versionId"]!.GetValue<string>().ShouldBe("1");
        resource["active"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public async Task GivenConditionalPutAsCreate_WhenSuccessful_ThenLocationIsVersioned()
    {
        var identifier = Guid.NewGuid().ToString();
        using var response = await fixture.Client.PutAsync(
            $"/Patient?identifier=http://example.org/contract|{identifier}",
            Content($$$"""{"resourceType":"Patient","identifier":[{"system":"http://example.org/contract","value":"{{{identifier}}}"}]}"""));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        response.Headers.Location.ShouldNotBeNull();
        response.Headers.Location!.ToString().ShouldEndWith("/_history/1");
    }

    private Task<HttpResponseMessage> PutAsync(string id, bool active, string prefix = "", bool minimal = false)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/{prefix}Patient/{id}")
        {
            Content = Content(new JsonObject { ["resourceType"] = "Patient", ["id"] = id, ["active"] = active }.ToJsonString())
        };
        if (minimal)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
        }
        return fixture.Client.SendAsync(request);
    }

    private static StringContent Content(string json) => new(json, Encoding.UTF8, "application/fhir+json");
}
