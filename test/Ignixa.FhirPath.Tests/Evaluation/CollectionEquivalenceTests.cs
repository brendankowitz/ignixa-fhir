/*
 * Copyright (c) 2025, Ignixa Contributors
 */

using Ignixa.Abstractions;
using Ignixa.Extensions.FirelySdk;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.FhirPath.Tests.Evaluation.Parity;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class CollectionEquivalenceTests
{
    private const string QuantityAndCodeBundleJson = """
    {
      "resourceType": "Bundle",
      "type": "collection",
      "entry": [
        {
          "resource": {
            "resourceType": "Observation",
            "id": "o1",
            "status": "final",
            "code": { "text": "dose" },
            "valueQuantity": {
              "value": 1000,
              "unit": "mg",
              "system": "http://unitsofmeasure.org",
              "code": "mg"
            }
          }
        },
        {
          "resource": {
            "resourceType": "Observation",
            "id": "o2",
            "status": "final",
            "code": { "text": "distance" },
            "valueQuantity": {
              "value": 2,
              "unit": "m",
              "system": "http://unitsofmeasure.org",
              "code": "m"
            }
          }
        }
      ]
    }
    """;

    private const string EquivalentCodeableConceptBundleJson = """
    {
      "resourceType": "Bundle",
      "type": "collection",
      "entry": [
        {
          "resource": {
            "resourceType": "Observation",
            "id": "o1",
            "status": "final",
            "code": { "text": "alpha" },
            "valueCodeableConcept": { "text": "alpha" }
          }
        },
        {
          "resource": {
            "resourceType": "Observation",
            "id": "o2",
            "status": "final",
            "code": { "text": "beta" },
            "valueCodeableConcept": { "text": "beta" }
          }
        }
      ]
    }
    """;

    private const string DifferentCodeableConceptBundleJson = """
    {
      "resourceType": "Bundle",
      "type": "collection",
      "entry": [
        {
          "resource": {
            "resourceType": "Observation",
            "id": "o1",
            "status": "final",
            "code": { "text": "alpha" },
            "valueCodeableConcept": { "text": "alpha" }
          }
        },
        {
          "resource": {
            "resourceType": "Observation",
            "id": "o2",
            "status": "final",
            "code": { "text": "beta" },
            "valueCodeableConcept": { "text": "gamma" }
          }
        }
      ]
    }
    """;

    private const string ConvertibleQuantityBundleJson = """
    {
      "resourceType": "Bundle",
      "type": "collection",
      "entry": [
        {
          "resource": {
            "resourceType": "Observation",
            "id": "left-mass",
            "status": "final",
            "code": { "text": "mass" },
            "valueQuantity": {
              "value": 1000000,
              "unit": "mg",
              "system": "http://unitsofmeasure.org",
              "code": "mg"
            }
          }
        },
        {
          "resource": {
            "resourceType": "Observation",
            "id": "left-distance",
            "status": "final",
            "code": { "text": "distance" },
            "valueQuantity": {
              "value": 2,
              "unit": "m",
              "system": "http://unitsofmeasure.org",
              "code": "m"
            }
          }
        },
        {
          "resource": {
            "resourceType": "Observation",
            "id": "right-distance",
            "status": "final",
            "code": { "text": "distance" },
            "valueQuantity": {
              "value": 200,
              "unit": "cm",
              "system": "http://unitsofmeasure.org",
              "code": "cm"
            }
          }
        },
        {
          "resource": {
            "resourceType": "Observation",
            "id": "right-mass",
            "status": "final",
            "code": { "text": "mass" },
            "valueQuantity": {
              "value": 1,
              "unit": "kg",
              "system": "http://unitsofmeasure.org",
              "code": "kg"
            }
          }
        }
      ]
    }
    """;

    private const string BackboneFamilyBundleJson = """
    {
      "resourceType": "Bundle",
      "type": "collection",
      "entry": [
        {
          "resource": {
            "resourceType": "CodeSystem",
            "id": "codes",
            "status": "active",
            "content": "complete",
            "concept": [
              {
                "code": "same",
                "display": "Same"
              }
            ]
          }
        },
        {
          "resource": {
            "resourceType": "ValueSet",
            "id": "values",
            "status": "active",
            "expansion": {
              "timestamp": "2026-08-19T00:00:00Z",
              "contains": [
                {
                  "code": "same",
                  "display": "Same"
                }
              ]
            }
          }
        }
      ]
    }
    """;

    private const string NonTransitiveQuantityBundleJson = """
    {
      "resourceType": "Bundle",
      "type": "collection",
      "entry": [
        {
          "resource": {
            "resourceType": "Observation",
            "id": "left-coarse",
            "status": "final",
            "code": { "text": "length" },
            "valueQuantity": {
              "value": 1.0,
              "unit": "m",
              "system": "http://unitsofmeasure.org",
              "code": "m"
            }
          }
        },
        {
          "resource": {
            "resourceType": "Observation",
            "id": "left-fine",
            "status": "final",
            "code": { "text": "length" },
            "valueQuantity": {
              "value": 1.04,
              "unit": "m",
              "system": "http://unitsofmeasure.org",
              "code": "m"
            }
          }
        },
        {
          "resource": {
            "resourceType": "Observation",
            "id": "right-metres",
            "status": "final",
            "code": { "text": "length" },
            "valueQuantity": {
              "value": 1.0,
              "unit": "m",
              "system": "http://unitsofmeasure.org",
              "code": "m"
            }
          }
        },
        {
          "resource": {
            "resourceType": "Observation",
            "id": "right-centimetres",
            "status": "final",
            "code": { "text": "length" },
            "valueQuantity": {
              "value": 96,
              "unit": "cm",
              "system": "http://unitsofmeasure.org",
              "code": "cm"
            }
          }
        }
      ]
    }
    """;

    private static readonly IFhirSchemaProvider Schema = FhirVersion.R4.GetSchemaProvider();

    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    [Fact]
    public void GivenQuantityAndCodeableConceptCollections_WhenComparedForEquivalence_ThenTheyAreNotEquivalent()
    {
        var bundle = ParseNative(QuantityAndCodeBundleJson);

        var result = EvaluateBoolean(
            bundle,
            "Bundle.entry.resource.value ~ Bundle.entry.resource.code");

        result.ShouldBeFalse();
    }

    [Fact]
    public void GivenEquivalentComplexCollectionsInDifferentOrders_WhenComparedForEquivalence_ThenTheyAreEquivalent()
    {
        var bundle = ParseNative(EquivalentCodeableConceptBundleJson);

        var result = EvaluateBoolean(
            bundle,
            "Bundle.entry.resource.code ~ (Bundle.entry.resource.value.last() | Bundle.entry.resource.value.first())");

        result.ShouldBeTrue();
    }

    [Fact]
    public void GivenComplexCollectionsDifferingInOneChild_WhenComparedForEquivalence_ThenTheyAreNotEquivalent()
    {
        var bundle = ParseNative(DifferentCodeableConceptBundleJson);

        var result = EvaluateBoolean(
            bundle,
            "Bundle.entry.resource.code ~ Bundle.entry.resource.value");

        result.ShouldBeFalse();
    }

    [Fact]
    public void GivenConvertibleQuantityCollectionsInDifferentOrders_WhenComparedForEquivalence_ThenTheyAreEquivalent()
    {
        var bundle = ParseNative(ConvertibleQuantityBundleJson);

        var result = EvaluateBoolean(
            bundle,
            "Bundle.entry.take(2).resource.value ~ Bundle.entry.skip(2).resource.value");

        result.ShouldBeTrue();
    }

    // Equivalence rounds decimals to the LESSER of the two stated precisions, which makes the relation
    // non-transitive: 1.0 ~ 0.96 (both round to 1.0 at one decimal place) and 1.04 ~ 1.0 (both round to
    // 1.0 at one place), yet 1.04 ~ 0.96 is false (two places apply, and they differ). The spec-correct
    // answer for the collections is therefore true, because the one-to-one pairing 1.0-0.96 and
    // 1.04-1.0 exists and makes every pair equivalent - and the definition asks only whether SOME such
    // pairing exists, not whether a particular traversal finds one.
    //
    // A first-fit matcher pairs 1.0 with 1.0, leaving 1.04 to fail against 0.96, so it answers false for
    // one ordering of the right operand and true for the other. Asserting both orderings together is what
    // pins the order-independence; asserting the value true is what pins the answer being the correct one
    // rather than merely a consistent one.
    //
    // What varies below is item order within the right operand - the operands themselves are never
    // swapped. Do not read these as commutativity coverage: swapping the operands is a separate property
    // and it does not hold today (1 'm' ~ 104 'cm' is true while 104 'cm' ~ 1 'm' is false), which is
    // tracked as a parity-corpus finding.
    [Fact]
    public void GivenNonTransitivelyEquivalentDecimalCollections_WhenTheRightOperandIsReordered_ThenBothAreEquivalent()
    {
        var root = new ScalarRoot();

        var firstOrder = EvaluateBoolean(root, "(1.0 | 1.04) ~ (1.0 | 0.96)");
        var reversedOrder = EvaluateBoolean(root, "(1.0 | 1.04) ~ (0.96 | 1.0)");

        firstOrder.ShouldBe(reversedOrder);
        firstOrder.ShouldBeTrue();
    }

    // !~ negates the same matching, so it inherits any order-dependence in it rather than having one of
    // its own. Covered separately because it is the operator search-parameter authors would reach for.
    [Fact]
    public void GivenNonTransitivelyEquivalentDecimalCollections_WhenNegatedWithTheRightOperandReordered_ThenNeitherIsInequivalent()
    {
        var root = new ScalarRoot();

        var firstOrder = EvaluateBoolean(root, "(1.0 | 1.04) !~ (1.0 | 0.96)");
        var reversedOrder = EvaluateBoolean(root, "(1.0 | 1.04) !~ (0.96 | 1.0)");

        firstOrder.ShouldBe(reversedOrder);
        firstOrder.ShouldBeFalse();
    }

    // Unit conversion admits the same non-transitivity, so the defect is not confined to bare decimals:
    // 96 'cm' converts to 0.96 'm' and is equivalent to 1.0 'm' at one decimal place, 1.04 'm' ~ 1.0 'm'
    // holds, and 1.04 'm' ~ 96 'cm' does not. The pairing 1.0-96 'cm' and 1.04-1.0 'm' is the perfect one.
    [Fact]
    public void GivenNonTransitivelyEquivalentQuantityCollectionsAcrossUnits_WhenTheRightOperandIsReordered_ThenBothAreEquivalent()
    {
        var root = new ScalarRoot();

        var firstOrder = EvaluateBoolean(root, "(1.0 'm' | 1.04 'm') ~ (1.0 'm' | 96 'cm')");
        var reversedOrder = EvaluateBoolean(root, "(1.0 'm' | 1.04 'm') ~ (96 'cm' | 1.0 'm')");

        firstOrder.ShouldBe(reversedOrder);
        firstOrder.ShouldBeTrue();
    }

    // Strengthens the reordering guard above, which compares only equal-or-not complex elements and so
    // cannot see a matcher that fails to back out of a bad pairing. These are resource-backed Quantity
    // elements - null Value, content in children - so the comparison runs the full ladder: structural
    // descent, quantity extraction, unit conversion, and the non-transitive precision rule.
    [Fact]
    public void GivenNonTransitivelyEquivalentQuantityElementsInDifferentOrders_WhenComparedForEquivalence_ThenTheyAreEquivalent()
    {
        var bundle = ParseNative(NonTransitiveQuantityBundleJson);

        var firstOrder = EvaluateBoolean(
            bundle,
            "Bundle.entry.take(2).resource.value ~ Bundle.entry.skip(2).resource.value");
        var reversedOrder = EvaluateBoolean(
            bundle,
            "Bundle.entry.take(2).resource.value ~ "
            + "(Bundle.entry.last().resource.value | Bundle.entry.skip(2).first().resource.value)");

        firstOrder.ShouldBe(reversedOrder);
        firstOrder.ShouldBeTrue();
    }

    [Theory]
    [InlineData("5 'mg'", "5000 'ug'", true, true)]
    [InlineData("5 'mg'", "6 'mg'", false, false)]
    [InlineData("5 'mg'", "5.4 'mg'", false, true)]
    [InlineData("1.0", "1.2", false, false)]
    [InlineData("1.0", "1.04", false, true)]
    public void GivenQuantitiesAndDecimals_WhenComparedForEqualityAndEquivalence_ThenTheyDifferOnlyByPrecision(
        string left,
        string right,
        bool expectedEquality,
        bool expectedEquivalence)
    {
        var root = new ScalarRoot();

        var equality = EvaluateBoolean(root, $"{left} = {right}");
        var equivalence = EvaluateBoolean(root, $"{left} ~ {right}");

        equality.ShouldBe(expectedEquality);
        equivalence.ShouldBe(expectedEquivalence);
    }

    [Fact]
    public void GivenComplexValues_WhenComparedForEqualityAndEquivalence_ThenTheyAgreeStructurally()
    {
        var bundle = ParseNative(DifferentCodeableConceptBundleJson);

        var equalMatch = EvaluateBoolean(
            bundle,
            "Bundle.entry.resource.code.first() = Bundle.entry.resource.value.first()");
        var equivalentMatch = EvaluateBoolean(
            bundle,
            "Bundle.entry.resource.code.first() ~ Bundle.entry.resource.value.first()");
        var equalDifference = EvaluateBoolean(
            bundle,
            "Bundle.entry.resource.code.last() = Bundle.entry.resource.value.last()");
        var equivalentDifference = EvaluateBoolean(
            bundle,
            "Bundle.entry.resource.code.last() ~ Bundle.entry.resource.value.last()");

        equalMatch.ShouldBeTrue();
        equivalentMatch.ShouldBeTrue();
        equalDifference.ShouldBeFalse();
        equivalentDifference.ShouldBeFalse();
    }

    [Fact]
    public void GivenNativeAndAdaptedElements_WhenComparingComplexCollections_ThenTheyReturnTheSameResult()
    {
        var nativeBundle = ParseNative(QuantityAndCodeBundleJson);
        var adaptedBundle = new IgnixaElementAdapter(FirelyEngine.Parse(QuantityAndCodeBundleJson));
        const string expression = "Bundle.entry.resource.value ~ Bundle.entry.resource.code";

        var nativeResult = EvaluateBoolean(nativeBundle, expression);
        var adaptedResult = EvaluateBoolean(adaptedBundle, expression);

        nativeResult.ShouldBeFalse();
        adaptedResult.ShouldBe(nativeResult);
    }

    [Fact]
    public void GivenBackboneFamilyElementsFromNativeAndAdapter_WhenCompared_ThenTheyReturnTheSameResult()
    {
        var nativeBundle = ParseNative(BackboneFamilyBundleJson);
        var adaptedBundle = new IgnixaElementAdapter(FirelyEngine.Parse(BackboneFamilyBundleJson));
        const string expression =
            "Bundle.entry.resource.ofType(CodeSystem).concept ~ " +
            "Bundle.entry.resource.ofType(ValueSet).expansion.contains";

        var nativeResult = EvaluateBoolean(nativeBundle, expression);
        var adaptedResult = EvaluateBoolean(adaptedBundle, expression);

        nativeResult.ShouldBeTrue();
        adaptedResult.ShouldBe(nativeResult);
    }

    private static IElement ParseNative(string json) => ResourceJsonNode.Parse(json).ToElement(Schema);

    private bool EvaluateBoolean(IElement subject, string expression)
    {
        var parsed = _parser.Parse(expression);
        var result = _evaluator.Evaluate(subject, parsed, DifferentialFixture.CreateContext(subject)).Single();
        return result.Value.ShouldBeOfType<bool>();
    }

    private sealed class ScalarRoot : IElement
    {
        public string Name => string.Empty;
        public string InstanceType => "integer";
        public object Value => 0;
        public string Location => string.Empty;
        public IType? Type => null;
        public bool HasPrimitiveValue => true;
        public IReadOnlyList<IElement> Children(string? name = null) => [];
        public T? Meta<T>() where T : class => null;
    }
}
