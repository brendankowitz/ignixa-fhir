using System.Reflection;

namespace Ignixa.Abstractions;

/// <summary>
/// Represents an embedded FHIR package bundled within a .NET assembly.
/// Provides metadata for loading package resources from assembly manifest resources.
/// </summary>
public interface IEmbeddedPackage
{
    /// <summary>
    /// Gets the package identifier (e.g., "local.ignixa.sqlonfhir").
    /// </summary>
    string PackageId { get; }

    /// <summary>
    /// Gets the assembly resource prefix for this package's embedded resources.
    /// Example: "Ignixa.SqlOnFhir.packages.sql-on-fhir-v2"
    /// </summary>
    string ResourcePrefix { get; }

    /// <summary>
    /// Gets the assembly containing the embedded package resources.
    /// </summary>
    Assembly Assembly { get; }
}
