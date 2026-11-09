namespace Ignixa.Api.Filters;

/// <summary>Marks an endpoint that runs <see cref="FhirAuthorizationFilter"/>, so its coverage can be verified.</summary>
public sealed class FhirAuthorizationRequiredMetadata
{
    public static FhirAuthorizationRequiredMetadata Instance { get; } = new();

    private FhirAuthorizationRequiredMetadata()
    {
    }
}
