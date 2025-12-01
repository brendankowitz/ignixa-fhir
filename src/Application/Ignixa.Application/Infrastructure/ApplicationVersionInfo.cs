// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using Ignixa.Domain.Abstractions;

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// Provides application version information from assembly attributes.
/// </summary>
/// <remarks>
/// <para>
/// Version information is retrieved from assembly attributes set by GitVersion.MsBuild during build.
/// The priority order for version retrieval is:
/// 1. AssemblyInformationalVersionAttribute - full semver including prerelease/build metadata
/// 2. AssemblyFileVersionAttribute - file version
/// 3. AssemblyVersionAttribute - basic assembly version
/// 4. Default fallback: "0.0.0-dev"
/// </para>
/// <para>
/// When GitVersion is disabled (e.g., Docker builds with /p:DisableGitVersion=true),
/// the assembly may not have the informational version, so fallbacks are used.
/// </para>
/// </remarks>
public sealed class ApplicationVersionInfo : IApplicationVersionInfo
{
    /// <summary>
    /// Default version used when GitVersion information is not available.
    /// </summary>
    private const string DefaultVersion = "0.0.0-dev";

    /// <summary>
    /// Default application name.
    /// </summary>
    private const string DefaultName = "Ignixa FHIR Server";

    /// <summary>
    /// Initializes a new instance of the <see cref="ApplicationVersionInfo"/> class.
    /// Retrieves version information from the entry assembly (Ignixa.Api).
    /// </summary>
    public ApplicationVersionInfo()
        : this(Assembly.GetEntryAssembly())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ApplicationVersionInfo"/> class
    /// with a specific assembly. Used for testing.
    /// </summary>
    /// <param name="assembly">The assembly to retrieve version information from.</param>
    internal ApplicationVersionInfo(Assembly? assembly)
    {
        Version = GetVersion(assembly);
        Name = GetName(assembly);
        ReleaseDate = GetReleaseDate(assembly);
    }

    /// <inheritdoc/>
    public string Version { get; }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public string ReleaseDate { get; }

    private static string GetVersion(Assembly? assembly)
    {
        if (assembly is null)
        {
            return DefaultVersion;
        }

        // Try AssemblyInformationalVersionAttribute first (set by GitVersion)
        // This includes full semver: "1.2.3-beta.1+5" or "1.2.3+build.456"
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            // GitVersion may add commit hash after a '+' - keep the full string for FHIR compliance
            // FHIR semver supports build metadata: "1.2.3+build.123"
            return informationalVersion;
        }

        // Fall back to AssemblyFileVersionAttribute
        var fileVersion = assembly
            .GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;

        if (!string.IsNullOrWhiteSpace(fileVersion))
        {
            // File version is typically "1.2.3.4" - take first 3 parts for semver
            var parts = fileVersion.Split('.');
            if (parts.Length >= 3)
            {
                return $"{parts[0]}.{parts[1]}.{parts[2]}";
            }

            return fileVersion;
        }

        // Fall back to assembly version
        var assemblyVersion = assembly.GetName().Version;
        if (assemblyVersion is not null)
        {
            return $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";
        }

        return DefaultVersion;
    }

    private static string GetName(Assembly? assembly)
    {
        if (assembly is null)
        {
            return DefaultName;
        }

        // Try AssemblyProductAttribute (set in Directory.Build.props)
        var product = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product;
        if (!string.IsNullOrWhiteSpace(product))
        {
            return product;
        }

        // Fall back to assembly name
        var assemblyName = assembly.GetName().Name;
        if (!string.IsNullOrWhiteSpace(assemblyName))
        {
            return assemblyName;
        }

        return DefaultName;
    }

    private static string GetReleaseDate(Assembly? assembly)
    {
        // For development builds, use current date
        // In production, GitVersion sets this via build process
        // We use the build date of the assembly as an approximation
        if (assembly is null)
        {
            return DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        }

        // Try to get the build timestamp from linker timestamp in PE header
        // This is more reliable than file system dates
        try
        {
            var location = assembly.Location;
            if (!string.IsNullOrEmpty(location) && File.Exists(location))
            {
                var buildDate = File.GetLastWriteTimeUtc(location);
                return DateOnly.FromDateTime(buildDate).ToString("yyyy-MM-dd");
            }
        }
        catch
        {
            // Ignore errors (e.g., single-file publish, assembly loaded from memory)
        }

        // Fall back to current date
        return DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
    }
}
