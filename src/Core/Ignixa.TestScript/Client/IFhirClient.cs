namespace Ignixa.TestScript.Client;

public interface IFhirClient
{
    string BaseUrl { get; }
    Task<FhirResponse> SendAsync(FhirRequest request, CancellationToken cancellationToken);
}
