// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Parsing;

/// <summary>
/// Drives <see cref="IrProjector"/> off IR the real parser produced wherever it can, so the projection is
/// asserted against shapes the parser actually emits rather than shapes a fixture imagined.
/// </summary>
public class IrProjectorTests
{
    [Fact]
    public void GivenAModifiedLeaf_WhenDescribed_ThenTheWrapperAndItsPredicateAreSeparateRowsOneLevelApart()
    {
        // Arrange
        var ir = ParsePatient(("name", SearchParamType.String), ("name:exact", "Smith"));

        // Act
        var rows = IrProjector.Describe(ir);

        // Assert
        rows.Select(row => (row.Kind, row.Depth)).ShouldBe([("param", 0), ("predicate", 1)]);
        rows[0].Text.ShouldBe("Param name");
        rows[1].Text.ShouldContain("name");
    }

    [Fact]
    public void GivenCommaSeparatedAlternatives_WhenDescribed_ThenEachAlternativeIsItsOwnRowUnderOneContainer()
    {
        // Arrange
        var ir = ParsePatient(("name", SearchParamType.String), ("name", "Smith,Jones"));

        // Act
        var rows = IrProjector.Describe(ir);

        // Assert
        var predicates = rows.Where(row => row.Kind == "predicate").ToList();
        predicates.Count.ShouldBe(2);
        predicates.ShouldAllBe(row => row.Depth == 2);
        rows[1].Kind.ShouldBeOneOf("or", "union");
    }

    [Fact]
    public void GivenAForwardChain_WhenDescribed_ThenTheChainRowNamesItsReferenceAndTargetWithoutInliningTheInnerExpression()
    {
        // Arrange
        var harness = SearchOptionsBuilderHarness.ForPatientChainedThrough(
            "general-practitioner", "Practitioner", "name", SearchParamType.String);
        var ir = SingleIr(harness, ("general-practitioner.name", "Smith"));

        // Act
        var rows = IrProjector.Describe(ir);

        // Assert
        rows[0].Kind.ShouldBe("chain");
        rows[0].Text.ShouldBe("Chain general-practitioner:Practitioner");
        rows[0].Text.ShouldNotContain("Smith", Case.Insensitive, "the inner expression has its own rows");
        rows.Select(row => row.Kind).ShouldBe(["chain", "param", "predicate"]);
        rows.Select(row => row.Depth).ShouldBe([0, 1, 2]);
    }

    [Fact]
    public void GivenAMissingModifier_WhenDescribed_ThenItIsASingleLeafRow()
    {
        // Arrange
        var ir = ParsePatient(("name", SearchParamType.String), ("name:missing", "true"));

        // Act
        var rows = IrProjector.Describe(ir);

        // Assert
        var row = rows.ShouldHaveSingleItem();
        row.Kind.ShouldBe("missing");
        row.Depth.ShouldBe(0);
        row.Text.ShouldBe("(MissingParam name)");
    }

    [Fact]
    public void GivenTwoParametersAndedTogether_WhenDescribed_ThenTheAndRowSitsAboveBothBranches()
    {
        // Arrange
        var harness = SearchOptionsBuilderHarness.ForPatient(
            ("name", SearchParamType.String), ("gender", SearchParamType.Token));
        var options = harness.Build([("name", "Smith"), ("gender", "male")]);

        // Act
        var rows = IrProjector.Describe(options.Expression);

        // Assert
        rows[0].ShouldBe(new IrRow("and", "And", 0));
        rows.Count(row => row.Kind == "param").ShouldBe(2);
        rows.Where(row => row.Kind == "param").ShouldAllBe(row => row.Depth == 1);
    }

    [Fact]
    public void GivenAUnionOfNegatedLeaves_WhenDescribed_ThenEveryContainerKindGetsItsOwnStableToken()
    {
        // Arrange
        var nameParameter = new SearchParameterInfo(
            "name", "name", SearchParamType.String, new Uri("http://ignixa.test/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(
            nameParameter, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"));
        var ir = new UnionExpression(UnionOperator.All, [new NotExpression(predicate), predicate]);

        // Act
        var rows = IrProjector.Describe(ir);

        // Assert
        rows.Select(row => (row.Kind, row.Depth)).ShouldBe(
            [("union", 0), ("not", 1), ("predicate", 2), ("predicate", 1)]);
        rows[0].Text.ShouldBe("Union All");
        rows[1].Text.ShouldBe("Not");
    }

    [Fact]
    public void GivenACompositeComponent_WhenDescribed_ThenTheComponentRowNamesItsPositionAndCode()
    {
        // Arrange
        var codeParameter = new SearchParameterInfo(
            "code", "code", SearchParamType.Token, new Uri("http://ignixa.test/SearchParameter/Observation-code"));
        var component = new CompositeComponentExpression(
            codeParameter,
            0,
            new SearchParameterPredicateExpression(
                codeParameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(null, "8480-6", null)));

        // Act
        var rows = IrProjector.Describe(component);

        // Assert
        rows[0].ShouldBe(new IrRow("composite", "Component[0] code", 0));
        rows[1].Kind.ShouldBe("predicate");
    }

    [Fact]
    public void GivenIncludeAndSortAndNotReferencedNodes_WhenDescribed_ThenEachIsALeafRowCarryingItsOwnRendering()
    {
        // Arrange
        var organization = new SearchParameterInfo(
            "organization", "organization", SearchParamType.Reference, new Uri("http://ignixa.test/SearchParameter/Patient-organization"));
        var name = new SearchParameterInfo(
            "name", "name", SearchParamType.String, new Uri("http://ignixa.test/SearchParameter/Patient-name"));

        // Act
        var include = IrProjector.Describe(
            new IncludeExpression(["Patient"], organization, "Patient", "Organization", ["Organization"], wildCard: false, reversed: false, iterate: false)).ShouldHaveSingleItem();
        var sort = IrProjector.Describe(new SortExpression(name, SortOrder.Descending)).ShouldHaveSingleItem();
        var notReferenced = IrProjector.Describe(new NotReferencedExpression("Observation", "subject")).ShouldHaveSingleItem();

        // Assert
        include.Kind.ShouldBe("include");
        include.Text.ShouldBe("(Include organization:Organization)");
        sort.Kind.ShouldBe("sort");
        sort.Text.ShouldBe("(Sort Param: -name)");
        notReferenced.Kind.ShouldBe("notReferenced");
        notReferenced.Text.ShouldBe("(NotReferenced Observation:subject)");
    }

    [Fact]
    public void GivenAnUnprojectableNode_WhenDescribed_ThenItThrowsRatherThanSilentlyDroppingIt()
    {
        // Arrange
        var node = Expression.CompartmentSearch("Patient", "123");

        // Act & Assert
        Should.Throw<NotSupportedException>(() => IrProjector.Describe(node))
            .Message.ShouldContain(nameof(CompartmentSearchExpression));
    }

    private static Expression ParsePatient(
        (string Code, SearchParamType Type) searchParameter,
        (string Key, string Value) query)
        => SingleIr(SearchOptionsBuilderHarness.ForPatient(searchParameter), query);

    private static Expression SingleIr(SearchOptionsBuilderHarness harness, (string Key, string Value) query)
    {
        var outcomes = new List<ParameterTrace>();
        harness.Build([query], outcomes);
        return outcomes.ShouldHaveSingleItem().Ir.ShouldNotBeNull();
    }
}
