using System.Reflection;
using Ignixa.Abstractions;

namespace Ignixa.SqlOnFhir;

/// <summary>
/// Provides embedded SQL-on-FHIR ViewDefinition package resources.
/// The ViewDefinition StructureDefinition is embedded in this assembly and loaded
/// automatically on application startup.
/// </summary>
public class SqlOnFhirEmbeddedPackage : IEmbeddedPackage
{
    /// <summary>
    /// The package ID matching entries in FHIR package registry.
    /// </summary>
    public string PackageId => "local.ignixa.sqlonfhir";

    /// <summary>
    /// The resource prefix for manifest resources in this assembly.
    /// Note: MSBuild converts hyphens to underscores in embedded resource names.
    /// Files at: src/Ignixa.SqlOnFhir/packages/sql-on-fhir-v2/package/
    /// Become: Ignixa.SqlOnFhir.packages.sql_on_fhir_v2.package
    /// </summary>
    public string ResourcePrefix => "Ignixa.SqlOnFhir.packages.sql_on_fhir_v2.package";

    /// <summary>
    /// The assembly containing the embedded resources.
    /// </summary>
    public Assembly Assembly => typeof(SqlOnFhirEmbeddedPackage).Assembly;
}
