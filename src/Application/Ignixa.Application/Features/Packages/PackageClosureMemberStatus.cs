using System.Text.Json.Serialization;

namespace Ignixa.PackageManagement.Models;

/// <summary>
/// Outcome observed for one package while walking a dependency closure.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PackageClosureMemberStatus>))]
public enum PackageClosureMemberStatus
{
    Imported,
    AlreadyInstalled,
    ProvidedInProcess,
    VersionConflict,
    LoadFailed,
    ManifestUnavailable,
}
