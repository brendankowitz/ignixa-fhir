/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Resolves paths into the vendored HL7 fhir-test-cases corpus.
 */

using System;
using System.IO;

namespace Ignixa.FhirMappingLanguage.Tests.Conformance;

/// <summary>
/// Resolves paths into the vendored HL7 <c>fhir-test-cases</c> corpus.
/// The corpus is downloaded by the <c>DownloadFhirTestCases</c> MSBuild target
/// into <c>TestData/fhir-test-cases</c> beside the project file.
/// </summary>
public static class FmlTestCasesLocator
{
    private static readonly Lazy<string> RootDirectory = new(FindRoot);

    /// <summary>
    /// Gets the root of the vendored corpus.
    /// </summary>
    public static string Root => RootDirectory.Value;

    /// <summary>
    /// Gets the <c>structure-mapping</c> directory for a FHIR version folder such as
    /// <c>r5</c> or <c>r4b</c>.
    /// </summary>
    public static string StructureMappingDirectory(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        return Path.Combine(Root, version, "structure-mapping");
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "TestData", "fhir-test-cases");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the vendored fhir-test-cases corpus. Run 'dotnet build' on " +
            "test/Ignixa.FhirMappingLanguage.Tests to download it.");
    }
}
