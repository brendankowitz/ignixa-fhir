namespace Ignixa.PackageManagement.Models;

/// <summary>An archive validation failure without package content or untrusted parser messages.</summary>
/// <param name="diagnostic">Content-free failure coordinates; must not be null.</param>
/// <exception cref="ArgumentNullException">The diagnostic is null.</exception>
public sealed class PackageExtractionException(PackageExtractionDiagnostic diagnostic)
    : IOException($"Strict package extraction failed: {(diagnostic ?? throw new ArgumentNullException(nameof(diagnostic))).Code}.")
{
    /// <summary>Failure category and optional physical header index/configured limit.</summary>
    public PackageExtractionDiagnostic Diagnostic { get; } = diagnostic;
}
