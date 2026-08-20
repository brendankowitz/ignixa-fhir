/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * The System/FHIR namespace distinction must be a property of the element, not of the CLR class that
 * happens to wrap it. These tests pin that on the axis where a class-name heuristic broke: R5+ type
 * rules over engine-produced System values, compared across both evaluation paths.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Shouldly;
using Xunit;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Holds the compiled and interpreted paths to the same answer for <c>ofType()</c> over values the
/// engine itself produced, on the versions where the R5 alias gate is closed.
/// </summary>
/// <remarks>
/// <para>
/// <c>count()</c> returns a <c>System.Integer</c> and <c>exists()</c> a <c>System.Boolean</c>, so
/// <c>ofType(Integer)</c> and <c>ofType(Boolean)</c> must select them on every version. Both paths
/// carry those results in their own private <see cref="IElement"/> wrapper, and while System-ness was
/// inferred from the wrapper's CLR class name the two paths classified the same value differently:
/// the interpreter's wrapper contained "Primitive" and the compiler's did not. Below R5 the pre-R5
/// cast alias rescued the misclassified value and hid the split; from R5 the gate closes and the
/// compiled path returned empty where the interpreter returned the value.
/// </para>
/// <para>
/// These cases therefore have to run on R5 or later and compare the two paths. A single-path
/// assertion, or the same assertion on R4, passes with the defect present.
/// </para>
/// </remarks>
public class SystemValueTypeMatchingTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();
    private readonly FhirPathDelegateCompiler _compiler = new(new FhirPathEvaluator());

    private const string ObservationJson = """
    {
      "resourceType": "Observation",
      "id": "example",
      "status": "final",
      "code": { "text": "test" },
      "valueString": "typed"
    }
    """;

    public static TheoryData<FhirVersion, string> SystemResultsAcrossPaths
    {
        get
        {
            var data = new TheoryData<FhirVersion, string>();
            foreach (var version in AllVersions)
            {
                data.Add(version, "value.count().ofType(Integer)");
                data.Add(version, "value.count().ofType(integer)");
                data.Add(version, "code.exists().ofType(Boolean)");
                data.Add(version, "code.exists().ofType(boolean)");
                data.Add(version, "value.count().is(Integer)");
                data.Add(version, "code.exists().as(Boolean)");
                data.Add(version, "'literal'.ofType(String)");
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(SystemResultsAcrossPaths))]
    public void GivenAnEngineProducedSystemValue_WhenTypeMatchedOnBothPaths_ThenTheAnswersAgree(
        FhirVersion version,
        string expression)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(ObservationJson).ToElement(schema);
        var ast = _parser.Parse(expression);
        var compiled = _compiler.TryCompile(ast);

        if (compiled is null)
        {
            // Declining to compile routes the caller to the interpreter, so the paths agree by
            // construction and there is nothing to compare.
            return;
        }

        // Act
        var compiledResult = Describe(() => compiled(element, Context(element, schema)));
        var interpretedResult = Describe(() => _evaluator.Evaluate(element, ast, Context(element, schema)));

        // Assert
        compiledResult.ShouldBe(
            interpretedResult,
            $"Compiled and interpreted evaluation of '{expression}' disagree on {version}.");
    }

    [Theory]
    [InlineData(FhirVersion.Stu3)]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    [InlineData(FhirVersion.R6)]
    public void GivenCountOnAnyVersion_WhenSelectedWithTheSystemSpelling_ThenTheIntegerSurvives(
        FhirVersion version)
    {
        // count() is specified to return System.Integer, so the System spelling selects it regardless
        // of the version gate, which governs only FHIR primitives read from the resource tree.

        // Arrange
        var schema = version.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(ObservationJson).ToElement(schema);

        // Act
        var interpreted = Interpret(element, "value.count().ofType(Integer)", schema);
        var compiled = Compile(element, "value.count().ofType(Integer)", schema);

        // Assert
        interpreted.ShouldHaveSingleItem().Value.ShouldBe(1);
        compiled.ShouldHaveSingleItem().Value.ShouldBe(1);
    }

    [Theory]
    [InlineData(FhirVersion.Stu3)]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    [InlineData(FhirVersion.R6)]
    public void GivenExistsOnAnyVersion_WhenSelectedWithTheSystemSpelling_ThenTheBooleanSurvives(
        FhirVersion version)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(ObservationJson).ToElement(schema);

        // Act
        var interpreted = Interpret(element, "code.exists().ofType(Boolean)", schema);
        var compiled = Compile(element, "code.exists().ofType(Boolean)", schema);

        // Assert
        interpreted.ShouldHaveSingleItem().Value.ShouldBe(true);
        compiled.ShouldHaveSingleItem().Value.ShouldBe(true);
    }

    /// <summary>
    /// Fails if the compiler's literal wrapper is renamed to something the old class-name heuristic
    /// would have matched, which would make the tests above pass for the wrong reason.
    /// </summary>
    /// <remarks>
    /// The defect these tests pin was that System-ness was read off <c>GetType().Name</c>. Renaming the
    /// wrapper to contain "Primitive" restores the correct answers while leaving the inference just as
    /// fragile, so the fix has to be observable as an explicit contract rather than as a spelling.
    /// </remarks>
    [Fact]
    public void GivenTheEvaluationPaths_WhenTheyWrapASystemValue_ThenSystemNessIsDeclaredNotInferred()
    {
        // Arrange
        var schema = FhirVersion.R5.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(ObservationJson).ToElement(schema);

        // Act
        var interpreted = Interpret(element, "value.count()", schema).ShouldHaveSingleItem();
        var compiled = Compile(element, "value.count()", schema).ShouldHaveSingleItem();

        // Assert
        interpreted.ShouldBeAssignableTo<ISystemValueElement>(
            "the interpreter's System wrapper must declare System-ness, not spell it in its class name");
        compiled.ShouldBeAssignableTo<ISystemValueElement>(
            "the compiler's System wrapper must declare System-ness, not spell it in its class name");
    }

    private static IReadOnlyList<FhirVersion> AllVersions =>
        [FhirVersion.Stu3, FhirVersion.R4, FhirVersion.R4B, FhirVersion.R5, FhirVersion.R6];

    private static EvaluationContext Context(IElement element, ISchema schema) =>
        new() { Resource = element, RootResource = element, Schema = schema };

    private IReadOnlyList<IElement> Interpret(IElement element, string expression, ISchema schema) =>
        _evaluator.Evaluate(element, _parser.Parse(expression), Context(element, schema)).ToList();

    private IReadOnlyList<IElement> Compile(IElement element, string expression, ISchema schema)
    {
        var compiled = _compiler.TryCompile(_parser.Parse(expression));
        compiled.ShouldNotBeNull($"'{expression}' must take the compiled path for this comparison to mean anything.");
        return compiled(element, Context(element, schema)).ToList();
    }

    private static IReadOnlyList<string> Describe(Func<IEnumerable<IElement>> evaluate)
    {
        try
        {
            return evaluate()
                .Select(e => $"{e.InstanceType}|{e.Value?.GetType().Name ?? "null"}|{e.Value}")
                .ToList();
        }
        catch (Exception ex)
        {
            return [$"threw:{ex.GetType().Name}"];
        }
    }
}
