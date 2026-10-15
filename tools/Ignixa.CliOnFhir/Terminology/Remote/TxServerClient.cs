// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Terminology.Remote;

/// <summary>
/// A remote FHIR terminology server (for example <c>https://tx.fhir.org/r4</c>), asked only when the local store cannot
/// answer. Requests are GETs of the standard operations whose query carries only codes, systems, versions, displays,
/// canonical urls, filter text and paging, never resource content. Responses are time-bounded, size-capped and untrusted:
/// their text is data, redirects and urls in them are never followed, and a failure (timeout, HTTP error,
/// OperationOutcome, unexpected shape) is reported as <c>tx-server-unavailable</c> or <c>tx-server-error</c>, never turned
/// into an answer. Successful answers are cached in memory for the session.
/// </summary>
public sealed class TxServerClient : IDisposable
{
    public const string UnavailableCode = "tx-server-unavailable";
    public const string ErrorCode = "tx-server-error";

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /// <summary>The largest response read; a larger one is an error.</summary>
    public const int MaxResponseBytes = 2 * 1024 * 1024;

    /// <summary>The only query parameter names any request carries.</summary>
    public static readonly IReadOnlySet<string> AllowedParameters = new HashSet<string>(StringComparer.Ordinal)
    {
        "system", "code", "version", "display", "url", "filter", "count", "offset", "codeA", "codeB", "targetsystem", "targetSystem", "sourceCode",
    };

    private readonly HttpClient _client;
    private readonly TimeSpan _timeout;
    private readonly ConcurrentDictionary<string, JsonObject> _answers = new(StringComparer.Ordinal);
    private readonly Lazy<Task<string?>> _fhirVersion;
    private int _requests;

#pragma warning disable CA2000 // The HttpClient created by the other constructor owns and disposes the handler.
    public TxServerClient(Uri baseUri)
        : this(baseUri, new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.All }, DefaultTimeout)
    {
    }
#pragma warning restore CA2000

    /// <summary>Uses <paramref name="handler"/> for every request (tests pass a stub).</summary>
    internal TxServerClient(Uri baseUri, HttpMessageHandler handler, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentNullException.ThrowIfNull(handler);

        BaseUri = Validate(baseUri) is { } problem ? throw new ArgumentException(problem, nameof(baseUri)) : WithTrailingSlash(baseUri);
        _timeout = timeout;
        _client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        _fhirVersion = new Lazy<Task<string?>>(ReadFhirVersionAsync, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public Uri BaseUri { get; }

    /// <summary>HTTP requests sent so far (cached answers send none).</summary>
    public int RequestsSent => Volatile.Read(ref _requests);

    /// <summary>Why <paramref name="uri"/> cannot be a terminology server base: not absolute https (http only to this machine), or with credentials, a query or a fragment; null when it can.</summary>
    public static string? Validate(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri || !(uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
        {
            return "The terminology server must be an absolute https URL (http only on this machine), for example https://tx.fhir.org/r4.";
        }

        return uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0
            ? "The terminology server URL must not carry credentials, a query or a fragment."
            : null;
    }

    /// <summary>
    /// GETs <c>&lt;base&gt;/&lt;operation&gt;?&lt;parameters&gt;</c> and returns the resource, which must be
    /// <paramref name="expectedType"/>; answers are cached by request URL.
    /// </summary>
    /// <exception cref="ToolRequestException">The server could not be reached in time, or did not answer as expected.</exception>
    public async Task<JsonObject> GetAsync(string operation, IReadOnlyList<(string Name, string Value)> parameters, string expectedType, CancellationToken cancellationToken) =>
        await GetOrNotFoundAsync(operation, parameters, expectedType, notFoundIsAnswer: false, cancellationToken)
            ?? throw new InvalidOperationException("A not-found answer was not asked for.");

    /// <summary>
    /// <see cref="GetAsync"/>, except that with <paramref name="notFoundIsAnswer"/> an HTTP 404 whose OperationOutcome has the
    /// issue code <c>not-found</c> returns null: the server does not know the code (or system), which is an answer for
    /// <c>$lookup</c> and <c>$validate-code</c>, though not one that proves absence anywhere else. Not cached.
    /// </summary>
    public async Task<JsonObject?> GetOrNotFoundAsync(string operation, IReadOnlyList<(string Name, string Value)> parameters, string expectedType, bool notFoundIsAnswer, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(parameters);

        foreach ((string name, _) in parameters)
        {
            if (!AllowedParameters.Contains(name))
            {
                throw new ArgumentException($"'{name}' is not a parameter a terminology request may carry.", nameof(parameters));
            }
        }

        var uri = new Uri(BaseUri, operation + (parameters.Count == 0 ? string.Empty : "?") + string.Join('&', parameters.Select(parameter => $"{Uri.EscapeDataString(parameter.Name)}={Uri.EscapeDataString(parameter.Value)}")));
        if (_answers.TryGetValue(uri.AbsoluteUri, out JsonObject? cached))
        {
            return (JsonObject)cached.DeepClone();
        }

        JsonObject? resource = await SendAsync(uri, operation, expectedType, notFoundIsAnswer, cancellationToken);
        if (resource is null)
        {
            return null;
        }

        _answers.TryAdd(uri.AbsoluteUri, resource);
        return (JsonObject)resource.DeepClone();
    }

    /// <summary>The FHIR version the server's CapabilityStatement reports (read once); null when it cannot be read or reports none.</summary>
    public Task<string?> FhirVersionAsync() => _fhirVersion.Value;

    // Read once per session; any failure means "not known", which callers handle by sending both parameter spellings.
    private async Task<string?> ReadFhirVersionAsync()
    {
        try
        {
            JsonObject statement = await GetAsync("metadata", [], "CapabilityStatement", CancellationToken.None);
            return statement["fhirVersion"] is JsonValue version && version.GetValueKind() == JsonValueKind.String ? version.GetValue<string>() : null;
        }
        catch (ToolRequestException)
        {
            return null;
        }
    }

    public void Dispose() => _client.Dispose();

    private async Task<JsonObject?> SendAsync(Uri uri, string operation, string expectedType, bool notFoundIsAnswer, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/fhir+json"));
        Interlocked.Increment(ref _requests);
        string what = $"{operation} on {BaseUri}";
        try
        {
            using HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            byte[] body = await ReadBoundedAsync(response, what, deadline.Token);
            JsonObject? resource = Parse(body);
            if (notFoundIsAnswer && response.StatusCode == HttpStatusCode.NotFound && OutcomeCodes(resource).Contains("not-found", StringComparer.Ordinal))
            {
                return null;
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw Error($"{what} answered HTTP {(int)response.StatusCode}{Outcome(resource)}{((int)response.StatusCode is >= 300 and < 400 ? "; redirects are not followed" : string.Empty)}");
            }

            string? type = resource?["resourceType"]?.GetValueKind() == JsonValueKind.String ? (string)resource["resourceType"]! : null;
            if (type == "OperationOutcome")
            {
                throw Error($"{what} answered with an OperationOutcome{Outcome(resource)}");
            }

            return type == expectedType && resource is not null
                ? resource
                : throw Error($"{what} answered {(type is null ? "something that is not a FHIR resource" : $"a {TerminologyNames.Token(type)}")} instead of a {expectedType}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw ToolRequestException.Error(UnavailableCode, $"The terminology server did not answer {what} within {_timeout.TotalSeconds:0} s; nothing was concluded from it.");
        }
        catch (HttpRequestException ex)
        {
            throw ToolRequestException.Error(UnavailableCode, $"The terminology server could not be reached for {what} ({ex.HttpRequestError}); nothing was concluded from it.");
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

    // The OperationOutcome's issue codes only, each shape-checked: its diagnostics and details are server text.
    private static string Outcome(JsonObject? resource)
    {
        if (resource?["resourceType"]?.GetValueKind() != JsonValueKind.String || (string)resource["resourceType"]! != "OperationOutcome")
        {
            return string.Empty;
        }

        List<string> codes = [.. OutcomeCodes(resource).Take(5)];
        return codes.Count == 0 ? " (an OperationOutcome)" : $" (OperationOutcome issue codes: {string.Join(", ", codes)})";
    }

    private static IEnumerable<string> OutcomeCodes(JsonObject? resource) =>
        resource?["resourceType"]?.GetValueKind() == JsonValueKind.String && (string)resource["resourceType"]! == "OperationOutcome" && resource["issue"] is JsonArray issues
            ? issues
                .Select(issue => issue?["code"])
                .Where(code => code?.GetValueKind() == JsonValueKind.String)
                .Select(code => TerminologyNames.Token((string)code!))
                .Distinct(StringComparer.Ordinal)
            : [];

    private static ToolRequestException Error(string reason) =>
        ToolRequestException.Error(ErrorCode, $"The terminology server did not give a usable answer: {reason}. Nothing was concluded from it.");

    private static Uri WithTrailingSlash(Uri uri) => uri.AbsolutePath.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
}
