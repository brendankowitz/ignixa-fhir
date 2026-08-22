// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Indexing;

/// <summary>
/// Pins the version gate on FHIRPath's singleton rule for <c>as</c> against the SearchParameter
/// expressions HL7 actually ships, in both directions: the rule must fire on R5 and must not fire
/// below it.
/// </summary>
/// <remarks>
/// <para>
/// FHIRPath 1.6.3 says the evaluator must throw when <c>as</c> is given more than one item.
/// <see cref="Ignixa.FhirPath.Evaluation.TypeMatcher.EnsureSingletonInput"/> enforces that from R5
/// onwards only, because HL7's own artifacts contradict it below R5:
/// <c>Observation.component.value as Quantity</c> is one of 58 operator-form <c>as</c> expressions in
/// the shipped R4 definitions and 59 in R4B, many over 0..* paths; HL7 rewrote almost all of them to
/// <c>ofType()</c> for R5. STU3 is unaffected - it spells all 50 of its casts with the <c>as()</c>
/// function. HAPI draws the line in exactly the same place (<c>doNotEnforceAsSingletonRule</c> below
/// R5) for exactly this reason.
/// </para>
/// <para>
/// The tests below go through <see cref="ISearchIndexer.Extract"/> rather than raw FHIRPath, because
/// that is where the damage would land: <c>ElementSearchIndexer</c> evaluates the whole expression at
/// the resource root, and its non-composite path catches and logs, so a throw would drop values from
/// the index with nothing surfaced to the caller.
/// </para>
/// </remarks>
public class AsOperatorSearchParameterCardinalityTests
{
    private const string TwoComponentObservationJson = """
    {
      "resourceType": "Observation",
      "id": "blood-pressure",
      "status": "final",
      "code": { "coding": [ { "system": "http://loinc.org", "code": "85354-9" } ] },
      "component": [
        {
          "code": { "coding": [ { "system": "http://loinc.org", "code": "8480-6" } ] },
          "valueQuantity": { "value": 107, "unit": "mmHg", "system": "http://unitsofmeasure.org", "code": "mm[Hg]" }
        },
        {
          "code": { "coding": [ { "system": "http://loinc.org", "code": "8462-4" } ] },
          "valueQuantity": { "value": 60, "unit": "mmHg", "system": "http://unitsofmeasure.org", "code": "mm[Hg]" }
        }
      ]
    }
    """;

    private const string SingleComponentObservationJson = """
    {
      "resourceType": "Observation",
      "id": "systolic-only",
      "status": "final",
      "code": { "coding": [ { "system": "http://loinc.org", "code": "85354-9" } ] },
      "component": [
        {
          "code": { "coding": [ { "system": "http://loinc.org", "code": "8480-6" } ] },
          "valueQuantity": { "value": 107, "unit": "mmHg", "system": "http://unitsofmeasure.org", "code": "mm[Hg]" }
        }
      ]
    }
    """;

    private const string DateTimeObservationJson = """
    {
      "resourceType": "Observation",
      "id": "body-weight",
      "status": "final",
      "code": { "coding": [ { "system": "http://loinc.org", "code": "29463-7" } ] },
      "valueDateTime": "2024-06-15T08:00:00Z"
    }
    """;

    private const string Stu3GoalJson = """
    {
      "resourceType": "Goal",
      "id": "stop-smoking",
      "status": "in-progress",
      "description": { "text": "Stop smoking" },
      "startDate": "2024-01-01",
      "target": { "dueDate": "2024-12-31" }
    }
    """;

    /// <summary>
    /// The two STU3 SearchParameters that depend on <c>Date</c> being in the pre-R5 alias set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// STU3 ships eleven casts spelled in PascalCase where the model declares lowercase -
    /// <c>DateTime</c> x6, <c>Date</c> x2, <c>String</c> x1, <c>Uri</c> x2 - and <c>Date</c>'s two
    /// sites are exactly these: <c>Goal.start.as(Date)</c> and <c>Goal.target.due.as(Date)</c>.
    /// </para>
    /// <para>
    /// They are pinned because the alias set's justification is written in terms of System spellings,
    /// and <c>Date</c> is the one entry whose shipped dependants are not otherwise visible anywhere in
    /// the suite. An auditor reconciling the alias list against the model could delete it as
    /// unnecessary and silently empty both of these parameters with no test objecting.
    /// </para>
    /// <para>
    /// <c>Quantity</c> is deliberately absent from that alias set for the opposite reason: STU3 spells
    /// seven casts <c>as(Quantity)</c> and R4/R4B nineteen, but <c>FHIR.Quantity</c> is genuinely
    /// PascalCase, so those resolve by ordinal match and need no alias.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("start-date")]
    [InlineData("target-date")]
    public void GivenStu3AndAGoal_WhenIndexing_ThenTheCapitalisedDateCastsStillProduceEntries(
        string searchParameterCode)
    {
        // Arrange
        var schema = FhirVersion.Stu3.GetSchemaProvider();

        // Act
        var entries = Index(schema, Stu3GoalJson)
            .Where(entry => entry.SearchParameter.Code == searchParameterCode)
            .ToList();

        // Assert. Exact count, per the standard this file states below: "not empty" would still pass if
        // the cast silently started yielding a different number of entries.
        entries.ShouldHaveSingleItem(
            $"STU3 '{searchParameterCode}' spells its cast as(Date); removing Date from the pre-R5 alias set empties it");
    }

    [Theory]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    public void GivenPreR5AndADateTimeObservation_WhenIndexing_ThenCodeValueDateStillProducesAnEntry(
        FhirVersion version)
    {
        // R4 and R4B still ship `value.as(DateTime) | value.as(Period)` in this composite. DateTime is
        // the canonical System spelling that those versions explicitly permit to cross to FHIR.dateTime.

        // Arrange
        var schema = version.GetSchemaProvider();

        // Act
        var entries = Index(schema, DateTimeObservationJson)
            .Where(entry => entry.SearchParameter.Code == "code-value-date")
            .ToList();

        // Assert
        entries.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("combo-value-quantity")]
    [InlineData("component-value-quantity")]
    [InlineData("component-code-value-quantity")]
    [InlineData("combo-code-value-quantity")]
    public void GivenR4AndASingleComponentObservation_WhenIndexing_ThenTheShippedAsExpressionsStillProduceEntries(string searchParameterCode)
    {
        // The regression this file exists to prevent. All four shipped R4 parameters put `as` on a
        // repeating path; enforcing the singleton rule on R4 made every one of them raise, and the
        // indexer's catch-and-log turned that into silently missing index entries.

        // Arrange
        var indices = Index(new R4CoreSchemaProvider(), SingleComponentObservationJson);

        // Act
        var entries = indices.Where(i => i.SearchParameter.Code == searchParameterCode).ToList();

        // Assert
        entries.ShouldNotBeEmpty($"'{searchParameterCode}' must still index on R4 - the 'as' singleton rule is gated off below R5");
    }

    [Theory]
    [InlineData("component-code-value-quantity", 2)]
    [InlineData("combo-code-value-quantity", 2)]
    public void GivenR4AndATwoComponentObservation_WhenIndexing_ThenTheCompositeParametersIndexBothComponents(string searchParameterCode, int expectedEntries)
    {
        // The composites scope `as` to one component at a time - their root expression is
        // "Observation.component", so the left operand is always a singleton - which is why they are
        // unaffected by the rule in either direction and index both components on every version.

        // Arrange
        var indices = Index(new R4CoreSchemaProvider(), TwoComponentObservationJson);

        // Act
        var entries = indices.Where(i => i.SearchParameter.Code == searchParameterCode).ToList();

        // Assert
        entries.Count.ShouldBe(expectedEntries);
    }

    [Theory]
    [InlineData("combo-value-quantity", 2)]
    [InlineData("component-value-quantity", 2)]
    public void GivenR4AndATwoComponentObservation_WhenIndexing_ThenTheNonCompositeParametersIndexBothComponents(string searchParameterCode, int expectedEntries)
    {
        // These two indexed NOTHING here until the 'as' operator was made to filter element-wise below
        // R5 - it returned empty for a non-singleton input while the as() function filtered, so
        // Observation.component.value as Quantity yielded nothing for a blood pressure and the most
        // common multi-component Observation in FHIR was unfindable by component value on R4/R4B.
        // That was a pre-existing defect, not one the version gate introduced; the gate is what made it
        // visible. Asserting 2 rather than "not empty" is deliberate: one entry would mean the operator
        // had silently gone back to taking a single item.

        // Arrange
        var indices = Index(new R4CoreSchemaProvider(), TwoComponentObservationJson);

        // Act
        var entries = indices.Where(i => i.SearchParameter.Code == searchParameterCode).ToList();

        // Assert
        entries.Count.ShouldBe(expectedEntries);
    }

    [Theory]
    [InlineData("combo-value-quantity", 2)]
    [InlineData("component-value-quantity", 2)]
    public void GivenR5AndATwoComponentObservation_WhenIndexing_ThenTheOfTypeExpressionsIndexBothComponents(string searchParameterCode, int expectedEntries)
    {
        // The other half of the story: HL7's R5 definitions use ofType(), which is exempt from the
        // singleton rule, so enforcing the rule on R5 costs nothing and both components index.

        // Arrange
        var indices = Index(new R5CoreSchemaProvider(), TwoComponentObservationJson);

        // Act
        var entries = indices.Where(i => i.SearchParameter.Code == searchParameterCode).ToList();

        // Assert
        entries.Count.ShouldBe(expectedEntries);
    }

    [Theory]
    [InlineData("(Observation.component.value as Quantity)")]
    [InlineData("Observation.component.value.as(Quantity)")]
    public void GivenR5AndAMultiItemInput_WhenEvaluatingAs_ThenTheSingletonRuleFires(string expression)
    {
        // Proves the rule is actually enforced somewhere, so that "gated on version" cannot quietly
        // decay into "never enforced". Both spellings are covered because they are separate code
        // paths - the operator in FhirPathEvaluator, the function in CollectionFunctions.
        var schema = new R5CoreSchemaProvider();
        var element = ResourceJsonNode.Parse(TwoComponentObservationJson).ToElement(schema);
        var context = new EvaluationContext { Resource = element, Schema = schema };

        // Act
        var exception = Should.Throw<FhirPathEvaluationException>(() => element.Select(expression, context).ToList());

        // Assert
        exception.Message.ShouldContain("single item");
    }

    [Theory]
    [InlineData("(Observation.component.value as Quantity)")]
    [InlineData("Observation.component.value.as(Quantity)")]
    public void GivenR4AndAMultiItemInput_WhenEvaluatingAs_ThenBothComponentsAreReturned(string expression)
    {
        // Below R5 the rule does not fire, and both spellings filter element-wise - which is what makes
        // the indexing assertions above come out at 2 rather than 0.

        // Arrange
        var schema = new R4CoreSchemaProvider();
        var element = ResourceJsonNode.Parse(TwoComponentObservationJson).ToElement(schema);
        var context = new EvaluationContext { Resource = element, Schema = schema };

        // Act
        var results = element.Select(expression, context).ToList();

        // Assert
        results.Count.ShouldBe(2);
    }

    private static IReadOnlyCollection<SearchIndexEntry> Index(IFhirSchemaProvider schema, string resourceJson)
    {
        var definitionManager = new SearchParameterDefinitionManager(schema, new NullLogger<SearchParameterDefinitionManager>());
        var indexer = SearchIndexerFactory.CreateInstance(schema, NullLoggerFactory.Instance, definitionManager, NullFhirBaseUriProvider.Instance);

        return indexer.Extract(ResourceJsonNode.Parse(resourceJson).ToElement(schema));
    }
}
