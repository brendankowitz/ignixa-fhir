// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Caching;
using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Terminology.Remote;

/// <summary>
/// NLM RxClass (<c>https://rxnav.nlm.nih.gov/REST</c>), the drug-class source behind <c>--rxclass-server</c>. Requests are
/// GETs under <c>rxclass/</c> whose query carries only RxCUIs, class ids, class types and relation names, never folder
/// content or the caller's text. Responses are time-bounded, size-capped and untrusted: their names are data, urls in them
/// are never followed, and a failure is reported as <c>rxclass-unavailable</c> or <c>rxclass-error</c>, never turned into
/// an answer. Answers are cached by request URL in memory and, when a directory is given, on disk with the time they were
/// retrieved, so repeated runs read the same answer; deleting the directory refreshes them.
/// </summary>
public sealed class RxClassClient : IDisposable
{
    public const string UnavailableCode = "rxclass-unavailable";
    public const string ErrorCode = "rxclass-error";

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    /// <summary>The largest response read; a larger one is an error.</summary>
    public const int MaxResponseBytes = 4 * 1024 * 1024;

    /// <summary>The only query parameter names any request carries.</summary>
    public static readonly IReadOnlySet<string> AllowedParameters = new HashSet<string>(StringComparer.Ordinal)
    {
        "rxcui", "relaSource", "relas", "rela", "classId", "classTypes",
    };

    private readonly HttpClient _client;
    private readonly TimeSpan _timeout;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, RxClassResponse> _answers = new(StringComparer.Ordinal);
    private int _requests;

#pragma warning disable CA2000 // The HttpClient created by the other constructor owns and disposes the handler.
    /// <param name="baseUri">The RxNav REST base, for example https://rxnav.nlm.nih.gov/REST.</param>
    /// <param name="cacheDirectory">Where answers are kept on disk; null to keep them in memory only.</param>
    public RxClassClient(Uri baseUri, string? cacheDirectory)
        : this(baseUri, cacheDirectory, new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.All }, DefaultTimeout, TimeProvider.System)
    {
    }
#pragma warning restore CA2000

    /// <summary>Uses <paramref name="handler"/> for every request and <paramref name="timeProvider"/> to stamp answers (tests pass stubs).</summary>
    internal RxClassClient(Uri baseUri, string? cacheDirectory, HttpMessageHandler handler, TimeSpan timeout, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(timeProvider);

        BaseUri = Validate(baseUri) is { } problem ? throw new ArgumentException(problem, nameof(baseUri)) : WithTrailingSlash(baseUri);
        CacheDirectory = cacheDirectory is null ? null : Path.GetFullPath(cacheDirectory);
        _timeout = timeout;
        _timeProvider = timeProvider;
        _client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public Uri BaseUri { get; }

    /// <summary>The disk cache; null when answers are kept in memory only.</summary>
    public string? CacheDirectory { get; }

    /// <summary>HTTP requests sent so far (cached answers send none).</summary>
    public int RequestsSent => Volatile.Read(ref _requests);

    /// <summary>Why <paramref name="uri"/> cannot be the RxNav base: not absolute https (http only to this machine), or with credentials, a query or a fragment; null when it can.</summary>
    public static string? Validate(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri || !(uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
        {
            return "The RxClass server must be an absolute https URL (http only on this machine), for example https://rxnav.nlm.nih.gov/REST.";
        }

        return uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0
            ? "The RxClass server URL must not carry credentials, a query or a fragment."
            : null;
    }

    /// <summary>GETs <c>&lt;base&gt;/rxclass/&lt;path&gt;?&lt;parameters&gt;</c>, from the cache when it holds the same URL.</summary>
    /// <param name="overlap">Refuses a disk cache inside the data folder.</param>
    /// <exception cref="ToolRequestException">The server could not be reached in time, or did not answer a JSON object.</exception>
    internal async Task<RxClassResponse> GetAsync(string path, IReadOnlyList<(string Name, string Value)> parameters, FolderOverlap overlap, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(overlap);

        foreach ((string name, _) in parameters)
        {
            if (!AllowedParameters.Contains(name))
            {
                throw new ArgumentException($"'{name}' is not a parameter an RxClass request may carry.", nameof(parameters));
            }
        }

        var uri = new Uri(BaseUri, "rxclass/" + path + "?" + string.Join('&', parameters.Select(parameter => $"{Uri.EscapeDataString(parameter.Name)}={Uri.EscapeDataString(parameter.Value)}")));
        string key = uri.AbsoluteUri;
        if (_answers.TryGetValue(key, out RxClassResponse? remembered))
        {
            return remembered with { Body = (JsonObject)remembered.Body.DeepClone(), FromCache = true, CacheProblem = null };
        }

        (RxClassResponse? stored, string? readProblem) = ReadCached(key, overlap);
        if (stored is not null)
        {
            _answers.TryAdd(key, stored);
            return stored with { Body = (JsonObject)stored.Body.DeepClone() };
        }

        JsonObject body = await SendAsync(uri, path, cancellationToken);
        DateTimeOffset retrievedAt = _timeProvider.GetUtcNow();
        string? writeProblem = WriteCached(key, body, retrievedAt, overlap);
        var answer = new RxClassResponse(body, retrievedAt, FromCache: false, readProblem ?? writeProblem);
        _answers.TryAdd(key, answer with { CacheProblem = null });
        return answer with { Body = (JsonObject)body.DeepClone() };
    }

    public void Dispose() => _client.Dispose();

    private string? CachePath(string key) => CacheDirectory is null
        ? null
        : Path.Combine(CacheDirectory, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32] + ".json");

    private (RxClassResponse? Answer, string? Problem) ReadCached(string key, FolderOverlap overlap)
    {
        if (CachePath(key) is not { } file || !File.Exists(file))
        {
            return (null, null);
        }

        if (overlap.Reason(file, "RxClass cache") is { } refused)
        {
            return (null, refused);
        }

        try
        {
            if (JsonNode.Parse(File.ReadAllBytes(file)) is JsonObject entry
                && entry["url"]?.GetValueKind() == JsonValueKind.String && (string)entry["url"]! == key
                && entry["retrievedAt"]?.GetValueKind() == JsonValueKind.String
                && DateTimeOffset.TryParse((string)entry["retrievedAt"]!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset retrievedAt)
                && entry["body"] is JsonObject body)
            {
                return (new RxClassResponse((JsonObject)body.DeepClone(), retrievedAt, FromCache: true, CacheProblem: null), null);
            }

            return (null, $"the cached answer '{file}' is not one this tool wrote; it was fetched again");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return (null, $"the cached answer '{file}' could not be read ({ex.GetType().Name}); it was fetched again");
        }
    }

    private string? WriteCached(string key, JsonObject body, DateTimeOffset retrievedAt, FolderOverlap overlap)
    {
        if (CachePath(key) is not { } file)
        {
            return null;
        }

        string? refused = overlap.Reason(CacheDirectory!, "RxClass cache");
        if (refused is not null)
        {
            return refused;
        }

        var entry = new JsonObject
        {
            ["url"] = key,
            ["retrievedAt"] = retrievedAt.ToString("O", CultureInfo.InvariantCulture),
            ["body"] = body.DeepClone(),
        };
        string temporary = Path.Combine(CacheDirectory!, $".{Path.GetFileName(file)}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(CacheDirectory!);
            if (overlap.Reason(CacheDirectory!, "RxClass cache") is { } after)
            {
                return after;
            }

            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(entry));
            File.Move(temporary, file, overwrite: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"the RxClass cache '{CacheDirectory}' could not be written ({ex.GetType().Name}); the answer was used but not kept";
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private async Task<JsonObject> SendAsync(Uri uri, string path, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        Interlocked.Increment(ref _requests);
        string what = $"rxclass/{path} on {BaseUri}";
        try
        {
            using HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw Error($"{what} answered HTTP {(int)response.StatusCode}{((int)response.StatusCode is >= 300 and < 400 ? "; redirects are not followed" : string.Empty)}");
            }

            byte[] body = await ReadBoundedAsync(response, what, deadline.Token);
            return Parse(body) ?? throw Error($"{what} answered something that is not a JSON object");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw ToolRequestException.Error(UnavailableCode, $"The RxClass server did not answer {what} within {_timeout.TotalSeconds:0} s; nothing was concluded from it.");
        }
        catch (HttpRequestException ex)
        {
            throw ToolRequestException.Error(UnavailableCode, $"The RxClass server could not be reached for {what} ({ex.HttpRequestError}); nothing was concluded from it.");
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, string what, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > MaxResponseBytes)
        {
            throw Error($"{what} answered more than {MaxResponseBytes} bytes");
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[16384];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes)
            {
                throw Error($"{what} answered more than {MaxResponseBytes} bytes");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return buffer.ToArray();
    }

    private static JsonObject? Parse(byte[] body)
    {
        try
        {
            return body.Length == 0 ? null : JsonNode.Parse(body) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ToolRequestException Error(string reason) =>
        ToolRequestException.Error(ErrorCode, $"The RxClass server did not give a usable answer: {reason}. Nothing was concluded from it.");

    private static Uri WithTrailingSlash(Uri uri) => uri.AbsolutePath.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
}
