/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * The defect this file exists for is producer omission: a wrapper around an engine-produced value that
 * nobody remembered to mark. It has landed twice. Behavioural tests cannot catch it, because a wrapper
 * no test reaches is exactly the one that gets forgotten - so the set of wrappers is asserted directly.
 */

using System.Reflection;
using Ignixa.Abstractions;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirPath.Evaluation;
using Ignixa.SqlOnFhir.Evaluation;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Requires every <see cref="IElement"/> implementor in the three evaluator assemblies to carry a
/// recorded decision about whether it wraps a System value, and requires its
/// <see cref="ISystemValueElement"/> declaration to match that decision.
/// </summary>
/// <remarks>
/// <para>
/// This is the guard against producer omission, and it is the only one. The behavioural tests in
/// <see cref="SystemValueTypeMatchingTests"/> pin the wrappers their expressions happen to reach;
/// removing <see cref="ISystemValueElement"/> from a wrapper no expression in the suite reaches leaves
/// the whole suite green. That was measured on <c>DateTimeFunctions.PrimitiveElement</c> - the entire
/// suite passed with the marker removed - and it is how six of eight producers came to be marked while
/// a comment asserted the set was closed.
/// </para>
/// <para>
/// What this test does and does not do: it fails when a new <see cref="IElement"/> implementor appears
/// without an entry, and it fails when an entry's declaration stops matching. It does not decide
/// whether a given wrapper holds a System value. That judgement stays with the author, who has to write
/// it down here; the test only refuses to let the question go unanswered.
/// </para>
/// <para>
/// Scope is the three assemblies that construct values during evaluation. Wrappers over a caller's
/// resource tree - <c>SchemaAwareElement</c> in Ignixa.Serialization, <c>IgnixaElementAdapter</c> in the
/// Firely extensions - are out of scope by construction: they carry a FHIR type from the schema and
/// never a System one, so there is no decision to record.
/// </para>
/// </remarks>
public class SystemValueElementDeclarationTests
{
    /// <summary>
    /// Every <see cref="IElement"/> implementor in scope, keyed by <see cref="Type.FullName"/>, with
    /// whether it wraps a System value and why.
    /// </summary>
    /// <remarks>
    /// The eight marked wrappers each carry the result of a FHIRPath function or literal, which the
    /// specification defines in the System namespace. The unmarked four each state what they carry
    /// instead, and for <c>QuantityElement</c> why its classification is a known divergence rather than
    /// a settled answer.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, (bool IsSystemValue, string Rationale)> Decisions =
        new Dictionary<string, (bool, string)>(StringComparer.Ordinal)
        {
            ["Ignixa.FhirPath.Evaluation.Functions.FunctionHelpers+PrimitiveElement"] =
                (true, "Carries the System.Boolean, System.Integer, System.Decimal and System.String results of the FHIRPath function library."),
            ["Ignixa.FhirPath.Evaluation.FhirPathEvaluator+PrimitiveElement"] =
                (true, "The interpreter's wrapper for function and operator results, all of them System values."),
            ["Ignixa.FhirPath.Evaluation.FhirPathDelegateCompiler+LiteralElement"] =
                (true, "The compiled path's counterpart to FhirPathEvaluator.PrimitiveElement; the two must classify identically."),
            ["Ignixa.FhirPath.Evaluation.Functions.DateTimeFunctions+PrimitiveElement"] =
                (true, "Carries System.DateTime, System.Date and System.Time results from now(), today() and the date arithmetic."),
            ["Ignixa.FhirPath.Evaluation.EvaluationContext+IndexElement"] =
                (true, "Backs $index, which the specification defines as a System.Integer."),
            ["Ignixa.FhirPath.Evaluation.EvaluationContext+StringElement"] =
                (true, "Backs the standard external constants %ucum, %sct, %loinc, %vs-* and %ext-*, all System.String."),
            ["Ignixa.FhirMappingLanguage.Evaluation.MappingEvaluator+PrimitiveElement"] =
                (true, "Carries the System values FHIRPath expressions inside a StructureMap produce."),
            ["Ignixa.SqlOnFhir.Evaluation.SqlOnFhirEvaluationVisitor+PrimitiveValueElement"] =
                (true, "Carries the System values a ViewDefinition's column expressions produce."),

            ["Ignixa.FhirPath.Evaluation.Functions.FunctionHelpers+QuantityElement"] =
                (false, "Unmarked, and a known divergence rather than a settled classification. A FHIRPath quantity literal is a System.Quantity, so `1 'mg' is System.Quantity` and `1.toQuantity() is System.Quantity` returning false - measured on R4 and R5, with `1 'mg' is FHIR.Quantity` true - contradicts the specification's type model. The same divergence is already pinned from the other side in Parity/FirelyVersusIgnixaDifferentialTests, where Firely types `1 'mg'` as System.Quantity and Ignixa as Quantity. Deferred, not accepted: it reports InstanceType \"Quantity\", which is neither in SystemOnlyTypes nor in CanonicalSystemPrimitiveSpellings, so marking it would leave unqualified `is Quantity` and `ofType(Quantity)` alone while flipping `is System.Quantity` from false to true and `is FHIR.Quantity` from true to false. It would not change what type() reports: CollectionFunctions.Type() maps \"quantity\" to FHIR/Quantity inside its isSystemLiteral branch, which is exactly what the unmarked default already yields. Measured blast radius of marking it: one failing test in 5,831 - this entry."),
            ["Ignixa.FhirPath.Evaluation.Functions.CollectionFunctions+TypeInfoElement"] =
                (false, "Describes a type rather than holding a value: its own InstanceType is the FHIR complex type ClassInfo/SimpleTypeInfo that type() returns."),
            ["Ignixa.FhirMappingLanguage.Evaluation.MappingEvaluator+MappingContextElement"] =
                (false, "A container for the named source and target variables in scope, not a value."),
            ["Ignixa.FhirMappingLanguage.Evaluation.MappingEvaluator+TempPropertyWrapper"] =
                (false, "Wraps a property of a resource under construction, so it carries that property's FHIR type."),
        };

    /// <summary>
    /// The assemblies that construct elements during evaluation, resolved through a type each one owns
    /// so the reference cannot be dropped without a compile error.
    /// </summary>
    private static IReadOnlyList<Assembly> ProducerAssemblies =>
    [
        typeof(FhirPathEvaluator).Assembly,
        typeof(MappingEvaluator).Assembly,
        typeof(SqlOnFhirEvaluator).Assembly,
    ];

    [Fact]
    public void GivenTheEvaluatorAssemblies_WhenElementImplementorsAreReflected_ThenEachHasARecordedDecision()
    {
        // Arrange
        var recorded = Decisions.Keys.OrderBy(name => name, StringComparer.Ordinal).ToList();

        // Act
        var implemented = ElementImplementors()
            .Select(type => type.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        // Assert
        implemented.ShouldBe(
            recorded,
            "every IElement implementor in these assemblies needs a recorded decision about whether it wraps a "
            + "System value. Add the new type to SystemValueElementDeclarationTests.Decisions with a rationale, "
            + "and declare ISystemValueElement on it if the answer is yes. This is the check that catches the "
            + "producer nobody remembered, which is the failure that shipped twice.");
    }

    [Fact]
    public void GivenARecordedDecision_WhenTheImplementorIsInspected_ThenItsDeclarationMatches()
    {
        // Arrange
        var implementors = ElementImplementors().ToDictionary(type => type.FullName!, StringComparer.Ordinal);

        // Act
        var mismatches = Decisions
            .Where(entry => implementors.TryGetValue(entry.Key, out var type)
                && typeof(ISystemValueElement).IsAssignableFrom(type) != entry.Value.IsSystemValue)
            .Select(entry => entry.Value.IsSystemValue
                ? $"{entry.Key} is recorded as a System value but does not declare ISystemValueElement: {entry.Value.Rationale}"
                : $"{entry.Key} declares ISystemValueElement but is recorded as not a System value: {entry.Value.Rationale}")
            .ToList();

        // Assert
        mismatches.ShouldBeEmpty(
            "a wrapper's ISystemValueElement declaration is what TypeMatcher reads; if it disagrees with the "
            + "decision recorded here, one of the two is wrong and the System/FHIR namespace split is unreliable.");
    }

    private static IEnumerable<Type> ElementImplementors() =>
        ProducerAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && typeof(IElement).IsAssignableFrom(type)
                && !type.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), inherit: false));
}
