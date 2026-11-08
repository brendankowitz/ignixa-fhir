// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.Api.Middleware;
using Ignixa.DataLayer.FileSystem.FileSystem;
using Ignixa.Domain.Models;
using Ignixa.Serialization.SourceNodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.Api.Tests.Middleware;

/// <summary>
/// Regression: a FileSystem sidecar holding a literal <c>null</c> surfaced as <see cref="InvalidOperationException"/>,
/// which the middleware answers with 400 and the exception message - the sidecar's path on the server. The malformed-JSON
/// row is a guard (it was already a 500 without the path).
/// </summary>
public sealed class FileSystemSidecarFailureResponseTests : IDisposable
{
    private readonly string _baseDirectory = Directory.CreateTempSubdirectory("ignixa-sidecar-response-").FullName;

    [Theory]
    [InlineData("null")]
    [InlineData("{not json")]
    public async Task GivenAnUnreadableSidecar_WhenTheResourceIsRead_ThenTheResponseIsAServerErrorThatNamesNoFile(string content)
    {
        // Arrange
        using var repository = new FileBasedFhirRepository(_baseDirectory, NullLogger<FileBasedFhirRepository>.Instance);
        var node = ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"p1"}""");
        await repository.CreateOrUpdateAsync(new ResourceWrapper("Patient", "p1", "1", DateTimeOffset.UtcNow, node, new ResourceRequest("PUT", "Patient/p1")));
        string sidecar = Directory.GetFiles(Path.Combine(_baseDirectory, "_internal", "Patient", "p1"), "*.metadata.json").ShouldHaveSingleItem();
        await File.WriteAllTextAsync(sidecar, content);

        var body = new MemoryStream();
        var context = new DefaultHttpContext();
        context.Response.Body = body;
        var middleware = new FhirExceptionMiddleware(
            async _ => await repository.GetAsync(new ResourceKey("Patient", "p1")),
            NullLogger<FhirExceptionMiddleware>.Instance);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.ShouldBe(StatusCodes.Status500InternalServerError);
        string outcome = Encoding.UTF8.GetString(body.ToArray());
        outcome.ShouldNotContain(_baseDirectory);
        outcome.ShouldNotContain(Path.GetFileName(sidecar));
        using JsonDocument document = JsonDocument.Parse(body.ToArray());
        document.RootElement.GetProperty("issue")[0].GetProperty("code").GetString().ShouldBe("exception");
    }

    public void Dispose() => Directory.Delete(_baseDirectory, recursive: true);
}
