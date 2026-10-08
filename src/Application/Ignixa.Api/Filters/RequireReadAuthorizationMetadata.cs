namespace Ignixa.Api.Filters;

/// <summary>
/// Marks an administrative operation that must also be authorized as a resource read.
/// </summary>
public sealed class RequireReadAuthorizationMetadata
{
    public static RequireReadAuthorizationMetadata Instance { get; } = new();

    private RequireReadAuthorizationMetadata()
    {
    }
}
