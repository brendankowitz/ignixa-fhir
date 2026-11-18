// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// #533 replaced the pairwise dedup behind <c>|</c>, <c>distinct()</c>, <c>isDistinct()</c>, <c>intersect()</c>,
/// <c>exclude()</c>, <c>in</c> and <c>repeat()</c> with hash buckets. These pin that every equality the pairwise
/// scan recognised - each one a place where a naive hash would split equal values - still dedupes, and that
/// unequal values still do not; and that dedup over keyed data is now linear.
/// </summary>
public class HashedDedupSemanticsTests
{
    private static readonly IFhirSchemaProvider Schema = FhirVersion.R4.GetSchemaProvider();

    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    public static TheoryData<string, int> UnionCounts => new()
    {
        // Integer and Decimal are one number; a decimal's scale is not part of its value.
        { "(1 | 1.0 | 1.00).count()", 1 },
        { "(1.5 | 1.50 | 2.5).count()", 2 },
        { "(1 | 1 'L'.value).count()", 1 },

        // Temporals compare as instants, so different literals can be one value...
        { "(@2012-01-01T10:00:00Z | @2012-01-01T20:00:00+10:00).count()", 1 },
        { "(@2012-01-01T10:00:30 | @2012-01-01T10:00:30.000).count()", 1 },

        // ...and differing precision is indeterminate, which collection membership treats as "not the same item".
        { "(@2012 | @2012-01).count()", 2 },
        { "(@T10:00 | @T10:00:00).count()", 2 },

        // A temporal and a String with its literal's text compare as text.
        { "(@2012-01-01 | '2012-01-01').count()", 1 },
        { "('2012-01-01' | @2012-01-01).count()", 1 },

        // Quantities compare after unit conversion, and a number is a quantity in unit '1'.
        { "(1 'm' | 100 'cm').count()", 1 },
        { "(1 '1' | 1).count()", 1 },
        { "(1 | 1 '1').count()", 1 },
        { "(1 'm' | 1 'g').count()", 2 },

        // Strings compare ordinally and Booleans by value.
        { "('a' | 'A' | 'a').count()", 2 },
        { "(true | false | true).count()", 2 },
        { "(1 | '1' | true).count()", 3 },
    };

    [Theory]
    [MemberData(nameof(UnionCounts))]
    public void GivenValuesThatEqualityRelates_WhenUnioned_ThenTheyDedupeExactlyAsBefore(string expression, int expected)
    {
        // Arrange
        var root = Parse("""{"resourceType":"Patient","id":"p"}""");

        // Act
        var result = Evaluate(root, expression);

        // Assert
        result.Single().Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("distinct().count()", 2)]
    [InlineData("isDistinct()", false)]
    [InlineData("intersect(%second).count()", 1)]
    [InlineData("exclude(%second).count()", 1)]
    [InlineData("union(%second).count()", 2)]
    public void GivenComplexElementsThatDifferOnlyInChildOrder_WhenDeduplicated_ThenTheyAreOneItem(string operation, object expected)
    {
        // Arrange - FHIRPath equality compares children by name, so two codings whose properties arrive in a different
        // order are equal, and a third with a different code is not.
        var root = Parse("""
            {"resourceType":"Observation","id":"o","status":"final",
             "code":{"coding":[{"system":"http://loinc.org","code":"8480-6"}]},
             "component":[
               {"code":{"coding":[{"code":"8480-6","system":"http://loinc.org"}]}},
               {"code":{"coding":[{"system":"http://loinc.org","code":"8462-4"}]}}
             ]}
            """);
        var expression = $"defineVariable('second', code.coding).select(code.coding.combine(component.code.coding)).{operation}";

        // Act
        var result = Evaluate(root, expression);

        // Assert
        result.Single().Value.ShouldBe(expected);
    }

    [Fact]
    public void GivenAQuantityAndANumericComplexElement_WhenUnioned_ThenTheUnitConversionStillDedupes()
    {
        // Arrange - a resource Quantity (a complex element) equals a System quantity in a convertible unit.
        var root = Parse("""{"resourceType":"Observation","id":"o","status":"final","code":{"text":"x"},"valueQuantity":{"value":1,"unit":"m","system":"http://unitsofmeasure.org","code":"m"}}""");

        // Act
        var result = Evaluate(root, "(value | 100 'cm').count()");

        // Assert
        result.Single().Value.ShouldBe(1);
    }

    [Theory]
    [InlineData("code | component.code")]
    [InlineData("component.code.coding.code.distinct()")]
    [InlineData("component.code.isDistinct()")]
    [InlineData("component.code.intersect(component.code)")]
    public void GivenFiveThousandDistinctComponents_WhenDeduplicated_ThenTheCostIsLinear(string expression)
    {
        // Arrange - combo-code's shape (#533). Pairwise, five thousand items cost about 12.5 million comparisons;
        // hashed, under two hundred steps each for navigation and hashing. A budget of 1,000,000 steps admits only
        // the latter.
        var root = Parse(ObservationWithComponents(5_000));
        var context = new EvaluationContext { Limits = FhirPathEvaluationLimits.Default with { MaxSteps = 1_000_000 } };

        // Act
        var result = _evaluator.Evaluate(root, _parser.Parse(expression), context).ToList();

        // Assert
        result.ShouldNotBeEmpty();
    }

    [Fact]
    public void GivenComplexElementsWhoseChildIsADateInOneAndItsTextInTheOther_WhenUnioned_ThenTheyAreOneItem()
    {
        // Arrange - a date equals a String with its text, so a parent holding the date and one holding the String
        // are equal child by child; their keys must agree even though a temporal has no text key of its own.
        var root = Node("root", "Root", null,
            Node("item", "Element", null, Node("start", "date", "2012-01-01")),
            Node("item", "Element", null, Node("start", "string", "2012-01-01")));

        // Act
        var result = Evaluate(root, "(item | item).count()");

        // Assert
        result.Single().Value.ShouldBe(1);
    }

    [Fact]
    public void GivenComplexElementsHoldingAQuantityAndItsUntypedTwin_WhenUnioned_ThenTheyAreOneItem()
    {
        // Arrange - a quantity is compared child by child with an element of another type that has its children,
        // so a parent holding the quantity and one holding the twin are equal; their keys must agree.
        var root = Node("root", "Root", null,
            Node("item", "Element", null, Node("measure", "Quantity", null, Node("value", "decimal", 1m), Node("code", "code", "m"))),
            Node("item", "Element", null, Node("measure", "Element", null, Node("value", "decimal", 1m), Node("code", "code", "m"))));

        // Act
        var result = Evaluate(root, "(item | item).count()");

        // Assert
        result.Single().Value.ShouldBe(1);
    }

    [Fact]
    public void GivenQuantityShapedChildrenWhoseValueIsAYearInOneAndItsTextInTheOther_WhenUnioned_ThenTheyAreOneItem()
    {
        // Arrange - regression: in a quantity-shaped child, a String value that reads as a number was keyed as a
        // possible magnitude and a date value was not, although "2012" equals @2012; the index kept both parents.
        FhirTemporal.TryParse("2012", FhirPrimitive.Date, out var year).ShouldBeTrue();
        var root = Node("root", "Root", null,
            Node("item", "Element", null, Node("measure", "Element", null, Node("value", "string", "2012"))),
            Node("item", "Element", null, Node("measure", "Element", null, Node("value", "date", year))));

        // Act
        var result = Evaluate(root, "(item | item).count()");

        // Assert
        result.Single().Value.ShouldBe(1);
    }

    [Fact]
    public void GivenQuantitiesInManySpellingsOfOneUnit_WhenUnioned_ThenEachConversionIsChargedForWhatItCosts()
    {
        // Arrange - regression: g.{a1}, g.{a2}, ... are one compatibility class, and each UCUM conversion between two
        // spellings takes a millisecond or more, but was charged 64 steps; sixty spellings fit a million steps while
        // taking seconds. A conversion is now charged 12,000 steps.
        var root = Node("root", "Root", null, Enumerable.Range(1, 60)
            .Select(i => Node("item", "Quantity", null, Node("value", "decimal", (decimal)i), Node("code", "code", $"g.{{a{i}}}")))
            .ToArray());
        var context = new EvaluationContext { Limits = FhirPathEvaluationLimits.Default with { MaxSteps = 1_000_000 } };

        // Act & Assert
        Should.Throw<FhirPathResourceLimitExceededException>(
            () => _evaluator.Evaluate(root, _parser.Parse("(item | item).count()"), context).ToList());
    }

    [Fact]
    public void GivenAThousandMassesInSixUnits_WhenUnioned_ThenEachUnitIsConvertedOnce()
    {
        // Arrange - mg, g, kg, ug, ng and lb are one class. Converting every lookup into every unit present would be
        // 6,000 conversions; each unit is instead scaled to the first once, and a lookup probes by magnitude.
        string[] units = ["mg", "g", "kg", "ug", "ng", "[lb_av]"];
        var root = Node("root", "Root", null, Enumerable.Range(0, 1_000)
            .Select(i => Node("item", "Quantity", null, Node("value", "decimal", i + 0.5m), Node("code", "code", units[i % units.Length])))
            .ToArray());
        var context = new EvaluationContext { Limits = FhirPathEvaluationLimits.Default with { MaxSteps = 1_000_000 } };

        // Act
        var result = _evaluator.Evaluate(root, _parser.Parse("(item | item).count()"), context).ToList();

        // Assert
        result.Single().Value.ShouldBe(1_000);
    }

    [Fact]
    public void GivenTheSameMassInTwoUnits_WhenUnioned_ThenTheyAreOneItem()
    {
        // Arrange - 1500 mg and 1.5 g land in the same magnitude bucket through different scales.
        var root = Node("root", "Root", null,
            Node("item", "Quantity", null, Node("value", "decimal", 1500m), Node("code", "code", "mg")),
            Node("item", "Quantity", null, Node("value", "decimal", 1.5m), Node("code", "code", "g")),
            Node("item", "Quantity", null, Node("value", "decimal", 0.0015m), Node("code", "code", "kg")));

        // Act
        var result = Evaluate(root, "(item | item).count()");

        // Assert
        result.Single().Value.ShouldBe(1);
    }

    [Fact]
    public void GivenEqualMassesWhoseMagnitudesStraddleABucketBoundary_WhenUnioned_ThenTheyAreOneItem()
    {
        // Arrange - found by search: in kg, 4807888.714286 g and 4807.888714286 kg differ by one rounding of a double
        // and fall in adjacent magnitude buckets, so the lookup must probe the neighbours.
        var root = Node("root", "Root", null,
            Node("item", "Quantity", null, Node("value", "decimal", 4807.888714286m), Node("code", "code", "kg")),
            Node("item", "Quantity", null, Node("value", "decimal", 4807888.714286m), Node("code", "code", "g")));

        // Act
        var result = Evaluate(root, "(item | item).count()");

        // Assert
        result.Single().Value.ShouldBe(1);
    }

    [Fact]
    public void GivenQuantitiesEqualOnlyAtDecimalsScaleLimit_WhenUnioned_ThenTheyDedupeAsEqualityDoes()
    {
        // Arrange - regression: 1.2346e-16 m expressed in Gm rounds to 1.235e-25 at decimal's 28 places, so equality
        // (which expresses the right operand in the left's unit) holds while the magnitudes are about 3e-4 apart -
        // about 300,000 buckets. Values that small are now compared directly.
        var root = Node("root", "Root", null,
            Node("item", "Quantity", null, Node("value", "decimal", 0.0000000000000000000000001235m), Node("code", "code", "Gm")),
            Node("item", "Quantity", null, Node("value", "decimal", 0.00000000000000012346m), Node("code", "code", "m")));

        // Act
        var equal = Evaluate(root, "item[0] = item[1]");
        var result = Evaluate(root, "(item | item).count()");

        // Assert
        equal.Single().Value.ShouldBe(true);
        result.Single().Value.ShouldBe(1);
    }

    [Fact]
    public void GivenAThousandQuantitiesInDistinctUnits_WhenUnioned_ThenTheCostIsLinear()
    {
        // Arrange - combo-value-quantity over components whose units all differ (m1, m2, ... m1000 are a thousand
        // dimensions). Grouped by unit alone, every lookup tested every unit: about 500,000 probes, each a UCUM
        // parse. Grouped by compatibility class, each unit is parsed once (3,000 steps) and a lookup tests only its
        // own class, so a thousand units fit in four million steps.
        var root = Parse(ObservationWithDistinctUnits(1_000));
        var context = new EvaluationContext { Limits = FhirPathEvaluationLimits.Default with { MaxSteps = 4_000_000 } };

        // Act
        var result = _evaluator.Evaluate(root, _parser.Parse("(component.value | component.value).count()"), context).ToList();

        // Assert
        result.Single().Value.ShouldBe(1_000);
    }

    private static IElement Parse(string json) => ResourceJsonNode.Parse(json).ToElement(Schema);

    private static string ObservationWithDistinctUnits(int count)
    {
        var json = new StringBuilder("""{"resourceType":"Observation","id":"o","status":"final","code":{"text":"x"},"component":[""");
        for (var i = 1; i <= count; i++)
        {
            if (i > 1)
            {
                json.Append(',');
            }

            json.Append("""{"code":{"text":"c"},"valueQuantity":{"value":1,"system":"http://unitsofmeasure.org","code":"m""")
                .Append(i)
                .Append("\"}}");
        }

        return json.Append("]}").ToString();
    }

    private static IElement Node(string name, string type, object? value, params IElement[] children) =>
        new TestNode(name, type, value, children);

    private static string ObservationWithComponents(int count)
    {
        var json = new StringBuilder("""{"resourceType":"Observation","id":"o","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"root"}]},"component":[""");
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
            {
                json.Append(',');
            }

            json.Append("""{"code":{"coding":[{"system":"http://loinc.org","code":"c""").Append(i).Append("\"}]}}");
        }

        return json.Append("]}").ToString();
    }

    private List<IElement> Evaluate(IElement root, string expression) =>
        _evaluator.Evaluate(root, _parser.Parse(expression)).ToList();

    private sealed class TestNode(string name, string type, object? value, IReadOnlyList<IElement> children) : IElement
    {
        public string Name => name;
        public string InstanceType => type;
        public object? Value => value;
        public string Location => name;
        public IType? Type => null;
        public bool HasPrimitiveValue => value is not null;

        public IReadOnlyList<IElement> Children(string? childName = null) =>
            childName is null ? children : children.Where(child => child.Name == childName).ToList();

        public T? Meta<T>() where T : class => null;
    }
}
