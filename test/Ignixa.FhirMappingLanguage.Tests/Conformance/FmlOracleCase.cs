/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * One <test> entry from an <fml-tests> manifest section.
 */

namespace Ignixa.FhirMappingLanguage.Tests.Conformance;

/// <summary>
/// A single transform oracle case declared in the corpus <c>manifest.xml</c>.
/// </summary>
/// <param name="Version">Corpus version folder, e.g. <c>r5</c>.</param>
/// <param name="Name">Case name as declared in the manifest.</param>
/// <param name="SourceFile">Input resource file name, relative to the structure-mapping directory.</param>
/// <param name="MapFile">FML map file name, relative to the structure-mapping directory.</param>
/// <param name="OutputFile">Expected output file name, relative to the structure-mapping directory.</param>
public sealed record FmlOracleCase(
    string Version,
    string Name,
    string SourceFile,
    string MapFile,
    string OutputFile)
{
    /// <inheritdoc />
    public override string ToString() => $"{Version}/{Name}";
}
