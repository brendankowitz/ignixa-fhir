using System.Text.Json.Serialization;

namespace Ignixa.PackageManagement.Models;

/// <summary>
/// Outcome observed for one package while walking a dependency closure.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PackageClosureMemberStatus>))]
public enum PackageClosureMemberStatus
{
    /// <summary>Newly imported.</summary>
    Imported,

    /// <summary>Version already present; import skipped.</summary>
    AlreadyInstalled,

    /// <summary>Core package served in-process; not blocking.</summary>
    ProvidedInProcess,

    /// <summary>Another version of the package ID was selected first.</summary>
    VersionConflict,

    /// <summary>Dependency failed to load.</summary>
    LoadFailed,

    /// <summary>Installed, but its dependencies could not be walked.</summary>
    ManifestUnavailable,
}
