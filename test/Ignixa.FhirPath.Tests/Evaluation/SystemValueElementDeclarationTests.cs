/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * The defect this file exists for is producer omission: a wrapper around an engine-produced value that
 * nobody remembered to mark. It has landed twice. Behavioural tests cannot catch it, because a wrapper
 * no test reaches is exactly the one that gets forgotten - so the set of wrappers is asserted directly.
 */

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Requires every <see cref="IElement"/> implementor in the production Ignixa assemblies copied beside the
/// test host to carry a
/// recorded decision about whether it wraps a System value, and requires its
/// <see cref="ISystemValueElement"/> declaration to match that decision.
/// </summary>
/// <remarks>
/// <para>
/// This is the guard against producer omission, and it is the only one. The behavioural tests in
/// <see cref="SystemValueTypeMatchingTests"/> pin the wrappers their expressions happen to reach;
/// removing <see cref="ISystemValueElement"/> from a wrapper no expression in the suite reaches leaves
/// the whole suite green. That was measured on <c>DateTimeFunctions.PrimitiveElement</c> - the entire
/// suite passed with the marker removed - and it is why all eight producers now need an explicit
/// decision rather than a comment asserting the set is closed.
/// </para>
/// <para>
/// What this test does and does not do: it fails when a new <see cref="IElement"/> implementor appears
/// without an entry, and it fails when an entry's declaration stops matching. It does not decide
/// whether a given wrapper holds a System value. That judgement stays with the author, who has to write
/// it down here; the test only refuses to let the question go unanswered.
/// </para>
/// <para>
/// Scope is every production <c>Ignixa.*</c> assembly copied beside the test host. This closes the omission gap for a
/// producer added outside the evaluator projects. Wrappers over a caller's resource tree are recorded as
/// non-System values when they implement <see cref="IElement"/>.
/// </para>
/// </remarks>
public class SystemValueElementDeclarationTests
{
    /// <summary>
    /// Every <see cref="IElement"/> implementor in scope, keyed by <see cref="Type.FullName"/>, with
    /// whether it wraps a System value and why.
    /// </summary>
    /// <remarks>
    /// The nine marked wrappers each carry the result of a FHIRPath function or literal, which the
    /// specification defines in the System namespace. The unmarked three each state what they carry
    /// instead.
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

            ["Ignixa.Serialization.SourceNodes.SchemaAwareElement"] =
                (false, "Wraps a caller-supplied source node using schema metadata, so it carries the source's FHIR type rather than an engine-produced System value."),
            ["Ignixa.FhirPath.Evaluation.Functions.FunctionHelpers+QuantityElement"] =
                (true, "Carries FHIRPath quantity literals and engine-produced quantity results, which are System.Quantity values. Resource-backed FHIR Quantity values use SchemaAwareElement instead. The marker changes only qualified type tests; unqualified Quantity matching is shared by both namespaces. What type() reports is a known divergence rather than a settled classification: the specification puts a quantity literal in the System namespace, so `(1 'mg').type()` should answer System/Quantity, and it still answers FHIR/Quantity because CollectionFunctions.Type special-cases \"quantity\" to FHIR inside its isSystemLiteral branch. Marking this producer therefore closed the `is` half of the divergence and left the type() half open, so `(1 'mg') is System.Quantity` is true while `(1 'mg').type()` says FHIR - the two now contradict each other. Pinned, and recorded as divergent, by SystemValueTypeMatchingTests.GivenQuantityLiteral_WhenItsTypeIsReported_ThenItRemainsFhirQuantity."),
            ["Ignixa.FhirPath.Evaluation.Functions.CollectionFunctions+TypeInfoElement"] =
                (false, "Describes a type rather than holding a value: its own InstanceType is the FHIR complex type ClassInfo/SimpleTypeInfo that type() returns."),
            ["Ignixa.FhirMappingLanguage.Evaluation.MappingEvaluator+MappingContextElement"] =
                (false, "A container for the named source and target variables in scope, not a value."),
            ["Ignixa.FhirMappingLanguage.Evaluation.MappingEvaluator+TempPropertyWrapper"] =
                (false, "Wraps a property of a resource under construction, so it carries that property's FHIR type."),
        };

    /// <summary>
    /// The referenced or loaded production Ignixa assemblies. Metadata scanning lets the census inspect
    /// assemblies copied beside the test host without requiring all of their dependencies to load.
    /// </summary>
    private static IReadOnlyList<string> ProducerAssemblyPaths =>
        Directory.EnumerateFiles(AppContext.BaseDirectory, "Ignixa.*.dll")
            .Where(path => !Path.GetFileNameWithoutExtension(path).EndsWith(".Tests", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void GivenIgnixaAssemblies_WhenElementImplementorsAreReflected_ThenEachHasARecordedDecision()
    {
        // Arrange
        var recorded = Decisions.Keys.OrderBy(name => name, StringComparer.Ordinal).ToList();

        // Act
        var implemented = ElementImplementors()
            .Select(element => element.FullName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        // Assert
        implemented.ShouldBe(
            recorded,
            "every IElement implementor in the production Ignixa assemblies copied beside the test host needs a recorded decision about whether it wraps a "
            + "System value. Add the new type to SystemValueElementDeclarationTests.Decisions with a rationale, "
            + "and declare ISystemValueElement on it if the answer is yes. This is the check that catches the "
            + "producer nobody remembered, which is the failure that shipped twice.");
    }

    [Fact]
    public void GivenARecordedDecision_WhenTheImplementorIsInspected_ThenItsDeclarationMatches()
    {
        // Arrange
        var implementors = ElementImplementors().ToDictionary(element => element.FullName, StringComparer.Ordinal);

        // Act
        var mismatches = Decisions
            .Where(entry => implementors.TryGetValue(entry.Key, out var implementor)
                && implementor.DeclaresSystemValue != entry.Value.IsSystemValue)
            .Select(entry => entry.Value.IsSystemValue
                ? $"{entry.Key} is recorded as a System value but does not declare ISystemValueElement: {entry.Value.Rationale}"
                : $"{entry.Key} declares ISystemValueElement but is recorded as not a System value: {entry.Value.Rationale}")
            .ToList();

        // Assert
        mismatches.ShouldBeEmpty(
            "a wrapper's ISystemValueElement declaration is what TypeMatcher reads; if it disagrees with the "
            + "decision recorded here, one of the two is wrong and the System/FHIR namespace split is unreliable.");
    }

    private static IEnumerable<(string FullName, bool DeclaresSystemValue)> ElementImplementors() =>
        ProducerAssemblyPaths
            .SelectMany(GetElementImplementors);

    private static IEnumerable<(string FullName, bool DeclaresSystemValue)> GetElementImplementors(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();

        foreach (var handle in metadata.TypeDefinitions)
        {
            var type = metadata.GetTypeDefinition(handle);
            if ((type.Attributes & (TypeAttributes.Interface | TypeAttributes.Abstract)) != 0
                || !ImplementsInterface(metadata, type, "IElement"))
            {
                continue;
            }

            yield return (
                GetTypeFullName(metadata, handle),
                ImplementsInterface(metadata, type, "ISystemValueElement"));
        }
    }

    private static bool ImplementsInterface(MetadataReader metadata, TypeDefinition type, string interfaceName) =>
        type.GetInterfaceImplementations()
            .Select(handle => metadata.GetInterfaceImplementation(handle).Interface)
            .Any(handle => InterfaceHasName(metadata, handle, interfaceName));

    private static bool InterfaceHasName(MetadataReader metadata, EntityHandle handle, string interfaceName)
    {
        var (namespaceName, typeName) = GetTypeName(metadata, handle);
        if (namespaceName == "Ignixa.Abstractions" && typeName == interfaceName)
        {
            return true;
        }

        return handle.Kind == HandleKind.TypeDefinition
            && ImplementsInterface(metadata, metadata.GetTypeDefinition((TypeDefinitionHandle)handle), interfaceName);
    }

    private static (string NamespaceName, string TypeName) GetTypeName(MetadataReader metadata, EntityHandle handle) =>
        handle.Kind switch
        {
            HandleKind.TypeDefinition => GetTypeName(metadata, metadata.GetTypeDefinition((TypeDefinitionHandle)handle)),
            HandleKind.TypeReference => GetTypeName(metadata, metadata.GetTypeReference((TypeReferenceHandle)handle)),
            _ => (string.Empty, string.Empty),
        };

    private static (string NamespaceName, string TypeName) GetTypeName(MetadataReader metadata, TypeDefinition type) =>
        (metadata.GetString(type.Namespace), metadata.GetString(type.Name));

    private static (string NamespaceName, string TypeName) GetTypeName(MetadataReader metadata, TypeReference type) =>
        (metadata.GetString(type.Namespace), metadata.GetString(type.Name));

    private static string GetTypeFullName(MetadataReader metadata, TypeDefinitionHandle handle)
    {
        var type = metadata.GetTypeDefinition(handle);
        var declaringType = type.GetDeclaringType();
        var typeName = metadata.GetString(type.Name);

        return declaringType.IsNil
            ? $"{metadata.GetString(type.Namespace)}.{typeName}"
            : $"{GetTypeFullName(metadata, declaringType)}+{typeName}";
    }
}
