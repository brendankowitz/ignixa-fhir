// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Terminology.Npm;

/// <summary>
/// Downloads FHIR npm packages from the FHIR package registry, <c>https://packages.fhir.org/</c>, and from nowhere else:
/// the package document from <c>/&lt;id&gt;</c> (versions, <c>dist-tags.latest</c>, each version's SHA-1 <c>shasum</c>)
/// and the archive from <c>/&lt;id&gt;/&lt;version&gt;</c>, which this registry serves directly. Redirects are not
/// followed (the documents' <c>dist.tarball</c> urls point at another host and are not used), responses are size-capped
/// and time-bounded, and the archive must match the published SHA-1. That digest comes from the same server, so it guards
/// against corruption; authenticity rests on TLS to packages.fhir.org.
/// </summary>
/// <remarks>
/// <c>Ignixa.PackageManagement</c>'s <c>NpmPackageAcquirer</c> cannot be used against this registry: <c>/&lt;id&gt;/&lt;version&gt;</c>
/// returns the archive rather than version metadata, versions carry no SHA-512 <c>integrity</c>, and its strict extractor
/// rejects the core packages, which ship entries outside <c>package/</c>.
/// </remarks>
public sealed class FhirPackageRegistry : IDisposable
{
    public const string NotFoundCode = "package-not-found";
    public const string DownloadFailedCode = "package-download-failed";

    public static readonly Uri BaseUri = new("https://packages.fhir.org/");

    public static readonly TimeSpan DocumentTimeout = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan ArchiveTimeout = TimeSpan.FromMinutes(5);

    private const int MaxDocumentBytes = 8 * 1024 * 1024;

    private readonly HttpClient _client;

#pragma warning disable CA2000 // The HttpClient created by the other constructor owns and disposes the handler.
    public FhirPackageRegistry()
        : this(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.None })
    {
    }
#pragma warning restore CA2000

    /// <summary>Uses <paramref name="handler"/> for every request (tests pass a stub; nothing else should).</summary>
    internal FhirPackageRegistry(HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<RegistryPackage> GetPackageAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        byte[] document = await GetAsync(new Uri(BaseUri, Uri.EscapeDataString(id)), "application/json", MaxDocumentBytes, DocumentTimeout, $"the package document of {id}", cancellationToken);
        try
        {
            using JsonDocument parsed = JsonDocument.Parse(document);
            JsonElement root = parsed.RootElement;
            string? latest = TerminologyJson.Object(root, "dist-tags") is { } tags ? TerminologyJson.NonEmpty(tags, "latest") : null;
            var shasums = new Dictionary<string, string>(StringComparer.Ordinal);
            if (TerminologyJson.Object(root, "versions") is { } versions)
            {
                foreach (JsonProperty version in versions.EnumerateObject())
                {
                    if (TerminologyJson.Object(version.Value, "dist") is { } dist && TerminologyJson.NonEmpty(dist, "shasum") is { } shasum)
                    {
                        shasums[version.Name] = shasum;
                    }
                }
            }

            return new RegistryPackage(id, latest, shasums);
        }
        catch (JsonException)
        {
            throw Failed($"the package document of {id} from {BaseUri} is not JSON");
        }
    }

    /// <summary>Downloads the archive of <paramref name="id"/>@<paramref name="version"/> and checks it against <paramref name="shasum"/>.</summary>
    public async Task<byte[]> DownloadAsync(string id, string version, string shasum, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(shasum);

        byte[] archive = await GetAsync(ArchiveUri(id, version), "application/octet-stream", PackageLimits.MaxCompressedBytes, ArchiveTimeout, $"the archive of {id}@{version}", cancellationToken);
#pragma warning disable CA5350 // SHA-1 is the only digest this registry publishes; it is checked for corruption, not trusted for authenticity.
        string actual = Convert.ToHexStringLower(SHA1.HashData(archive));
#pragma warning restore CA5350
        if (!string.Equals(actual, shasum, StringComparison.OrdinalIgnoreCase))
        {
            throw Failed($"the archive of {id}@{version} does not match the SHA-1 the registry publishes for it");
        }

        return archive;
    }

    public static Uri ArchiveUri(string id, string version) => new(BaseUri, $"{Uri.EscapeDataString(id)}/{Uri.EscapeDataString(version)}");

    public void Dispose() => _client.Dispose();

    private async Task<byte[]> GetAsync(Uri uri, string accept, int maxBytes, TimeSpan timeout, string what, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        try
        {
            using HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw ToolRequestException.Error(NotFoundCode, $"The FHIR package registry {BaseUri} has no {what} (HTTP 404).");
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw Failed($"{BaseUri} answered HTTP {(int)response.StatusCode} for {what}{((int)response.StatusCode is >= 300 and < 400 ? "; redirects are not followed" : string.Empty)}");
            }

            if (response.Content.Headers.ContentLength > maxBytes)
            {
                throw Failed($"{what} is larger than {maxBytes} bytes");
            }

            await using Stream body = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[81920];
            int read;
            while ((read = await body.ReadAsync(chunk, deadline.Token)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                {
                    throw Failed($"{what} is larger than {maxBytes} bytes");
                }

                await buffer.WriteAsync(chunk.AsMemory(0, read), deadline.Token);
            }

            return buffer.ToArray();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Failed($"{what} did not arrive within {timeout.TotalSeconds:0} s");
        }
        catch (HttpRequestException ex)
        {
            throw Failed($"{what} could not be fetched ({ex.HttpRequestError})");
        }
    }

    private static ToolRequestException Failed(string reason) =>
        ToolRequestException.Error(DownloadFailedCode, $"Downloading from the FHIR package registry failed: {reason}.");
}
