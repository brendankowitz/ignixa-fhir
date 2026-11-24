/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Size and nesting limits on mapping text.
 */

using System.Text;
using Ignixa.Abstractions;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Mutator;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.FhirMappingLanguage.Tests.TestHelpers;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Serialization.TestSupport;
using Ignixa.Specification.Extensions;
using Shouldly;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Parser;

public class MappingParserSizeLimitTests
{
    private const int OneMegabyte = 1024 * 1024;

    [Fact]
    public void GivenBracesNestedOneLevelBeyondTheLimit_WhenParsed_ThenThrowsParseException()
    {
        // Arrange: the group's own brace counts, so this many nested rule sets is one level too deep.
        var mapping = NestedRuleSets(MappingParser.MaxNestingDepth);

        // Act
        var exception = Should.Throw<ParseException>(() => new MappingParser().Parse(mapping));

        // Assert
        exception.Message.ShouldContain("maximum nesting depth");
    }

    [Fact]
    public void GivenMappingTextLongerThanTheLimit_WhenParsed_ThenThrowsParseException()
    {
        // Arrange
        var mapping = NestedRuleSets(1).PadRight(MappingParser.MaxMappingTextLength + 1);

        // Act
        var exception = Should.Throw<ParseException>(() => new MappingParser().Parse(mapping));

        // Assert
        exception.Message.ShouldContain("maximum is");
    }

    [Fact]
    public void GivenNestingAtTheLimit_WhenParsedAndExecutedOnAOneMegabyteStack_ThenTheMapRuns()
    {
        // Arrange: the limit only protects the process if everything it admits also runs on a 1 MB stack,
        // smaller than the 1.5 MB thread-pool default.
        var mapping = NestedRuleSets(MappingParser.MaxNestingDepth - 1);
        var schema = FhirVersion.R5.GetSchemaProvider();
        var source = ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"p1"}""");
        var target = ResourceJsonNode.Parse("""{"resourceType":"Patient"}""");
        var mutator = new JsonNodeMutator(new FhirPathEvaluator(), new FhirPathParser(), () => schema);
        var context = new MappingContext();
        context.SetSource("src", source.ToElement(schema));
        context.SetTarget("tgt", target.ToElement(schema));
        context.SetTargetResource("tgt", target);

        // Act
        StackSizedThread.Run(
            () =>
            {
                var map = new MappingParser().Parse(mapping);
                new MappingEvaluator(MappingEvaluatorOptions.Default, mutator).Execute(map, context);
                return map;
            },
            OneMegabyte);

        // Assert
        context.Errors.ShouldBeEmpty();
        target.MutableNode()["id"]!.GetValue<string>().ShouldBe("p1");
    }

    [Fact]
    public void GivenNestingWithinTheLimitButBeyondASmallThreadStack_WhenParsed_ThenThrowsParseException()
    {
        // Arrange: on a 256 KB thread the grammar's own stack check, not the nesting bound, has to stop descent.
        var mapping = NestedRuleSets(MappingParser.MaxNestingDepth - 1);

        // Act
        var exception = Should.Throw<ParseException>(
            () => StackSizedThread.Run(() => new MappingParser().Parse(mapping), 256 * 1024));

        // Assert
        exception.InnerException.ShouldBeOfType<InsufficientExecutionStackException>();
    }

    private static string NestedRuleSets(int levels)
    {
        var text = new StringBuilder("map \"http://example.org/m\" = \"M\"\ngroup Main(source src, target tgt) {\n");
        for (var i = 0; i < levels; i++)
        {
            text.Append("src -> tgt then {\n");
        }

        text.Append("src.id as i -> tgt.id = i;\n");
        for (var i = 0; i < levels; i++)
        {
            text.Append("};\n");
        }

        return text.Append("}\n").ToString();
    }
}
