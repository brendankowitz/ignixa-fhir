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
/// <para>
/// The converse holds for the values that reach the type operators without a function call -
/// <c>$index</c> and the standard external constants. <c>TryCompile</c> declines every expression
/// that produces one, so a differential comparison is satisfied whatever the answer is, and the same
/// misclassification went unnoticed there through a whole round of review. Those are asserted
/// single-path against their expected values, per version.
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

    private const string QuantityObservationJson = """
    {
      "resourceType": "Observation",
      "id": "quantity-example",
      "status": "final",
      "code": { "text": "test" },
      "valueQuantity": {
        "value": 1,
        "unit": "mg",
        "system": "http://unitsofmeasure.org",
        "code": "mg"
      }
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

    /// <summary>
    /// The standard external constants, per version, with the value each must still yield when
    /// selected with its System spelling.
    /// </summary>
    /// <remarks>
    /// Every one of these is a <c>System.String</c> defined by the FHIRPath specification, so
    /// <c>ofType(String)</c> selects it on every version - the R5 gate withdraws the FHIR aliases, not
    /// the System namespace itself. They regressed to empty on R5 and R6 because the element backing
    /// them did not declare <see cref="ISystemValueElement"/>.
    /// </remarks>
    public static TheoryData<FhirVersion, string, string> StandardConstantSelections
    {
        get
        {
            var data = new TheoryData<FhirVersion, string, string>();
            foreach (var version in AllVersions)
            {
                foreach (var (constant, expected) in StandardConstantValues)
                {
                    data.Add(version, constant, expected);
                }
            }

            return data;
        }
    }

    /// <summary>
    /// The same constants without their values, for the assertions that only need the identity.
    /// </summary>
    public static TheoryData<FhirVersion, string> StandardConstants
    {
        get
        {
            var data = new TheoryData<FhirVersion, string>();
            foreach (var version in AllVersions)
            {
                foreach (var constant in StandardConstantNames)
                {
                    data.Add(version, constant);
                }
            }

            return data;
        }
    }

    /// <summary>
    /// Asserted single-path, and against the value rather than against the other path, because
    /// <see cref="FhirPathDelegateCompiler.TryCompile"/> declines these expressions: a differential
    /// comparison agrees by construction whatever the answer is, which is how they regressed to empty
    /// on R5 and R6 with the versioned differential suite green.
    /// </summary>
    [Theory]
    [MemberData(nameof(StandardConstantSelections))]
    public void GivenAStandardConstantOnAnyVersion_WhenSelectedWithTheSystemSpelling_ThenTheStringSurvives(
        FhirVersion version,
        string constant,
        string expected)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(ObservationJson).ToElement(schema);

        // Act
        var selected = Interpret(element, $"{constant}.ofType(String)", schema);

        // Assert
        selected.ShouldHaveSingleItem(
            $"'{constant}' is a System.String constant, so ofType(String) selects it on {version}").Value.ShouldBe(expected);
    }

    [Theory]
    [MemberData(nameof(StandardConstants))]
    public void GivenAStandardConstantOnAnyVersion_WhenTypeTested_ThenItIsASystemString(
        FhirVersion version,
        string constant)
    {
        // The type TEST gate is not version dependent, so this was false on all five versions rather
        // than only the two the cast gate reaches.

        // Arrange
        var schema = version.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(ObservationJson).ToElement(schema);

        // Act
        var isString = Interpret(element, $"{constant} is String", schema);

        // Assert
        isString.ShouldHaveSingleItem().Value.ShouldBe(true);
    }

    [Theory]
    [InlineData(FhirVersion.Stu3)]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    [InlineData(FhirVersion.R6)]
    public void GivenIndexOnAnyVersion_WhenSelectedWithTheSystemSpelling_ThenTheIntegersSurvive(
        FhirVersion version)
    {
        // $index is a System.Integer the evaluator produces, so ofType(Integer) keeps both positions.
        // Also single-path: TryCompile declines select().

        // Arrange
        var schema = version.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(ObservationJson).ToElement(schema);

        // Act
        var indices = Interpret(element, "(1 | 2).select($index).ofType(Integer)", schema);

        // Assert
        indices.Select(i => i.Value).ShouldBe([0, 1]);
    }

    [Theory]
    [InlineData(FhirVersion.Stu3)]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    [InlineData(FhirVersion.R6)]
    public void GivenIndexOnAnyVersion_WhenTypeTested_ThenItIsASystemInteger(FhirVersion version)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(ObservationJson).ToElement(schema);

        // Act
        var isInteger = Interpret(element, "(1 | 2).select($index is Integer)", schema);

        // Assert
        isInteger.Select(i => i.Value).ShouldBe([true, true]);
    }

    [Theory]
    [InlineData(FhirVersion.R4, "1 'mg'")]
    [InlineData(FhirVersion.R4, "1.toQuantity()")]
    [InlineData(FhirVersion.R5, "1 'mg'")]
    [InlineData(FhirVersion.R5, "1.toQuantity()")]
    public void GivenEngineProducedQuantity_WhenTypeTestedWithQualifiedNamespaces_ThenOnlySystemQuantityMatches(
        FhirVersion version,
        string quantityExpression)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(ObservationJson).ToElement(schema);

        // Act
        var isSystemQuantity = Interpret(element, $"({quantityExpression}) is System.Quantity", schema);
        var isFhirQuantity = Interpret(element, $"({quantityExpression}) is FHIR.Quantity", schema);

        // Assert
        isSystemQuantity.ShouldHaveSingleItem().Value.ShouldBe(true);
        isFhirQuantity.ShouldHaveSingleItem().Value.ShouldBe(false);
    }

    [Theory]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R5)]
    public void GivenResourceBackedQuantity_WhenTypeTestedWithQualifiedNamespaces_ThenOnlyFhirQuantityMatches(
        FhirVersion version)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(QuantityObservationJson).ToElement(schema);

        // Act
        var isSystemQuantity = Interpret(element, "value is System.Quantity", schema);
        var isFhirQuantity = Interpret(element, "value is FHIR.Quantity", schema);

        // Assert
        isSystemQuantity.ShouldHaveSingleItem().Value.ShouldBe(false);
        isFhirQuantity.ShouldHaveSingleItem().Value.ShouldBe(true);
    }

    [Fact]
    public void GivenSystemAndFhirQuantities_WhenSelectedWithoutNamespace_ThenBothMatchQuantity()
    {
        // Arrange
        var schema = FhirVersion.R5.GetSchemaProvider();
        var literalSubject = ResourceJsonNode.Parse(ObservationJson).ToElement(schema);
        var resourceSubject = ResourceJsonNode.Parse(QuantityObservationJson).ToElement(schema);

        // Act
        var literalIsQuantity = Interpret(literalSubject, "1 'mg' is Quantity", schema);
        var literalOfType = Interpret(literalSubject, "(1 'mg').ofType(Quantity)", schema);
        var resourceIsQuantity = Interpret(resourceSubject, "value is Quantity", schema);
        var resourceOfType = Interpret(resourceSubject, "value.ofType(Quantity)", schema);

        // Assert
        literalIsQuantity.ShouldHaveSingleItem().Value.ShouldBe(true);
        literalOfType.ShouldHaveSingleItem();
        resourceIsQuantity.ShouldHaveSingleItem().Value.ShouldBe(true);
        resourceOfType.ShouldHaveSingleItem();
    }

    /// <summary>
    /// Pins what <c>type()</c> answers for a quantity literal. That answer is a known deviation from
    /// the specification, not a settled classification.
    /// </summary>
    /// <remarks>
    /// The specification puts a quantity literal in the System namespace, so this should answer
    /// System/Quantity. Marking <c>QuantityElement</c> as a System value closed the <c>is</c> half of
    /// that divergence and left this half open, so <c>(1 'mg') is System.Quantity</c> is now true while
    /// <c>(1 'mg').type()</c> still says FHIR/Quantity - the two contradict each other. The cause is the
    /// <c>"quantity"</c> case in <c>CollectionFunctions.Type</c>, which maps to FHIR from inside the
    /// <c>isSystemLiteral</c> branch. Correcting it is a separate spec fix needing its own evidence;
    /// this guard exists so that correction is deliberate and visible rather than incidental.
    /// </remarks>
    [Fact]
    public void GivenQuantityLiteral_WhenItsTypeIsReported_ThenItRemainsFhirQuantity()
    {
        // Arrange
        var schema = FhirVersion.R5.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(ObservationJson).ToElement(schema);

        // Act
        var typeNamespace = Interpret(element, "(1 'mg').type().namespace", schema);
        var typeName = Interpret(element, "(1 'mg').type().name", schema);

        // Assert
        typeNamespace.ShouldHaveSingleItem().Value.ShouldBe("FHIR");
        typeName.ShouldHaveSingleItem().Value.ShouldBe("Quantity");
    }

    private static IReadOnlyList<FhirVersion> AllVersions =>
        [FhirVersion.Stu3, FhirVersion.R4, FhirVersion.R4B, FhirVersion.R5, FhirVersion.R6];

    /// <summary>
    /// The three enumerated standard constants plus one representative from each of the two families
    /// <see cref="EvaluationContext"/> expands by rule.
    /// </summary>
    private static IReadOnlyList<(string Constant, string Value)> StandardConstantValues =>
    [
        ("%ucum", "http://unitsofmeasure.org"),
        ("%sct", "http://snomed.info/sct"),
        ("%loinc", "http://loinc.org"),
        ("%`vs-administrative-gender`", "http://hl7.org/fhir/ValueSet/administrative-gender"),
        ("%`ext-patient-birthTime`", "http://hl7.org/fhir/StructureDefinition/patient-birthTime"),
    ];

    private static IEnumerable<string> StandardConstantNames =>
        StandardConstantValues.Select(entry => entry.Constant);

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
