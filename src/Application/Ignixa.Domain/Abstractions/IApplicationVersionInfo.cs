// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Abstractions;

/// <summary>
/// Provides application version information for use in CapabilityStatement
/// and other metadata responses.
/// </summary>
/// <remarks>
/// Version information is typically retrieved from assembly attributes set by GitVersion.MsBuild
/// during the build process. When GitVersion is disabled (e.g., Docker builds with
/// /p:DisableGitVersion=true), a fallback version is used.
/// </remarks>
public interface IApplicationVersionInfo
{
    /// <summary>
    /// Gets the semantic version string (e.g., "1.2.3", "1.2.3-beta.1+build.123").
    /// This includes prerelease tags and build metadata when available.
    /// Used for CapabilityStatement.version and CapabilityStatement.software.version.
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Gets the application name (e.g., "Ignixa FHIR Server").
    /// Used for CapabilityStatement.software.name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the release date in ISO 8601 format (e.g., "2025-10-16").
    /// For development builds, this is typically the build date.
    /// Used for CapabilityStatement.software.releaseDate.
    /// </summary>
    string ReleaseDate { get; }
}
