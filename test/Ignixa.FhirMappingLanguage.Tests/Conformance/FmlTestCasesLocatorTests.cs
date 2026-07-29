/*
 * Copyright (c) 2025, Ignixa Contributors
 */

using System.IO;
using Shouldly;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Conformance;

public class FmlTestCasesLocatorTests
{
    [Theory]
    [InlineData("r5")]
    [InlineData("r4b")]
    public void GivenAVendoredCorpus_WhenLocatingStructureMappingDirectory_ThenTheDirectoryExists(string version)
    {
        var directory = FmlTestCasesLocator.StructureMappingDirectory(version);

        Directory.Exists(directory).ShouldBeTrue($"Expected the vendored corpus at {directory}. Run 'dotnet build' to download it.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void GivenABlankVersion_WhenLocatingStructureMappingDirectory_ThenThrowsArgumentException(string? version)
    {
        Should.Throw<ArgumentException>(() => FmlTestCasesLocator.StructureMappingDirectory(version!));
    }
}
