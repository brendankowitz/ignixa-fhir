// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Caching;

/// <summary>
/// Where the persisted index cache lives, or that it is off. The cache is never placed inside the source
/// folder: the folder stays read-only, and a user-level directory survives across folders and checkouts.
/// </summary>
/// <param name="RootDirectory">Directory holding one subdirectory per cached folder; null when the cache is not in use.</param>
/// <param name="UnavailableReason">Why a cache was wanted but its location could not be determined; null otherwise.</param>
public sealed record IndexCacheOptions(string? RootDirectory, string? UnavailableReason = null)
{
    private const string ApplicationDirectoryName = "cli-on-fhir";

    /// <summary>No reading, no writing.</summary>
    public static IndexCacheOptions Disabled { get; } = new((string?)null);

    public bool IsEnabled => RootDirectory is not null;

    /// <summary>Caches under <paramref name="rootDirectory"/> instead of the user-level default.</summary>
    public static IndexCacheOptions At(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        return new IndexCacheOptions(Path.GetFullPath(rootDirectory));
    }

    /// <summary>
    /// The user-level default: <c>$XDG_CACHE_HOME/cli-on-fhir</c> when set to an absolute path, otherwise
    /// <c>%LOCALAPPDATA%\cli-on-fhir</c> on Windows and <c>~/.cache/cli-on-fhir</c> elsewhere. When the platform
    /// reports no such directory the result is unavailable, with the reason, rather than a guessed location.
    /// </summary>
    public static IndexCacheOptions UserDefault() => UserDefault(Environment.GetEnvironmentVariable("XDG_CACHE_HOME"));

    /// <summary><see cref="UserDefault()"/> with the <c>XDG_CACHE_HOME</c> value supplied, so it can be exercised without touching the process environment.</summary>
    internal static IndexCacheOptions UserDefault(string? xdgCacheHome)
    {
        if (!string.IsNullOrWhiteSpace(xdgCacheHome) && Path.IsPathRooted(xdgCacheHome))
        {
            return At(Path.Combine(xdgCacheHome, ApplicationDirectoryName));
        }

        string baseDirectory = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");

        // GetFolderPath yields an empty string when the platform has no such folder, which would turn the
        // combined path into a relative one.
        return baseDirectory is { Length: > 0 } && Path.IsPathRooted(baseDirectory)
            ? At(Path.Combine(baseDirectory, ApplicationDirectoryName))
            : new IndexCacheOptions(null, "the user cache directory could not be determined (no XDG_CACHE_HOME, and no home or local application data folder); use --cache-dir");
    }
}
