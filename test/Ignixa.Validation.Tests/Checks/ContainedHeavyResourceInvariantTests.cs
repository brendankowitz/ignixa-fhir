// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Checks;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Checks;

/// <summary>
/// The default evaluation limits must not fail legitimate resources. <c>dom-3</c> walks the whole resource for every
/// contained resource, so a form-builder Questionnaire (SDC, LHC-Forms) that carries dozens of contained ValueSets
/// with their expansions is the realistic worst case for it.
/// </summary>
public class ContainedHeavyResourceInvariantTests
{
    /// <summary>The R4 <c>dom-3</c> expression, as shipped in the R4 specification provider.</summary>
    private const string Dom3 =
        "contained.where((('#'+id in (%resource.descendants().reference | %resource.descendants().as(canonical) | " +
        "%resource.descendants().as(uri) | %resource.descendants().as(url))) or descendants().where(reference = '#').exists() " +
        "or descendants().where(as(canonical) = '#').exists() or descendants().where(as(canonical) = '#').exists()).not())" +
        ".trace('unmatched', id).empty()";

    private readonly ISchema _schema = TestSchemaProvider.GetR4Schema();
    private readonly FhirPathParser _parser = new();

    [Fact]
    public void GivenAQuestionnaireWithAHundredAndFiftyContainedValueSetExpansions_WhenValidatingDom3UnderDefaultLimits_ThenItPasses()
    {
        // Arrange - regression: with navigation charged per item, dom-3's four walks of the whole resource per
        // contained ValueSet came to over ten million steps here (about 37,000 nodes, 150 contained), so the limit
        // failed a valid resource. The walks are loop-invariant and are now computed once per evaluation.
        var questionnaire = QuestionnaireWithContainedValueSets(valueSets: 150, codesPerValueSet: 60);

        // Act
        var result = Validate(questionnaire);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenAContainedValueSetNothingReferences_WhenValidatingDom3_ThenItStillFails()
    {
        // Arrange - control: computing the walk once must not change the answer.
        var questionnaire = QuestionnaireWithContainedValueSets(valueSets: 3, codesPerValueSet: 2, referenced: 2);

        // Act
        var result = Validate(questionnaire);

        // Assert
        result.IsValid.ShouldBeFalse();
    }

    private ValidationResult Validate(IElement resource)
    {
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = "dom-3",
            Severity = ConstraintSeverity.Error,
            Human = "If the resource is contained in another resource, it SHALL be referred to from elsewhere in the resource",
            Expression = Dom3,
            AppliesTo = ["Questionnaire"],
        };

        return new FhirPathInvariantCheck(constraint, _schema, _parser)
            .Validate(resource, new ValidationSettings { Depth = ValidationDepth.Spec }, ValidationState.ForRoot(resource));
    }

    private IElement QuestionnaireWithContainedValueSets(int valueSets, int codesPerValueSet, int? referenced = null)
    {
        var contained = new JsonArray();
        var items = new JsonArray();

        for (var v = 0; v < valueSets; v++)
        {
            var codes = new JsonArray();
            for (var c = 0; c < codesPerValueSet; c++)
            {
                codes.Add(new JsonObject
                {
                    ["system"] = "http://loinc.org",
                    ["code"] = $"LA{v}-{c}",
                    ["display"] = $"Answer {c} of list {v}",
                });
            }

            contained.Add(new JsonObject
            {
                ["resourceType"] = "ValueSet",
                ["id"] = $"vs{v}",
                ["status"] = "active",
                ["expansion"] = new JsonObject
                {
                    ["identifier"] = $"urn:uuid:expansion-{v}",
                    ["timestamp"] = "2024-01-01T00:00:00Z",
                    ["contains"] = codes,
                },
            });

            if (v < (referenced ?? valueSets))
            {
                items.Add(new JsonObject
                {
                    ["linkId"] = $"q{v}",
                    ["text"] = $"Question {v}",
                    ["type"] = "choice",
                    ["answerValueSet"] = $"#vs{v}",
                });
            }
        }

        var json = new JsonObject
        {
            ["resourceType"] = "Questionnaire",
            ["id"] = "contained-heavy",
            ["status"] = "active",
            ["contained"] = contained,
            ["item"] = items,
        };

        return JsonNodeSourceNode.Create(json).ToElement(_schema);
    }
}
