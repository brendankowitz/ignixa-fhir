using Semver;

namespace Ignixa.PackageManagement.Models;

/// <summary>An exact ordinal NPM identity, including prerelease and build metadata.</summary>
public sealed record NpmPackageIdentity
{
    public NpmPackageIdentity(string name, string version)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(version);
        Name = name.Trim();
        Version = version.Trim();
        string[] segments = Name.StartsWith('@') ? Name[1..].Split('/') : [Name];
        if (Name.Length is < 1 or > 214 || segments.Length != (Name.StartsWith('@') ? 2 : 1) ||
            segments.Any(segment => !ValidSegment(segment, allowUppercase: false)))
        {
            throw new ArgumentException("An exact lowercase NPM name is required.", nameof(name));
        }

        if (Version.Length is < 1 or > 256 ||
            !SemVersion.TryParse(Version, SemVersionStyles.Strict, out _))
        {
            throw new ArgumentException("An exact SemVer 2 version is required.", nameof(version));
        }
    }

    public string Name { get; }
    public string Version { get; }

    internal static bool ValidSegment(string value, bool allowUppercase) =>
        value.Length > 0 && IsLetterOrDigit(value[0], allowUppercase) &&
        value.All(c => IsLetterOrDigit(c, allowUppercase) || c is '.' or '_' or '-');

    private static bool IsLetterOrDigit(char c, bool allowUppercase) =>
        c is >= 'a' and <= 'z' or >= '0' and <= '9' || (allowUppercase && c is >= 'A' and <= 'Z');
}
