/*
 * Copyright (c) 2025, Ignixa Contributors
 */

using System.Text;
using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Pins the one thing that keeps <c>~</c>'s structural descent - and the far older descent in
/// <c>FunctionHelpers.AreElementsEqual</c> that <c>=</c>, <c>in</c>, <c>contains</c>, <c>distinct()</c>,
/// <c>|</c>, <c>intersect</c> and <c>exclude</c> all share - from being able to exhaust the stack.
/// </summary>
/// <remarks>
/// <para>
/// Both descents recurse once per element-tree level with no depth counter, and a
/// <see cref="StackOverflowException"/> cannot be caught: it terminates the process, which would make
/// <c>ElementSearchIndexer</c>'s containment ladder a promise it could not keep. Measured
/// out-of-process on this branch, the floor is around 3,900 nested levels for the equivalence descent
/// and between 3,200 and 4,800 for the equality one - both far past anything that can arrive.
/// </para>
/// <para>
/// What holds them apart is <see cref="JsonSerializerOptions.MaxDepth"/>, which defaults to 64 and is
/// overridden nowhere in <c>src/</c>. Every element tree the evaluator sees is parsed through
/// <c>JsonSourceNodeFactory</c>, so a document deeper than that is rejected as JSON before any element
/// exists to walk - the first test below is what would fail if that ceiling were ever raised. A second,
/// non-configurable ceiling backs it up: <c>Utf8JsonWriter</c> refuses to write past 1,000 levels, so
/// nothing deeper can be stored or returned even if it were built in memory.
/// </para>
/// <para>
/// A depth guard on the equivalence descent alone would not close this. The equality descent runs first
/// - <c>AreElementsEquivalent</c> calls it on its first rung - and reaches the same floor from
/// expressions that generated search parameters really do use, so guarding only <c>~</c> would buy a
/// green test and no containment.
/// </para>
/// </remarks>
public class EquivalenceRecursionDepthTests
{
    /// <summary>
    /// Comfortably past the ~30 nested extensions the default parser ceiling admits, without pinning
    /// the exact boundary, which is an implementation detail of how many JSON levels a nesting costs.
    /// </summary>
    private const int LevelsPastTheParserCeiling = 40;

    /// <summary>
    /// Inside the ceiling, and already an order of magnitude past any nesting FHIR itself produces.
    /// </summary>
    private const int LevelsInsideTheParserCeiling = 25;

    private static readonly IFhirSchemaProvider Schema = FhirVersion.R4.GetSchemaProvider();

    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    [Fact]
    public void GivenAResourceNestedPastTheParserCeiling_WhenParsing_ThenItIsRejectedBeforeAnyElementTreeExists()
    {
        // Arrange
        var json = NestedExtensionPatient(LevelsPastTheParserCeiling, leftLeaf: "a", rightLeaf: "b");

        // Act
        var parse = () => ResourceJsonNode.Parse(json);

        // Assert
        parse.ShouldThrow<JsonException>();
    }

    [Fact]
    public void GivenTheDeepestNestingTheParserAdmits_WhenComparingWithEquivalence_ThenTheDescentCompletes()
    {
        // Arrange
        var differing = ParseNative(NestedExtensionPatient(LevelsInsideTheParserCeiling, leftLeaf: "a", rightLeaf: "b"));
        var matching = ParseNative(NestedExtensionPatient(LevelsInsideTheParserCeiling, leftLeaf: "a", rightLeaf: "A"));

        // Act
        var differingResult = EvaluateBoolean(differing, "extension ~ modifierExtension");
        var matchingResult = EvaluateBoolean(matching, "extension ~ modifierExtension");

        // Assert - the second pair differs only in case, which equivalence ignores, so a true answer
        // proves the descent reached the leaf rather than stopping short of it.
        differingResult.ShouldBeFalse();
        matchingResult.ShouldBeTrue();
    }

    private static IElement ParseNative(string json) => ResourceJsonNode.Parse(json).ToElement(Schema);

    private bool EvaluateBoolean(IElement subject, string expression)
    {
        var parsed = _parser.Parse(expression);
        var result = _evaluator.Evaluate(subject, parsed, new FhirEvaluationContext { Resource = subject }).Single();
        return (bool)result.Value!;
    }

    private static string NestedExtensionPatient(int levels, string leftLeaf, string rightLeaf)
    {
        var sb = new StringBuilder();
        sb.Append("""{"resourceType":"Patient","id":"p","extension":[""");
        sb.Append(ExtensionChain(levels, leftLeaf));
        sb.Append("""],"modifierExtension":[""");
        sb.Append(ExtensionChain(levels, rightLeaf));
        sb.Append("]}");
        return sb.ToString();
    }

    private static string ExtensionChain(int levels, string leaf)
    {
        var sb = new StringBuilder();

        for (var i = 0; i < levels; i++)
        {
            sb.Append("""{"url":"http://example.org/nested","extension":[""");
        }

        sb.Append($$"""{"url":"http://example.org/leaf","valueString":"{{leaf}}"}""");

        for (var i = 0; i < levels; i++)
        {
            sb.Append("]}");
        }

        return sb.ToString();
    }
}
