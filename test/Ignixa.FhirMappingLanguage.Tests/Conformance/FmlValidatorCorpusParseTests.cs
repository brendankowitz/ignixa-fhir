/*
 * Copyright (c) 2025, Ignixa Contributors
 */

using System.IO;
using System.Linq;
using System.Text;
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Parser;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Ignixa.FhirMappingLanguage.Tests.Conformance;

/// <summary>
/// Parse ratchet over the two official <c>validator</c>-directory FML files. These exercise
/// parser constructs — a group <c>extends</c> target with a <c>&lt;&lt;type+&gt;&gt;</c> annotation
/// and a parenthesized FHIRPath transform — that appear nowhere in the <c>structure-mapping</c>
/// corpus, so pinning their parse output guards several parser fixes against regression.
/// </summary>
public class FmlValidatorCorpusParseTests(ITestOutputHelper output)
{
    private const string AddressFile = "map-general-test.fml";
    private const string SyntaxFile = "map-general-test2.fml";

    [Fact]
    public void GivenMapGeneralTest_WhenParsed_ThenHeaderAndDeclarationCountsMatch()
    {
        var map = ParseValidatorFile(AddressFile);

        map.Url.ShouldBe("http://hl7.org/fhir/StructureMap/Address4to3");
        map.Groups.Count.ShouldBe(1);
        map.Uses.Count.ShouldBe(2);
        map.Imports.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenMapGeneralTest_WhenParsingTheGroup_ThenExtendsTargetAndTypeModeSurvive()
    {
        var map = ParseValidatorFile(AddressFile);

        var group = map.Groups.ShouldHaveSingleItem();
        group.Extends.ShouldBe("Element");
        group.TypeMode.ShouldBe(GroupTypeMode.TypeAndTypes);
    }

    [Fact]
    public void GivenMapGeneralTest2_WhenParsed_ThenHeaderAndDeclarationCountsMatch()
    {
        var map = ParseValidatorFile(SyntaxFile);

        map.Url.ShouldBe("http://github.com/FHIR/fhir-test-cases/r5/fml/syntax");
        map.Groups.Count.ShouldBe(2);
        map.Uses.Count.ShouldBe(2);
        map.Imports.Count.ShouldBe(0);
    }

    [Fact]
    public void GivenMapGeneralTest2_WhenParsingTheRootUuidRule_ThenParenthesizedFhirPathTransformSurvives()
    {
        var map = ParseValidatorFile(SyntaxFile);

        var rule = map.Groups
            .SelectMany(g => g.Rules)
            .Single(r => r.Name == "rootuuid");

        var transform = rule.Targets
            .Select(t => t.Transform)
            .OfType<FhirPathExpression>()
            .ShouldHaveSingleItem();

        transform.PathExpression.ShouldBe("'urn:uuid:' + r.lower()");
    }

    private MapExpression ParseValidatorFile(string fileName)
    {
        var path = Path.Combine(FmlTestCasesLocator.ValidatorDirectory(), fileName);

        File.Exists(path).ShouldBeTrue(
            $"Validator corpus file not found: {path}. Verify the fhir-test-cases download completed.");

        output.WriteLine($"Parsing {path}");
        return new MappingParser().Parse(File.ReadAllText(path, Encoding.UTF8));
    }
}
