namespace Ignixa.Abstractions;

/// <summary>
/// Configuration options for NpmPackageLoader.
/// Allows overriding registry URL, timeouts, and retry policies.
/// </summary>
public class NpmPackageLoaderOptions
{
    /// <summary>
    /// The base URL of the NPM package registry.
    /// Default: https://packages.simplifier.net (Simplifier.net FHIR Package API)
    /// Examples:
    /// - https://packages.simplifier.net (Simplifier.net FHIR registry - provides better search)
    /// - https://packages.fhir.org (official FHIR registry)
    /// - https://build.fhir.org (CI builds)
    /// - https://custom-registry.example.com (private registry)
    /// </summary>
    public string RegistryUrl { get; set; } = "https://packages.simplifier.net";

    /// <summary>
    /// Optional timeout for HTTP requests when downloading packages.
    /// If not set, uses HttpClient's default timeout configuration.
    /// </summary>
    public TimeSpan? RequestTimeout { get; set; }

    /// <summary>
    /// Enable retry policies for transient failures.
    /// Default: true
    /// </summary>
    public bool EnableRetryPolicies { get; set; } = true;
}
