using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;
using Microsoft.Extensions.Logging.Abstractions;

#pragma warning disable CA2000 // SendAsync transfers response ownership to the acquirer.

namespace Ignixa.PackageManagement.Tests.Acquisition;

internal sealed class SyntheticNpmRegistry : HttpMessageHandler
{
    internal const string Artifact = "https://artifacts.test/files/test.tgz";
    internal const string Manifest = """
        {"name":"test.pkg","version":"1.2.3","fhirVersion":"4.0.1","fhirVersions":["4.0.1","3.0.2"],
         "dependencies":{"hl7.fhir.r4.core":"4.0.1","unsupported.pkg":"2.0.0"},"provenance":{"nested":true}}
        """;
    internal byte[] Tarball { get; set; } = CreateTarball();
    internal List<(string Uri, string? Authorization)> Requests { get; } = [];
    internal List<StrictInputStream> Bodies { get; } = [];
    internal bool Disposed { get; private set; }
    internal Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>>? Respond { get; set; }
    internal string Integrity => "sha512-" + Convert.ToBase64String(SHA512.HashData(Tarball));
    internal static NpmPackageIdentity Identity => new("test.pkg", "1.2.3");

    internal static byte[] CreateTarball(string manifest = Manifest, string path = "package/custom-metadata.json") =>
        StrictPackageFixture.Gzip(StrictPackageFixture.Tar(
            ("package/Patient.json", """{"resourceType":"Patient","id":"p"}"""),
            ("package/.index.json", """{"index-version":1}"""),
            (path, """{"custom":"preserved"}"""),
            ("package/package.json", manifest)));

    internal byte[] Metadata(string name = "test.pkg", string version = "1.2.3", string? integrity = null, string artifact = Artifact) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            name,
            version,
            dist = new { tarball = artifact, integrity = integrity ?? Integrity }
        }));

    internal HttpResponseMessage Bytes(byte[] bytes, HttpStatusCode status = HttpStatusCode.OK, long? declaredLength = null)
    {
        var stream = new StrictInputStream(bytes);
        Bodies.Add(stream);
        var response = new HttpResponseMessage(status) { Content = new StreamContent(stream) };
        response.Content.Headers.ContentLength = declaredLength;
        return response;
    }

    internal static NpmPackageSourcePolicy Policy(
        int redirects = 0, PackageExtractionLimits? limits = null, int metadataBytes = 1048576,
        PackageAcquisitionRetryPolicy? retry = null, string source = "source") =>
        new(source, new Uri("https://registry.test/npm/"), [new Uri("https://artifacts.test/files/")],
            limits, metadataBytes, retry, redirects);

    internal NpmPackageAcquirer Acquirer(
        Func<string, Uri, CancellationToken, ValueTask<System.Net.Http.Headers.AuthenticationHeaderValue?>>? authenticate = null,
        TimeProvider? timeProvider = null) =>
        new(new PackageExtractor(NullLogger<PackageExtractor>.Instance), this, authenticate, timeProvider: timeProvider);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get)
        {
            throw new InvalidOperationException("Synthetic registry accepts GET only.");
        }

        Requests.Add((request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString()));
        return Respond?.Invoke(request, Requests.Count, cancellationToken) ??
            Task.FromResult(Bytes(request.RequestUri.Host == "registry.test" ? Metadata() : Tarball));
    }

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }
}
