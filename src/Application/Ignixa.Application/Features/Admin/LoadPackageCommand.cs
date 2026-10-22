using Ignixa.Application.Features.Conformance;
using Ignixa.PackageManagement.Models;
using Medino;

namespace Ignixa.Application.Features.Admin;

/// <summary>
/// Command to load a FHIR package from the NPM registry into a specific tenant's database.
/// Set <paramref name="IncludeDependencies"/> to walk the declared dependency closure
/// (e.g. AU Core pulls AU Base, HL7 Terminology, UV Extensions in one call).
/// </summary>
public record LoadPackageCommand(
    string TenantId,
    string PackageId,
    string Version,
    bool IncludeDependencies = false) : IRequest<LoadPackageResult>;

/// <summary>
/// Result of loading a package.
/// </summary>
public record LoadPackageResult
{
    /// <summary>
    /// Package ID (e.g., "hl7.fhir.us.core").
    /// </summary>
    public required string PackageId { get; init; }

    /// <summary>
    /// Package version (e.g., "5.0.1").
    /// </summary>
    public required string PackageVersion { get; init; }

    /// <summary>
    /// Total number of resources extracted.
    /// </summary>
    public int TotalResources { get; init; }

    /// <summary>
    /// Number of resources imported.
    /// </summary>
    public int ImportedResources { get; init; }

    /// <summary>
    /// Time taken in milliseconds.
    /// </summary>
    public long DurationMilliseconds { get; init; }

    /// <summary>
    /// Breakdown by resource type.
    /// </summary>
    public Dictionary<string, int> ResourcesByType { get; init; } = new();

    /// <summary>
    /// Legacy count-based package summary strings. <c>null</c> for single-package loads;
    /// prefer <see cref="ClosureMembers"/> for authoritative per-observation outcomes.
    /// </summary>
    public IReadOnlyList<string>? LoadedPackages { get; init; }

    /// <summary>
    /// Human-readable skipped-package summary strings, including informational core-package entries.
    /// <c>null</c> for single-package loads or when nothing was skipped. Use <see cref="IsComplete"/> or
    /// <c>ClosureMembers[].IsBlocking</c> to determine whether the load is incomplete.
    /// </summary>
    public IReadOnlyList<string>? SkippedPackages { get; init; }

    /// <summary>
    /// Typed per-package closure outcomes; empty for single-package loads.
    /// </summary>
    public IReadOnlyList<PackageClosureMember> ClosureMembers { get; init; } = [];

    /// <summary>
    /// Activation outcome for the requested root package.
    /// </summary>
    public ActivationResult? Activation { get; init; }

    /// <summary>
    /// True only when dependency-closure import has no blocking member and the requested root
    /// package activated successfully. Dependencies imported through
    /// <see cref="LoadPackageCommand.IncludeDependencies"/> are not activated by this operation.
    /// Installed content is retained when this is false.
    /// </summary>
    public bool IsComplete => ClosureMembers.All(member => !member.IsBlocking) && Activation is { Success: true };
}
