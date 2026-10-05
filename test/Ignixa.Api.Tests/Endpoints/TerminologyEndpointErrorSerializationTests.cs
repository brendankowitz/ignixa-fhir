using System.Text;
using System.Text.Json.Nodes;
using Ignixa.Api.Endpoints.Experimental;
using Ignixa.Api.Http;
using Ignixa.Application.Features.Experimental.Terminology.Expand;
using Ignixa.Application.Features.Experimental.Terminology.Subsumes;
using Ignixa.Application.Features.Experimental.Terminology.Translate;
using Ignixa.Application.Infrastructure;
using Ignixa.Validation.Abstractions;
using Medino;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Api.Tests.Endpoints;

public sealed class TerminologyEndpointErrorSerializationTests : IAsyncLifetime
{
    private const string MissingValueSet = "http://example.org/ValueSet/missing";
    private readonly ITerminologyService _terminology = Substitute.For<ITerminologyService>();
    private readonly WebApplication _app;

    public TerminologyEndpointErrorSerializationTests()
    {
        _terminology.ExpandValueSetAsync(Arg.Any<ExpansionParameters>(), Arg.Any<CancellationToken>())
            .Returns((ExpandResult?)null);
        var expandHandler = new ExpandValueSetHandler(_terminology, NullLogger<ExpandValueSetHandler>.Instance);
        var mediator = Substitute.For<IMediator>();
        mediator.SendAsync(Arg.Any<ExpandValueSetQuery>(), Arg.Any<CancellationToken>())
            .Returns(call => expandHandler.HandleAsync(call.Arg<ExpandValueSetQuery>(), call.Arg<CancellationToken>()));
        mediator.SendAsync(Arg.Any<TranslateCodeCommand>(), Arg.Any<CancellationToken>())
            .Returns(new TranslateCodeResult(JsonNode.Parse("""
                {"resourceType":"Parameters","parameter":[{"name":"result","valueBoolean":false}]}
                """)!));
        mediator.SendAsync(Arg.Any<SubsumesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new SubsumesQueryResult(JsonNode.Parse("""
                {"resourceType":"Parameters","parameter":[{"name":"outcome","valueCode":"not-subsumed"}]}
                """)!));
        var accessor = Substitute.For<IFhirRequestContextAccessor>();
        accessor.RequestContext.Returns(FhirRequestContextFactory.CreateBackgroundContext(1));
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(mediator);
        builder.Services.AddSingleton(accessor);
        _app = builder.Build();
        _app.MapTerminologyEndpoints();
    }

    [Theory]
    [InlineData("ExpandValueSet")]
    [InlineData("ExpandValueSetTenant")]
    public async Task GivenMissingValueSet_WhenExpanded_ThenNotFoundIsSerializedAsFhirOperationOutcome(string endpointName)
    {
        var (context, body, raw) = await SendAsync(
            endpointName, $"?url={Uri.EscapeDataString(MissingValueSet)}&_pretty=true");

        AssertOutcome(context, body, StatusCodes.Status404NotFound, "not-found", MissingValueSet);
        raw.ShouldContain("\n");
    }

    [Theory]
    [InlineData("ExpandValueSet", null, "'url'")]
    [InlineData("ExpandValueSetTenant", null, "'url'")]
    [InlineData("TranslateCode", """{"code":"","system":"http://example.org/system"}""", "'code'")]
    [InlineData("TranslateCodeTenant", """{"code":"","system":"http://example.org/system"}""", "'code'")]
    [InlineData("SubsumesCodes", """{"codeA":"","codeB":"car","system":"http://example.org/system"}""", "'codeA'")]
    [InlineData("SubsumesTenant", """{"codeA":"","codeB":"car","system":"http://example.org/system"}""", "'codeA'")]
    public async Task GivenMissingRequiredParameter_WhenTerminologyOperationRuns_ThenBadRequestIsFhirOperationOutcome(
        string endpointName, string? requestBody, string expectedParameter)
    {
        var (context, body, raw) = await SendAsync(endpointName, "?_pretty=true", requestBody);

        AssertOutcome(context, body, StatusCodes.Status400BadRequest, "required", expectedParameter);
        raw.ShouldContain("\n");
    }

    [Theory]
    [InlineData("ExpandValueSet", "ValueSet", null)]
    [InlineData("ExpandValueSetTenant", "ValueSet", null)]
    [InlineData("TranslateCode", "Parameters", """{"code":"car","system":"http://example.org/system"}""")]
    [InlineData("TranslateCodeTenant", "Parameters", """{"code":"car","system":"http://example.org/system"}""")]
    [InlineData("SubsumesCodes", "Parameters", """{"codeA":"car","codeB":"house","system":"http://example.org/system"}""")]
    [InlineData("SubsumesTenant", "Parameters", """{"codeA":"car","codeB":"house","system":"http://example.org/system"}""")]
    public async Task GivenSuccessfulOperation_WhenExecuted_ThenExistingResourceResponsesArePreserved(
        string endpointName, string resourceType, string? requestBody)
    {
        _terminology.ExpandValueSetAsync(Arg.Any<ExpansionParameters>(), Arg.Any<CancellationToken>())
            .Returns(new ExpandResult("urn:uuid:expansion", DateTimeOffset.UnixEpoch, 1, 0,
                [new ExpandedConcept("http://example.org/system", "car", "Car")]));

        var (context, body, _) = await SendAsync(endpointName, "?url=http://example.org/ValueSet/known", requestBody);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        body["resourceType"]!.GetValue<string>().ShouldBe(resourceType);
        if (endpointName.StartsWith("Expand", StringComparison.Ordinal))
        {
            body["expansion"]!["contains"]![0]!["code"]!.GetValue<string>().ShouldBe("car");
        }
        else if (endpointName.StartsWith("Translate", StringComparison.Ordinal))
        {
            body["parameter"]![0]!["valueBoolean"]!.GetValue<bool>().ShouldBeFalse();
        }
        else
        {
            body["parameter"]![0]!["valueCode"]!.GetValue<string>().ShouldBe("not-subsumed");
        }
    }

    private static void AssertOutcome(HttpContext context, JsonNode body, int status, string code, string diagnostics)
    {
        context.Response.StatusCode.ShouldBe(status);
        body["resourceType"].ShouldNotBeNull($"Expected a FHIR outcome, received {body.ToJsonString()}");
        body["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
        context.Response.ContentType.ShouldBe(KnownContentTypes.ApplicationFhirJson);
        var issues = body["issue"]!.AsArray();
        issues.Count.ShouldBe(1);
        issues[0]!["severity"]!.GetValue<string>().ShouldBe("error");
        issues[0]!["code"]!.GetValue<string>().ShouldBe(code);
        issues[0]!["diagnostics"]!.GetValue<string>().ShouldContain(diagnostics);
    }

    private async Task<(HttpContext Context, JsonNode Body, string Raw)> SendAsync(
        string endpointName, string query, string? requestBody = null)
    {
        var endpoint = ((IEndpointRouteBuilder)_app).DataSources.SelectMany(source => source.Endpoints)
            .Single(candidate => candidate.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == endpointName);
        await using var scope = _app.Services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.RouteValues["tenantId"] = "1";
        context.Request.Method = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Single();
        context.Request.Path = ((RouteEndpoint)endpoint).RoutePattern.RawText!.Replace("{tenantId:int}", "1", StringComparison.Ordinal);
        context.Request.QueryString = new QueryString(query);
        if (requestBody is not null)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(requestBody);
            context.Request.Body = new MemoryStream(bytes);
            context.Request.ContentType = KnownContentTypes.ApplicationFhirJson;
            context.Request.ContentLength = bytes.Length;
            context.Features.Set<IHttpRequestBodyDetectionFeature>(new RequestBodyDetectionFeature());
        }
        using var response = new MemoryStream();
        context.Response.Body = response;
        await endpoint.RequestDelegate!(context);
        var raw = Encoding.UTF8.GetString(response.ToArray());
        return (context, JsonNode.Parse(raw)!, raw);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _app.DisposeAsync();

    private sealed class RequestBodyDetectionFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
