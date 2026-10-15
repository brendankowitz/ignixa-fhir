/*
 * Copyright (c) 2026, Ignixa Contributors
 */

using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.FhirMappingLanguage.Serialization;
using Ignixa.Serialization.TestSupport;
using Shouldly;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Parser;

public class RefinedFmlSyntaxTests
{
    [Theory]
    [InlineData("first", ListMode.First)]
    [InlineData("not_first", ListMode.NotFirst)]
    [InlineData("last", ListMode.Last)]
    [InlineData("not_last", ListMode.NotLast)]
    [InlineData("only_one", ListMode.OnlyOne)]
    public void GivenOrderedSourceClauses_WhenRoundTripping_ThenEveryClauseIsPreserved(string mode, ListMode expectedMode)
    {
        var map = ParseRule($$"""
            src.value : integer 0..* default (10) {{mode}} as v
              where (v >= 10) check (v <= 100) log (v)
              -> tgt.value = v as result share values "copy value";
            """);

        var reparsed = RoundTrip(map);
        var rule = reparsed.Groups[0].Rules[0];
        rule.Name.ShouldBe("copy value");
        var source = rule.Sources[0];
        source.Type.ShouldBe("integer");
        source.Variable.ShouldBe("v");
        source.Cardinality!.Min.ShouldBe(0);
        source.Cardinality.Max.ShouldBeNull();
        source.ListMode.ShouldBe(expectedMode);
        source.Default.ShouldBeOfType<FhirPathExpression>().PathExpression.ShouldBe("10");
        source.Condition.ShouldBeOfType<FhirPathExpression>().PathExpression.ShouldBe("v >= 10");
        source.Check.ShouldBeOfType<FhirPathExpression>().PathExpression.ShouldBe("v <= 100");
        source.Log.ShouldBeOfType<FhirPathExpression>().PathExpression.ShouldBe("v");
        rule.Targets[0].Variable.ShouldBe("result");
        rule.Targets[0].ListMode.ShouldBe(ListMode.Share);
        rule.Targets[0].ListRuleId.ShouldBe("values");

        var resource = new StructureMapBuilder().Build(reparsed);
        resource.Group[0].Rule[0].Source[0].MutableNode()["listMode"]!.GetValue<string>().ShouldBe(mode);
        resource.Group[0].Rule[0].Target[0].ListRuleId.ShouldBe("values");
        var fromResource = new StructureMapParser().Parse(resource);
        fromResource.Groups[0].Rules[0].Sources[0].ListMode.ShouldBe(expectedMode);
        fromResource.Groups[0].Rules[0].Targets[0].ListRuleId.ShouldBe("values");
    }

    [Fact]
    public void GivenStandaloneTargets_WhenRoundTripping_ThenExpressionsAndAliasesArePreserved()
    {
        var map = ParseRule("src -> ('http://example.org/') as baseUrl, create('Encounter') as encounter;");

        var targets = RoundTrip(map).Groups[0].Rules[0].Targets;

        targets.Count.ShouldBe(2);
        targets[0].Context.ShouldBeNull();
        targets[0].Variable.ShouldBe("baseUrl");
        targets[0].Transform.ShouldBeOfType<FhirPathExpression>().PathExpression.ShouldBe("'http://example.org/'");
        targets[1].Context.ShouldBeNull();
        targets[1].Variable.ShouldBe("encounter");
        var transform = targets[1].Transform.ShouldBeOfType<TransformExpression>();
        transform.FunctionName.ShouldBe("create");
        transform.Arguments[0].ShouldBeOfType<LiteralExpression>().Value.ShouldBe("Encounter");
        var target = new StructureMapBuilder().Build(map).Group[0].Rule[0].Target[0];
        target.MutableNode()["transform"]!.GetValue<string>().ShouldBe("evaluate");
        target.Parameter[0].MutableNode()["valueString"]!.GetValue<string>().ShouldBe("'http://example.org/'");
    }

    [Fact]
    public void GivenBatchIdentity_WhenParsingAndRendering_ThenEveryFieldBecomesAnIndependentCopy()
    {
        var map = ParseRule("""src -> tgt: id, active, `not-found` "copy";""");

        var rules = RoundTrip(map).Groups[0].Rules;

        rules.Select(r => r.Name).ShouldBe(["copy-id", "copy-active", "copy-not-found"]);
        rules.Select(r => r.Sources.Single().Context.ShouldBeOfType<QualifiedIdentifierExpression>().Property)
            .ShouldBe(["id", "active", "not-found"]);
        rules.Select(r => r.Targets.Single().Context.ShouldBeOfType<QualifiedIdentifierExpression>().Property)
            .ShouldBe(["id", "active", "not-found"]);
    }

    [Fact]
    public void GivenMultipleDependentCallsAndNestedRules_WhenRoundTripping_ThenNoCallOrRuleIsLost()
    {
        var map = ParseRule("""
            src -> tgt then First(src, tgt), Second(src, tgt) {
              src.id as v -> tgt.id = v;
            } "dependent";
            """);

        var dependent = RoundTrip(map).Groups[0].Rules[0].Dependent.ShouldBeOfType<DependentExpression>();

        dependent.Invocations.Select(i => i.GroupName).ShouldBe(["First", "Second"]);
        dependent.Invocations[1].Arguments.Select(a => a.ShouldBeOfType<IdentifierExpression>().Name)
            .ShouldBe(["src", "tgt"]);
        dependent.Rules!.Rules.Count.ShouldBe(1);
        dependent.Rules.Rules[0].Sources[0].Variable.ShouldBe("v");
        var resource = new StructureMapBuilder().Build(map);
        resource.Group[0].Rule[0].Dependent.Select(d => d.Name).ShouldBe(["First", "Second"]);
        resource.Group[0].Rule[0].Rule.Count.ShouldBe(1);
        var fromResource = new StructureMapParser().Parse(resource);
        fromResource.Groups[0].Rules[0].Dependent.ShouldBeOfType<DependentExpression>()
            .Invocations.Select(i => i.GroupName).ShouldBe(["First", "Second"]);
    }

    [Theory]
    [InlineData("given.join(' ') & ' ' & family")]
    [InlineData("iif(active, 'yes', 'no')")]
    [InlineData("@2024-12-01T09:00:00")]
    [InlineData("@T09:00:00")]
    [InlineData("5 days")]
    [InlineData("5.5 'mg'")]
    [InlineData("2147483648L")]
    [InlineData("-1.5")]
    [InlineData("{}")]
    [InlineData("$this.id")]
    [InlineData("name.where(use = 'official').first()")]
    public void GivenFhirPathTransformArgument_WhenRoundTripping_ThenTheExpressionIsPreserved(string expression)
    {
        var map = ParseRule($"src -> tgt.value = evaluate(src, {expression});");

        var transform = RoundTrip(map).Groups[0].Rules[0].Targets[0].Transform.ShouldBeOfType<TransformExpression>();

        transform.FunctionName.ShouldBe("evaluate");
        transform.Arguments.Count.ShouldBe(2);
        transform.Arguments[0].ShouldBeOfType<IdentifierExpression>().Name.ShouldBe("src");
        transform.Arguments[1].ShouldBeOfType<FhirPathExpression>().PathExpression.ShouldBe(expression);
    }

    [Fact]
    public void GivenNamedTransformParameters_WhenRoundTripping_ThenNamesAndValuesArePreserved()
    {
        var map = ParseRule("""
            src.code as v -> tgt.code = translate(v, map_uri: 'http://example.org/map', output: 'Coding');
            """);

        var transform = RoundTrip(map).Groups[0].Rules[0].Targets[0].Transform.ShouldBeOfType<TransformExpression>();

        transform.Arguments[1].ShouldBeOfType<NamedArgumentExpression>().Name.ShouldBe("map_uri");
        transform.Arguments[1].ShouldBeOfType<NamedArgumentExpression>()
            .Value.ShouldBeOfType<LiteralExpression>().Value.ShouldBe("http://example.org/map");
        transform.Arguments[2].ShouldBeOfType<NamedArgumentExpression>().Name.ShouldBe("output");
        transform.Arguments[2].ShouldBeOfType<NamedArgumentExpression>()
            .Value.ShouldBeOfType<LiteralExpression>().Value.ShouldBe("Coding");
        Should.Throw<NotSupportedException>(() => new StructureMapBuilder().Build(map))
            .Message.ShouldContain("Named transform parameters");
    }

    [Fact]
    public void GivenLetDeclarations_WhenRoundTripping_ThenLiteralAndComputedConstantsArePreserved()
    {
        var map = new MappingParser().Parse("""
            map 'http://example.org/test' = Test
            let baseUrl = 'http://example.org/';
            let size = 20;
            let computed = %baseUrl & 'suffix';
            group Main(source src, target tgt) { src -> tgt; }
            """);

        var constants = RoundTrip(map).Constants;

        constants.Select(c => c.Name).ShouldBe(["baseUrl", "size", "computed"]);
        constants[0].Value.ShouldBeOfType<LiteralExpression>().Value.ShouldBe("http://example.org/");
        constants[1].Value.ShouldBeOfType<LiteralExpression>().Value.ShouldBe(20);
        constants[2].Value.ShouldBeOfType<FhirPathExpression>().PathExpression.ShouldBe("%baseUrl & 'suffix'");
    }

    [Fact]
    public void GivenRepeatedAndMultilineMetadata_WhenRoundTripping_ThenOrderTypesAndTextArePreserved()
    {
        var map = new MappingParser().Parse(""""
            /// url = 'http://example.org/test'
            /// name = 'Test'
            /// experimental = true
            /// jurisdiction =
            /// jurisdiction.text = 'First'
            /// jurisdiction =
            /// jurisdiction.text = 'Second'
            /// description = """
            **Markdown** with "quotes".
            A second line.
            """
            group Main(source src, target tgt) { src -> tgt; }
            """");

        var reparsed = RoundTrip(map);

        reparsed.MetadataDeclarations.Count.ShouldBe(8);
        reparsed.MetadataDeclarations.ShouldBe(map.MetadataDeclarations);
        reparsed.Metadata["description"].ShouldBe("""
            **Markdown** with "quotes".
            A second line.
            """);
        reparsed.Metadata["experimental"].ShouldBe("true");
        reparsed.MetadataDeclarations.Where(d => d.Key == "jurisdiction.text").Select(d => d.Value)
            .ShouldBe(["'First'", "'Second'"]);
    }

    [Theory]
    [InlineData(@"'given.join(\' \')'", "given.join(' ')")]
    [InlineData(@"'line\n\u0041\\end'", "line\nA\\end")]
    [InlineData("'don''t'", "don't")]
    public void GivenEscapedStrings_WhenRoundTripping_ThenDecodedValuesArePreserved(string literal, string expected)
    {
        var map = ParseRule($"src -> tgt.text = {literal};");

        var transform = RoundTrip(map).Groups[0].Rules[0].Targets[0].Transform;

        transform.ShouldBeOfType<LiteralExpression>().Value.ShouldBe(expected);
    }

    [Fact]
    public void GivenQuotedConceptMapCodes_WhenRoundTripping_ThenCodesAndEquivalenceArePreserved()
    {
        var map = new MappingParser().Parse("""
            map 'http://example.org/test' = 'Test'
            conceptmap 'codes' {
              prefix s = 'http://example.org/source'
              prefix t = 'http://example.org/target'
              s:'not-found' - t:"a.b"
            }
            group Main(source src, target tgt) { src -> tgt; }
            """);

        var code = RoundTrip(map).ConceptMaps[0].Groups[0].CodeMaps[0];

        code.SourceCode.ShouldBe("not-found");
        code.TargetCode.ShouldBe("a.b");
        code.Equivalence.ShouldBe(ConceptMapEquivalence.Equivalent);
    }

    [Theory]
    [InlineData("src -> tgt.id = evaluate(src, name.where(true);")]
    [InlineData("src -> tgt.id = evaluate(src, name[0));")]
    [InlineData("src -> tgt.id = evaluate(src, );")]
    public void GivenIncompleteTransformArguments_WhenParsing_ThenTheMapIsRejected(string rule)
    {
        Should.Throw<ParseException>(() => ParseRule(rule));
    }

    private static MapExpression ParseRule(string rule) =>
        new MappingParser().Parse($$"""
            map 'http://example.org/test' = 'Test'
            group Main(source src, target tgt) {
              {{rule}}
            }
            """);

    private static MapExpression RoundTrip(MapExpression map)
    {
        var serializer = new FmlSerializer();
        var rendered = serializer.Serialize(map);
        var reparsed = new MappingParser().Parse(rendered);
        serializer.Serialize(reparsed).ShouldBe(rendered);
        return reparsed;
    }
}
