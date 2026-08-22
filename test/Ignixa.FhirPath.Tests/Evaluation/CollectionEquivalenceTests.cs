/*
 * Copyright (c) 2025, Ignixa Contributors
 */

using System.Reflection;
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

    /// <summary>
    /// Bounded property test proving <c>FhirPathEvaluator.TryPair</c> computes a genuine maximum bipartite
    /// matching, independent of the five hand-picked cases above - every one of which is n=2, where
    /// <c>TryPair</c> reaches recursion depth 2 at most and never needs to displace a pairing more than
    /// once, so the cycle guard and the deeper augmenting search are never exercised.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A prior PR claimed this algorithm was "proven maximum against a brute-force oracle over 33.5M
    /// generated graphs". No such oracle, generator, or property test existed anywhere in the repo - the
    /// claim survived only in the PR description. This is the oracle that claim should have shipped with.
    /// </para>
    /// <para>
    /// <c>TryPair</c> and <c>AreCollectionsEquivalent</c> are not modified or reimplemented here - they
    /// have been independently reviewed as correct (visited reset per left node, the <c>-1</c> sentinel,
    /// termination, the up-front cardinality check, order independence). <see cref="RunsProductionAlgorithm"/>
    /// invokes the real, private <c>TryPair</c> through reflection and reproduces only
    /// <c>AreCollectionsEquivalent</c>'s outer loop (iterate left indices in order, clear <c>visited</c>,
    /// call <c>TryPair</c>) so the full per-graph answer is exercised, not just one augmenting search.
    /// </para>
    /// <para>
    /// The graph is driven from a synthetic <c>bool[][]</c> adjacency matrix rather than from FHIR element
    /// equivalence: <c>AreElementsEquivalent</c> is close to an equality relation for most element types
    /// (two decimals or two strings), so it cannot be made to express an arbitrary bipartite graph -
    /// equivalence is symmetric and near-transitive by construction, whereas an adjacency matrix's cells
    /// are independent. Reflection reaches the matching algorithm directly instead, which is what "driven
    /// from an adjacency predicate you control" means here: the predicate is the matrix itself.
    /// </para>
    /// <para>
    /// The brute-force oracle (<see cref="HasPerfectMatchingByBruteForce"/>) tries every permutation of
    /// right-hand indices and accepts if any one respects the adjacency matrix everywhere - the textbook
    /// definition of a perfect matching - and calls nothing from <c>FhirPathEvaluator</c>, so it shares no
    /// code path with the algorithm it is checking.
    /// </para>
    /// </remarks>
    private static readonly MethodInfo TryPairMethod =
        typeof(FhirPathEvaluator).GetMethod("TryPair", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "FhirPathEvaluator.TryPair was not found by reflection. Its name or signature changed, and "
            + "this property test needs updating to match before it can prove anything again.");

    // Mirrors FhirPathEvaluator.Unpaired. The two are independent constants that happen to agree, not a
    // shared one - reflection reaches the method, not its private fields.
    private const int Unpaired = -1;

    private static bool RunsProductionAlgorithm(bool[][] adjacency)
    {
        var n = adjacency.Length;
        var candidates = new List<int>[n];

        for (var leftIndex = 0; leftIndex < n; leftIndex++)
        {
            var row = new List<int>();
            for (var rightIndex = 0; rightIndex < n; rightIndex++)
            {
                if (adjacency[leftIndex][rightIndex])
                {
                    row.Add(rightIndex);
                }
            }

            candidates[leftIndex] = row;
        }

        var pairedWith = new int[n];
        Array.Fill(pairedWith, Unpaired);

        for (var leftIndex = 0; leftIndex < n; leftIndex++)
        {
            var visited = new bool[n];
            var paired = (bool)TryPairMethod.Invoke(null, [leftIndex, candidates, pairedWith, visited])!;
            if (!paired)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasPerfectMatchingByBruteForce(bool[][] adjacency)
    {
        var n = adjacency.Length;
        var indices = new int[n];
        for (var i = 0; i < n; i++)
        {
            indices[i] = i;
        }

        foreach (var permutation in Permutations(indices))
        {
            var isPerfectMatching = true;
            for (var leftIndex = 0; leftIndex < n; leftIndex++)
            {
                if (!adjacency[leftIndex][permutation[leftIndex]])
                {
                    isPerfectMatching = false;
                    break;
                }
            }

            if (isPerfectMatching)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Heap's algorithm, written independently of anything under test - its only job is to drive the
    /// brute-force oracle above, which must not call the code it exists to check.
    /// </summary>
    private static IEnumerable<int[]> Permutations(int[] items)
    {
        var n = items.Length;
        var working = (int[])items.Clone();
        var state = new int[n];

        yield return (int[])working.Clone();

        var i = 0;
        while (i < n)
        {
            if (state[i] < i)
            {
                if (i % 2 == 0)
                {
                    (working[0], working[i]) = (working[i], working[0]);
                }
                else
                {
                    (working[state[i]], working[i]) = (working[i], working[state[i]]);
                }

                yield return (int[])working.Clone();
                state[i]++;
                i = 0;
            }
            else
            {
                state[i] = 0;
                i++;
            }
        }
    }

    [Fact]
    public void GivenEveryPossibleThreeByThreeAdjacencyMatrix_WhenMatchedByTheProductionAlgorithm_ThenItAgreesWithBruteForce()
    {
        const int n = 3;

        for (var mask = 0; mask < 1 << (n * n); mask++)
        {
            var adjacency = new bool[n][];
            for (var row = 0; row < n; row++)
            {
                adjacency[row] = new bool[n];
                for (var column = 0; column < n; column++)
                {
                    adjacency[row][column] = (mask & (1 << (row * n + column))) != 0;
                }
            }

            var expected = HasPerfectMatchingByBruteForce(adjacency);
            var actual = RunsProductionAlgorithm(adjacency);

            actual.ShouldBe(expected, $"adjacency mask {mask} (n=3) disagrees with the brute-force oracle.");
        }
    }

    [Fact]
    public void GivenEveryPossibleFourByFourAdjacencyMatrix_WhenMatchedByTheProductionAlgorithm_ThenItAgreesWithBruteForce()
    {
        const int n = 4;

        for (var mask = 0; mask < 1 << (n * n); mask++)
        {
            var adjacency = new bool[n][];
            for (var row = 0; row < n; row++)
            {
                adjacency[row] = new bool[n];
                for (var column = 0; column < n; column++)
                {
                    adjacency[row][column] = (mask & (1 << (row * n + column))) != 0;
                }
            }

            var expected = HasPerfectMatchingByBruteForce(adjacency);
            var actual = RunsProductionAlgorithm(adjacency);

            actual.ShouldBe(expected, $"adjacency mask {mask} (n=4) disagrees with the brute-force oracle.");
        }
    }

    /// <summary>
    /// n=5 is 2^25 adjacency matrices - exhaustive is not a "few seconds" property test at this size, so
    /// this samples instead, from a fixed seed rather than an unseeded <see cref="Random"/>, so a failure
    /// reproduces on the next run instead of depending on when the test happened to be executed.
    /// </summary>
    [Fact]
    public void GivenAFixedSeedSampleOfFiveByFiveAdjacencyMatrices_WhenMatchedByTheProductionAlgorithm_ThenItAgreesWithBruteForce()
    {
        const int n = 5;
        const int sampleCount = 2000;

        // Deterministic test-data generation, not a security context - CA5394 does not apply.
#pragma warning disable CA5394
        var random = new Random(20260821);
#pragma warning restore CA5394

        for (var sample = 0; sample < sampleCount; sample++)
        {
            var adjacency = new bool[n][];
            for (var row = 0; row < n; row++)
            {
                adjacency[row] = new bool[n];
                for (var column = 0; column < n; column++)
                {
#pragma warning disable CA5394
                    adjacency[row][column] = random.Next(2) == 1;
#pragma warning restore CA5394
                }
            }

            var expected = HasPerfectMatchingByBruteForce(adjacency);
            var actual = RunsProductionAlgorithm(adjacency);

            actual.ShouldBe(
                expected,
                $"sample {sample} (n=5, seed 20260821) disagrees with the brute-force oracle.");
        }
    }

    /// <summary>
    /// A synthetic adjacency where greedy first-fit - pair each left item with its first available
    /// candidate, never revisit - reports no perfect matching, while one genuinely exists through a
    /// single displacement. The human-readable example of what the augmenting-path search buys over
    /// first-fit, matching <c>FhirPathEvaluator.TryPair</c>'s own remarks on why greedy is unsound here.
    /// </summary>
    /// <remarks>
    /// L0 pairs with R0 or R1; L1 pairs only with R0; L2 pairs with R1 or R2. Greedy left-to-right gives
    /// L0 to R0 (its first candidate) and then strands L1, whose only candidate R0 is already taken -
    /// greedy never asks whether L0 could move elsewhere. The perfect matching L0-R1, L1-R0, L2-R2 exists,
    /// and reaching it takes exactly one displacement: L1 claims R0 by evicting L0, and L0 finds R1 free.
    /// </remarks>
    [Fact]
    public void GivenAnAdjacencyWhereGreedyFirstFitFails_WhenMatchedByTheProductionAlgorithm_ThenAPerfectMatchingIsFound()
    {
        bool[][] adjacency =
        [
            [true, true, false],
            [true, false, false],
            [false, true, true],
        ];

        RunsProductionAlgorithm(adjacency).ShouldBeTrue();
        HasPerfectMatchingByBruteForce(adjacency).ShouldBeTrue();
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
