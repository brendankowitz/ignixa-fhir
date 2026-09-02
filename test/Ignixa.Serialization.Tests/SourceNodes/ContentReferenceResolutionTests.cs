// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Shouldly;
using Xunit;

namespace Ignixa.Serialization.Tests.SourceNodes;

/// <summary>
/// Pins the schema invariant that <c>SchemaAwareElement.ComputeChildResolution</c>'s
/// <c>ContentReference</c> branch depends on, and the parse it uses to get there.
/// </summary>
/// <remarks>
/// Issue #454's fix keys recursion detection on <c>ITypeExtended.ContentReference</c> and resolves the
/// target through <c>ISchema.GetTypeDefinition</c>. When that lookup misses, the element falls through
/// to <c>DeriveInstanceType</c>, and a ContentReference element carries no declared type - so it types
/// as its own bare element name (<c>"item"</c>), which is not a FHIR type and matches no converter.
/// That is the #454 failure shape one level down: silent, and indistinguishable from "no matches" at
/// the API.
/// <para>
/// The fix's safety argument was that every ContentReference in the generated schemas resolves. That
/// was true when measured and enforced by nothing - the schemas are generated from published packages
/// and the generator emits <c>contentReference</c> verbatim, so a regeneration could falsify it
/// silently while every test stayed green. These tests make the claim fail out loud instead.
/// </para>
/// </remarks>
public class ContentReferenceResolutionTests
{
    private static readonly FhirVersion[] Shipped =
    [
        FhirVersion.Stu3, FhirVersion.R4, FhirVersion.R4B, FhirVersion.R5, FhirVersion.R6,
    ];

    public static TheoryData<FhirVersion> ShippedVersions
    {
        get
        {
            var data = new TheoryData<FhirVersion>();
            foreach (var version in Shipped)
            {
                data.Add(version);
            }

            return data;
        }
    }

    /// <summary>
    /// Every <c>ContentReference</c> a shipped schema declares must resolve through the same parse
    /// production uses. A version is asserted to declare some, so the walk cannot pass by finding
    /// nothing.
    /// </summary>
    [Theory]
    [MemberData(nameof(ShippedVersions))]
    public void GivenAShippedSchema_WhenEveryContentReferenceIsResolved_ThenNoneIsLeftDangling(FhirVersion version)
    {
        var provider = version.GetSchemaProvider();
        var declared = new List<string>();
        var dangling = new List<string>();

        foreach (var (parent, child) in WalkChildElements(provider))
        {
            if (child is not ITypeExtended { ContentReference: { Length: > 1 } contentReference })
            {
                continue;
            }

            var qualifiedPath = $"{parent.Info.Name}.{child.Info.Name}";
            declared.Add(qualifiedPath);

            // The production parse, deliberately restated rather than called: a test that runs the
            // implementation cannot fail when the implementation is what broke.
            var targetTypeName = contentReference[(contentReference.IndexOf('#', StringComparison.Ordinal) + 1)..];
            if (provider.GetTypeDefinition(targetTypeName) == null)
            {
                dangling.Add($"{qualifiedPath} -> {contentReference}");
            }
        }

        declared.ShouldNotBeEmpty($"{version} declares no ContentReference at all, so this walk proved nothing");
        dangling.ShouldBeEmpty(
            $"{version} declares {declared.Count} ContentReferences and {dangling.Count} do not resolve. Each one "
            + $"types as its own bare element name and indexes nothing: {string.Join(", ", dangling)}");
    }

    /// <summary>
    /// A floor, not an exact pin, for the reason the parity corpus gives for its own floors: a count
    /// that falls is either a schema that genuinely dropped sites or a walk that stopped reaching them,
    /// and the two must not be indistinguishable. 307 is the total measured across the five providers
    /// when #454 landed.
    /// </summary>
    [Fact]
    public void GivenAllShippedSchemas_WhenContentReferencesAreCounted_ThenTheWalkStillReachesThemAll()
    {
        var total = Shipped
            .Sum(version => WalkChildElements(version.GetSchemaProvider())
                .Count(pair => pair.Child is ITypeExtended { ContentReference: { Length: > 1 } }));

        total.ShouldBeGreaterThanOrEqualTo(307);
    }

    /// <summary>
    /// <c>ElementDefinition.contentReference</c> is a <c>uri</c>, and the absolute form is as legal as
    /// the local fragment. Package-backed schemas carry whichever spelling the IG published -
    /// <c>TypeSnapshotProjector</c> passes the value through verbatim - and this repository's own test
    /// corpus contains the absolute form. Parsing with <c>TrimStart('#')</c> would leave a URL intact,
    /// miss the lookup, and silently type the element as its own name.
    /// </summary>
    [Theory]
    [InlineData("#Questionnaire.item")]
    [InlineData("http://hl7.org/fhir/StructureDefinition/Questionnaire#Questionnaire.item")]
    public void GivenEitherLegalContentReferenceSpelling_WhenNavigated_ThenTheChildResolvesToTheSameTarget(string contentReference)
    {
        var schema = new RespelledContentReferenceSchema(FhirVersion.R4.GetSchemaProvider(), contentReference);

        var questionnaireJson = """
        {
          "resourceType": "Questionnaire",
          "id": "q1",
          "status": "active",
          "item": [
            { "linkId": "1", "type": "group", "item": [ { "linkId": "1.1", "type": "string" } ] }
          ]
        }
        """;

        var element = ResourceJsonNode.Parse(questionnaireJson).ToElement(schema);
        var nestedItem = element.Children("item").Single().Children("item").Single();

        nestedItem.InstanceType.ShouldBe("Questionnaire.Item");
    }

    /// <summary>
    /// Walks every reachable (parent, child element) pair in a schema. Backbones are reached by their
    /// qualified name and datatypes by their declared one, so an element nested under either is
    /// visited - a walk that followed only backbones would miss every ContentReference declared on a
    /// datatype.
    /// </summary>
    private static IEnumerable<(IType Parent, IType Child)> WalkChildElements(IFhirSchemaProvider provider)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<IType>();

        foreach (var resourceTypeName in provider.ResourceTypeNames)
        {
            var definition = provider.GetTypeDefinition(resourceTypeName);
            if (definition != null && seen.Add(definition.Info.Name))
            {
                queue.Enqueue(definition);
            }
        }

        while (queue.Count > 0)
        {
            var parent = queue.Dequeue();
            foreach (var child in parent.Children)
            {
                yield return (parent, child);

                var qualified = provider.GetTypeDefinition($"{parent.Info.Name}.{child.Info.Name}");
                var declared = (child as ITypeExtended)?.DefaultTypeName is { Length: > 0 } typeName
                    ? provider.GetTypeDefinition(typeName)
                    : null;

                foreach (var next in new[] { qualified, declared })
                {
                    if (next != null && seen.Add(next.Info.Name))
                    {
                        queue.Enqueue(next);
                    }
                }
            }
        }
    }

    /// <summary>
    /// An <see cref="ISchema"/> decorator that re-spells the <c>ContentReference</c> on
    /// <c>Questionnaire.item</c>'s recursive child, so one navigation can be driven with each legal
    /// spelling against otherwise real schema data.
    /// </summary>
    private sealed class RespelledContentReferenceSchema(IFhirSchemaProvider inner, string contentReference) : ISchema
    {
        public FhirVersion Version => inner.Version;

        public bool IsKnownType(string typeName) => inner.IsKnownType(typeName);

        public IType? GetTypeDefinition(string typeName)
        {
            var definition = inner.GetTypeDefinition(typeName);
            return definition != null && string.Equals(definition.Info.Name, "Questionnaire.Item", StringComparison.Ordinal)
                ? new RespelledParent(definition, contentReference)
                : definition;
        }
    }

    private sealed class RespelledParent(IType inner, string contentReference) : IType
    {
        public TypeInfo Info => inner.Info;

        public bool IsCollection => inner.IsCollection;

        public bool IsRequired => inner.IsRequired;

        public bool InSummary => inner.InSummary;

        public int Order => inner.Order;

        public IReadOnlyList<IType> Children { get; } = inner.Children
            .Select(child => string.Equals(child.Info.Name, "item", StringComparison.Ordinal)
                ? new RespelledChild((ITypeExtended)child, contentReference)
                : child)
            .ToArray();
    }

    private sealed class RespelledChild(ITypeExtended inner, string contentReference) : ITypeExtended
    {
        public TypeInfo Info => inner.Info;

        public bool IsCollection => inner.IsCollection;

        public bool IsRequired => inner.IsRequired;

        public bool InSummary => inner.InSummary;

        public int Order => inner.Order;

        public IReadOnlyList<IType> Children => inner.Children;

        public int Min => inner.Min;

        public string Max => inner.Max;

        public IReadOnlyList<IConstraint> Constraints => inner.Constraints;

        public IBinding? Binding => inner.Binding;

        public IReadOnlyList<ITypeReference> Types => inner.Types;

        public IReadOnlyList<string> ReferenceTargets => inner.ReferenceTargets;

        public string? DefaultTypeName => inner.DefaultTypeName;

        public object? FixedValue => inner.FixedValue;

        public object? PatternValue => inner.PatternValue;

        public string? ContentReference => contentReference;
    }
}
