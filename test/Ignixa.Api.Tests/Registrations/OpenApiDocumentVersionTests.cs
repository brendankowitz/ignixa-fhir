using System.Text.Json;
using Ignixa.Api.Registrations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace Ignixa.Api.Tests.Registrations;

/// <summary>
/// ASP.NET Core 11 / Microsoft.OpenApi 3.x can emit OpenAPI 3.2; the server pins the served document to 3.1.
/// </summary>
public sealed class OpenApiDocumentVersionTests
{
    [Fact]
    public async Task GivenCoreServices_WhenOpenApiDocumentIsServed_ThenSpecVersionIs31()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Authorization:Enabled"] = "false" });
        builder.Services.AddIgnixaCoreServices(builder.Configuration, builder.Environment);

        await using var app = builder.Build();
        ((IEndpointRouteBuilder)app).MapOpenApi();
        await app.StartAsync(CancellationToken.None);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };

            using var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), CancellationToken.None);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken.None));

            document.RootElement.GetProperty("openapi").GetString().ShouldNotBeNull().ShouldStartWith("3.1");
        }
        finally
        {
            await app.StopAsync(CancellationToken.None);
        }
    }
}
