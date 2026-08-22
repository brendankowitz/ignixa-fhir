/*
 * Copyright (c) 2026, Ignixa Contributors
 *
 * The defect this file guards against (#426) is an enumerated list that can go stale in one direction
 * without anyone noticing: SystemTypeConstructionAnalyzer.GetSystemPrimitiveRuntimeTypeName hand-maps a
 * function's declared [FhirPathFunction] ReturnType to the System type it constructs, and a ReturnType it
 * does not recognise falls through to "constructs no System value" whenever the schema happens to know
 * the name as a FHIR type. That fallthrough is correct for today's four fall-through values (ClassInfo,
 * Coding, Extension, Resource), but nothing stops a future function from declaring a known-FHIR-type
 * return that actually builds a System value: it would silently resolve to "constructs no System value",
 * a confident wrong answer rather than an admitted unknown, which is the dangerous direction for this
 * analysis to be wrong in.
 *
 * A census that only checks "every declared ReturnType has a recorded decision" would be a second copy
 * of the same list - it would agree with the analyzer by construction and catch nothing. So this file
 * also behaviourally cross-checks each decision: it builds a synthetic function declaring that ReturnType
 * and asks SystemTypeConstructionAnalyzer.Analyze what it actually does, rather than re-deriving the
 * answer from the same switch the production code uses. GetSystemPrimitiveRuntimeTypeName is private, but
 * Ignixa.FhirPath.Tests has InternalsVisibleTo, so the internal SymbolTable and SystemTypeConstructionAnalyzer
 * surface is exercised directly instead of being mirrored.
 */

using System.Reflection;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Analysis;
using Ignixa.FhirPath.Attributes;
using Ignixa.FhirPath.Evaluation.Functions;
using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Visitors;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Tests.Analysis;

/// <summary>
/// Requires every distinct <c>ReturnType</c> declared by a <see cref="FhirPathFunctionAttribute"/> in the
/// production function library to carry a recorded decision about what
/// <see cref="SystemTypeConstructionAnalyzer"/> does with it, and behaviourally cross-checks that decision
/// against the analyzer's actual verdict for a synthetic function declaring it.
/// </summary>
/// <remarks>
/// <para>
/// This is the guard against a future ReturnType silently falling through to a wrong answer. It does not
/// judge whether a given ReturnType <em>should</em> construct a System value - that judgement stays with
/// the author, recorded in <see cref="Decisions"/> - it only refuses to let a new keyword go unrecorded,
/// and refuses to let a recorded decision drift out of sync with what the analyzer actually does.
/// </para>
/// <para>
/// Scope is <c>typeof(FunctionHelpers).Assembly</c>, the assembly that declares every
/// <see cref="FhirPathFunctionAttribute"/> in the repository (verified: no other assembly declares one).
/// </para>
/// </remarks>
public class SystemTypeConstructionReturnTypeCensusTests
{
    private const BindingFlags DeclaredMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private const string ProbeFunctionName = "censusProbe";

    private const string KnownRootPropertyName = "census-known-root-property";

    private enum ConstructionVerdict
    {
        ConstructsSystemValue,
        ConstructsNoSystemValue,
        FailsOpenByDesign,
    }

    /// <summary>
    /// Every distinct declared <c>ReturnType</c> keyword, keyed case-insensitively to match how
    /// <see cref="SystemTypeConstructionAnalyzer"/> itself compares it, with the verdict class a synthetic
    /// function declaring it must produce and why.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (ConstructionVerdict Verdict, string Rationale)> Decisions =
        new Dictionary<string, (ConstructionVerdict, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["boolean"] =
                (ConstructionVerdict.ConstructsSystemValue,
                "GetSystemPrimitiveRuntimeTypeName maps BOOLEAN to \"boolean\"; declared by the is-type predicates and toBoolean()."),
            ["integer"] =
                (ConstructionVerdict.ConstructsSystemValue,
                "Maps INTEGER to \"integer\"; declared by toInteger(), length() and similar counting functions."),
            ["decimal"] =
                (ConstructionVerdict.ConstructsSystemValue,
                "Maps DECIMAL to \"decimal\"; declared by toDecimal() and the arithmetic-adjacent conversions."),
            ["string"] =
                (ConstructionVerdict.ConstructsSystemValue,
                "Maps STRING to \"string\"; declared by toString() and the string-producing conversions."),
            ["long"] =
                (ConstructionVerdict.ConstructsSystemValue,
                "Maps LONG to \"long\"; declared by toLong()."),
            ["quantity"] =
                (ConstructionVerdict.ConstructsSystemValue,
                "Maps QUANTITY to \"Quantity\" regardless of the declared casing (\"quantity\" or \"Quantity\" both appear in the "
                + "library and both upper-invariant to the same switch arm); declared by toQuantity()."),
            ["date"] =
                (ConstructionVerdict.ConstructsSystemValue,
                "Maps DATE to \"date\"; declared by toDate()."),
            ["dateTime"] =
                (ConstructionVerdict.ConstructsSystemValue,
                "Maps DATETIME to \"dateTime\"; declared by toDateTime() and now()."),
            ["time"] =
                (ConstructionVerdict.ConstructsSystemValue,
                "Maps TIME to \"time\"; declared by toTime()."),
            ["ClassInfo"] =
                (ConstructionVerdict.FailsOpenByDesign,
                "Declared by type(). \"ClassInfo\" names a reflection concept the FHIR schema does not register, so "
                + "IsKnownFhirType(\"ClassInfo\") is false and the fallthrough answers Any rather than guessing either namespace - "
                + "the deliberate fail-open resolution documented on the four fallthrough cases."),
            ["Coding"] =
                (ConstructionVerdict.ConstructsNoSystemValue,
                "Declared by translate(). \"Coding\" is a real FHIR complex type the schema registers, so IsKnownFhirType(\"Coding\") "
                + "is true and the fallthrough answers None: a navigated/constructed FHIR value, not a System one."),
            ["Extension"] =
                (ConstructionVerdict.ConstructsNoSystemValue,
                "Declared by extension(). \"Extension\" is a registered FHIR type, so the fallthrough answers None for the same "
                + "reason as Coding."),
            ["Resource"] =
                (ConstructionVerdict.ConstructsNoSystemValue,
                "Declared by resolve(). \"Resource\" is a registered FHIR type, so the fallthrough answers None for the same reason "
                + "as Coding."),
            ["context"] =
                (ConstructionVerdict.ConstructsNoSystemValue,
                "Declared by selectors such as where() and first(). \"context\" inherits whatever the focus already constructs; "
                + "probed here with a focus that is a known root property access, which AnalyzePropertyAccess classifies as None, "
                + "so the selector must pass that None through unchanged rather than guessing."),
            ["constructsFromContext"] =
                (ConstructionVerdict.FailsOpenByDesign,
                "Declared by abs(), round() and sum(). Builds a new value out of a non-empty focus; naming the constructed type "
                + "would mean mirroring the evaluator's per-function result matrix, so this is a deliberate over-approximation to "
                + "Any rather than an unnamed System guess, documented on AnalyzeConstructionFromContext."),
            ["boundaryOfContext"] =
                (ConstructionVerdict.FailsOpenByDesign,
                "Declared by lowBoundary() and highBoundary(). Routes through the same AnalyzeConstructionFromContext as "
                + "constructsFromContext and is Any for the same reason: naming the boundary type here would require mirroring "
                + "the evaluator's boundary matrix."),
            ["fromArgument"] =
                (ConstructionVerdict.ConstructsSystemValue,
                "Declared by select() and iif(). For a non-iif function the result is the union of its arguments' own "
                + "constructions; probed with a single System-integer-constructing argument, so the union is that named type."),
            ["any"] =
                (ConstructionVerdict.FailsOpenByDesign,
                "Declared by children(), descendants(), repeat() and repeatAll(). \"No specific return type inference\" - the "
                + "analyzer answers Any unconditionally, independent of focus or arguments, by design. repeat()/repeatAll() "
                + "moved here from \"context\" in #423: they return the projection, never the focus, so inheriting the focus's "
                + "construction named a value they cannot produce."),
        };

    private static readonly IFhirSchemaProvider Schema = FhirVersion.R4.GetSchemaProvider();

    [Fact]
    public void GivenTheFunctionLibrary_WhenReturnTypesAreReflected_ThenEachHasARecordedDecision()
    {
        // Arrange
        var recorded = Decisions.Keys
            .Select(name => name.ToUpperInvariant())
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        // Act
        var declared = DeclaredReturnTypes(typeof(FunctionHelpers).Assembly)
            .Select(name => name.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        // Assert
        declared.ShouldBe(
            recorded,
            "every distinct ReturnType declared by a [FhirPathFunction] in the production library needs a recorded verdict in "
            + "SystemTypeConstructionReturnTypeCensusTests.Decisions - state whether a function declaring it constructs a named "
            + "System value, constructs no System value, or is expected to fail open to Any, with a rationale. This is the check "
            + "that catches the keyword nobody taught GetSystemPrimitiveRuntimeTypeName about.");
    }

    [Fact]
    public void GivenARecordedDecision_WhenAProbeFunctionDeclaringItIsAnalyzed_ThenTheVerdictMatches()
    {
        // Arrange
        var mismatches = new List<string>();

        // Act
        foreach (var (returnType, (expectedVerdict, rationale)) in Decisions)
        {
            var actualVerdict = ClassifyVerdict(AnalyzeProbeFunction(returnType));
            if (actualVerdict != expectedVerdict)
            {
                mismatches.Add(
                    $"ReturnType \"{returnType}\" is recorded as {expectedVerdict} ({rationale}) but "
                    + $"SystemTypeConstructionAnalyzer.Analyze answered {actualVerdict} for a probe function declaring it.");
            }
        }

        // Assert
        mismatches.ShouldBeEmpty(
            "a recorded decision must match what SystemTypeConstructionAnalyzer actually does for a function declaring that "
            + "ReturnType, or the census is only a second copy of the same list it exists to check.");
    }

    /// <summary>
    /// Runs <see cref="SystemTypeConstructionAnalyzer.Analyze"/> against a synthetic function that declares
    /// <paramref name="returnType"/>, using a fresh <see cref="SymbolTable"/> so the probe cannot collide
    /// with any real function's registration.
    /// </summary>
    private static SystemTypeConstruction AnalyzeProbeFunction(string returnType)
    {
        var symbolTable = new SymbolTable(Schema);
        symbolTable.Add(new FunctionDefinition(ProbeFunctionName, declaredReturnType: returnType));

        var analyzer = new SystemTypeConstructionAnalyzer(
            symbolTable,
            propertyName => propertyName == KnownRootPropertyName);

        return analyzer.Analyze(BuildProbeExpression(returnType));
    }

    /// <summary>
    /// Builds the call site the probe function's dispatch branch needs to produce a deterministic verdict.
    /// </summary>
    /// <remarks>
    /// Every concrete System-primitive keyword, and the four FHIR-type fallthrough keywords, reach their
    /// verdict from the ReturnType string alone, so a bare call with no focus or arguments is enough. The
    /// sentinel keywords dispatch through the focus or arguments instead, so each is probed with the
    /// specific shape that makes its branch's answer deterministic rather than dependent on an unrelated
    /// default.
    /// </remarks>
    private static FunctionCallExpression BuildProbeExpression(string returnType) =>
        returnType.ToUpperInvariant() switch
        {
            "CONTEXT" =>
                new FunctionCallExpression(new PropertyAccessExpression(null, KnownRootPropertyName), ProbeFunctionName, []),
            "CONSTRUCTSFROMCONTEXT" or "BOUNDARYOFCONTEXT" =>
                new FunctionCallExpression(new ConstantExpression(5L), ProbeFunctionName, []),
            "FROMARGUMENT" =>
                new FunctionCallExpression(null, ProbeFunctionName, [new ConstantExpression(5L)]),
            _ =>
                new FunctionCallExpression(null, ProbeFunctionName, []),
        };

    private static ConstructionVerdict ClassifyVerdict(SystemTypeConstruction construction)
    {
        if (construction.MayConstructAny)
        {
            return ConstructionVerdict.FailsOpenByDesign;
        }

        return construction.TypeNames.Count > 0
            ? ConstructionVerdict.ConstructsSystemValue
            : ConstructionVerdict.ConstructsNoSystemValue;
    }

    private static IEnumerable<string> DeclaredReturnTypes(Assembly assembly) =>
        assembly.GetTypes()
            .SelectMany(type => type.GetMethods(DeclaredMembers))
            .Select(method => method.GetCustomAttribute<FhirPathFunctionAttribute>())
            .Where(attribute => attribute is not null)
            .Select(attribute => attribute!.ReturnType);
}
