using System.Text;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.TestScript.Client;

public sealed class HttpTestRequestProvider(HttpClient httpClient) : ITestRequestProvider
{
    public async Task<TestResponse> ExecuteAsync(TestRequest request, CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(request.Method, request.Url);

        if (request.Body is not null)
        {
            var contentType = request.Headers.GetValueOrDefault("Content-Type", "application/fhir+json");
            httpRequest.Content = new StringContent(
                request.Body.SerializeToString(),
                Encoding.UTF8,
                contentType);
        }

        foreach (var (key, value) in request.Headers)
        {
            if (key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                continue;
            httpRequest.Headers.TryAddWithoutValidation(key, value);
        }

        var httpResponse = await httpClient.SendAsync(httpRequest, cancellationToken);

        var responseBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
        ResourceJsonNode? body = null;
        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            try { body = JsonSourceNodeFactory.Parse(responseBody); }
            catch (System.Text.Json.JsonException) { }
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in httpResponse.Headers.Concat(httpResponse.Content.Headers))
        {
            headers[header.Key] = string.Join(", ", header.Value);
        }

        return new TestResponse
        {
            StatusCode = (int)httpResponse.StatusCode,
            Body = body,
            Headers = headers
        };
    }
}
