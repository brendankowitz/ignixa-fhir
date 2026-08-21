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
    /// The marked wrappers each carry the result of a FHIRPath function or literal, which the
    /// specification defines in the System namespace. The unmarked ones each state what they carry
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

    /// <summary>
    /// Proves the census sees a producer that inherits its <see cref="IElement"/> declaration rather than
    /// making it, using fixtures in this assembly because the production assemblies contain no such shape
    /// and the census would go on passing if one appeared.
    /// </summary>
    /// <remarks>
    /// This is the mutation that exposed the hole. Before the base-type walk, a directly-implementing
    /// producer correctly failed the census while an inheriting one left both census tests green - a guard
    /// with a hole, which is worse than a known absence because the omission it exists to catch would have
    /// looked answered.
    /// </remarks>
    [Fact]
    public void GivenAProducerInheritingItsDeclaration_WhenElementImplementorsAreReflected_ThenTheCensusSeesIt()
    {
        // Arrange
        var testAssemblyPath = typeof(SystemValueElementDeclarationTests).Assembly.Location;

        // Act
        var implementors = GetElementImplementors(testAssemblyPath)
            .ToDictionary(implementor => implementor.FullName, StringComparer.Ordinal);

        // Assert
        implementors.ShouldContainKey(
            typeof(InheritedElementProducer).FullName!,
            "a producer inheriting IElement through an abstract base must still be censused, or the omission "
            + "guard has a hole exactly where a forgotten producer would sit.");
        implementors[typeof(InheritedElementProducer).FullName!].DeclaresSystemValue.ShouldBeFalse();
        implementors.ShouldContainKey(typeof(InheritedSystemValueProducer).FullName!);
        implementors[typeof(InheritedSystemValueProducer).FullName!].DeclaresSystemValue.ShouldBeTrue(
            "an inherited ISystemValueElement declaration must be seen too, or the declaration half of the "
            + "census is blind to the same shape.");
    }

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

    /// <summary>
    /// Whether <paramref name="type"/> implements <paramref name="interfaceName"/>, directly or through a
    /// base type.
    /// </summary>
    /// <remarks>
    /// The base-type walk is what makes this a census rather than a sample. The compiler emits an
    /// <c>InterfaceImpl</c> row only for interfaces named in a type's own declaration, so a concrete
    /// producer that inherits <see cref="IElement"/> from an abstract base declares nothing itself and is
    /// invisible without it. Verified by mutation: such a producer left both census tests green.
    /// </remarks>
    /// <returns>
    /// Interfaces short-circuit to <see langword="false"/>: their <c>Extends</c> row is not a readable
    /// type reference and reading it faults the metadata reader, and an interface is never a producer.
    /// Only bases defined in the same module are followed. A base in another assembly resolves to a
    /// <see cref="TypeReferenceHandle"/> that this reader cannot open, so a producer inheriting
    /// <see cref="IElement"/> across an assembly boundary is still out of reach. No such producer exists;
    /// the census would need a resolver spanning all scanned assemblies to cover one.
    /// </returns>
    private static bool ImplementsInterface(MetadataReader metadata, TypeDefinition type, string interfaceName)
    {
        var declaresIt = type.GetInterfaceImplementations()
            .Select(handle => metadata.GetInterfaceImplementation(handle).Interface)
            .Any(handle => InterfaceHasName(metadata, handle, interfaceName));

        if (declaresIt)
        {
            return true;
        }

        if ((type.Attributes & TypeAttributes.Interface) != 0)
        {
            return false;
        }

        var baseType = type.BaseType;

        return !baseType.IsNil
            && baseType.Kind == HandleKind.TypeDefinition
            && ImplementsInterface(metadata, metadata.GetTypeDefinition((TypeDefinitionHandle)baseType), interfaceName);
    }

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

    /// <summary>
    /// Declares <see cref="IElement"/> so its subclasses do not have to, which is the shape the census was
    /// blind to.
    /// </summary>
    private abstract class ElementProducerBase : IElement
    {
        public string Name => nameof(ElementProducerBase);

        public object? Value => null;

        public string InstanceType => "string";

        public string Location => Name;

        public IType? Type => null;

        public bool HasPrimitiveValue => false;

        public IReadOnlyList<IElement> Children(string? name = null) => [];

        public T? Meta<T>()
            where T : class => null;
    }

    private abstract class SystemValueProducerBase : ElementProducerBase, ISystemValueElement
    {
    }

    private sealed class InheritedElementProducer : ElementProducerBase
    {
    }

    private sealed class InheritedSystemValueProducer : SystemValueProducerBase
    {
    }
}
