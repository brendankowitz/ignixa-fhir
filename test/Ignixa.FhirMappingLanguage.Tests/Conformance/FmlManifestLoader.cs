/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Reads <fml-tests> entries out of the corpus manifest.
 */

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Ignixa.FhirMappingLanguage.Tests.Conformance;

/// <summary>
/// Reads the <c>&lt;fml-tests&gt;</c> section of a corpus <c>manifest.xml</c>.
/// </summary>
public static class FmlManifestLoader
{
    /// <summary>
    /// Loads every declared transform oracle case for a corpus version folder
    /// such as <c>r5</c> or <c>r4b</c>.
    /// </summary>
    public static IReadOnlyList<FmlOracleCase> Load(string version)
    {
        var manifestPath = Path.Combine(FmlTestCasesLocator.StructureMappingDirectory(version), "manifest.xml");

        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"Corpus manifest not found at {manifestPath}.", manifestPath);
        }

        var document = XDocument.Load(manifestPath);

        return document.Descendants("fml-tests")
            .Elements("test")
            .Select(element => new FmlOracleCase(
                version,
                (string?)element.Attribute("name") ?? string.Empty,
                (string?)element.Attribute("source") ?? string.Empty,
                (string?)element.Attribute("map") ?? string.Empty,
                (string?)element.Attribute("output") ?? string.Empty))
            .Where(c => c.MapFile.Length > 0 && c.SourceFile.Length > 0 && c.OutputFile.Length > 0)
            .ToList();
    }
}
