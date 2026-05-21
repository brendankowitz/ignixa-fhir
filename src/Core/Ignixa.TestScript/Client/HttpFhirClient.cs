using System.Text;
using System.Text.Json.Nodes;

namespace Ignixa.TestScript.Client;

public sealed class HttpFhirClient(HttpClient httpClient) : IFhirClient
{
    public string BaseUrl => httpClient.BaseAddress?.ToString().TrimEnd('/') ?? string.Empty;

    public async Task<FhirResponse> SendAsync(FhirRequest request, CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(request.Method, request.Url);

        if (request.Body is not null)
        {
            httpRequest.Content = new StringContent(
                request.Body.ToJsonString(),
                Encoding.UTF8,
                "application/fhir+json");
        }

        foreach (var (key, value) in request.Headers)
        {
            httpRequest.Headers.TryAddWithoutValidation(key, value);
        }

        var httpResponse = await httpClient.SendAsync(httpRequest, cancellationToken);

        var responseBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
        JsonNode? body = null;
        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            try { body = JsonNode.Parse(responseBody); }
            catch { /* non-JSON response body is valid */ }
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in httpResponse.Headers.Concat(httpResponse.Content.Headers))
        {
            headers[header.Key] = string.Join(", ", header.Value);
        }

        return new FhirResponse
        {
            StatusCode = (int)httpResponse.StatusCode,
            Body = body,
            Headers = headers
        };
    }
}
