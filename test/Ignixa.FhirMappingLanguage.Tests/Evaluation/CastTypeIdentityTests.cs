/*
 * Copyright (c) 2026, Ignixa Contributors
 */

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Mutator;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.FhirMappingLanguage.Tests.Conformance;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Serialization.TestSupport;
using Ignixa.Specification.Extensions;
using Shouldly;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Evaluation;

public class CastTypeIdentityTests
{
    public static TheoryData<string, string, string, bool> CastTypes()
    {
        var data = new TheoryData<string, string, string, bool>();
        (string Literal, string SystemType, string FhirType)[] pairs =
        [
            ("'text'", "String", "string"),
            ("true", "Boolean", "boolean"),
            ("42", "Integer", "integer"),
            ("9223372036854775807L", "Long", "integer64"),
            ("1.25", "Decimal", "decimal"),
            ("'2024-02'", "Date", "date"),
            ("'2024-02-29T12:34:56Z'", "DateTime", "dateTime"),
            ("'12:34:56'", "Time", "time"),
            ("2.5 'mg'", "Quantity", "Quantity")
        ];
        foreach (var (literal, system, fhir) in pairs)
        {
            data.Add(literal, system, fhir, true);
            data.Add(literal, system, fhir, false);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(CastTypes))]
    public void GivenANamespacedCast_WhenInspectingAnIntermediate_ThenFhirPathRetainsItsNamespace(
        string literal, string systemType, string fhirType, bool isSystem)
    {
        var castType = isSystem ? $"System.{systemType}" : $"FHIR.{fhirType}";
        var (context, target) = Execute($$"""
            src -> cast({{literal}}, '{{castType}}') as value then {
              src -> tgt.active = (value.is(System.{{systemType}}));
            };
            """);
        var value = context.GetVariable("value").ShouldBeAssignableTo<IElement>().ShouldNotBeNull();

        (value is ISystemValueElement).ShouldBe(isSystem);
        Evaluate(value, $"is(System.{systemType})").ShouldBe(isSystem);
        Evaluate(value, $"is(FHIR.{fhirType})").ShouldBe(!isSystem);
        Evaluate(value, "type().namespace").ShouldBe(isSystem ? "System" : "FHIR");
        Evaluate(value, "type().name").ShouldBe(isSystem ? systemType : fhirType);
        target.MutableNode()["active"]!.GetValue<bool>().ShouldBe(isSystem);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GivenCastValuesAsTheCurrentFocus_WhenCheckingTypes_ThenImplicitAndExplicitFocusAgree(bool isSystem)
    {
        var castType = isSystem ? "System.String" : "FHIR.string";
        var (context, target) = Execute($$"""
            src -> cast('text', '{{castType}}') as value then {
              value check ($this.is({{castType}})) -> tgt.active = (is(System.String));
            };
            """);

        var value = context.GetVariable("value").ShouldBeAssignableTo<IElement>().ShouldNotBeNull();
        Evaluate(value, "is(System.String)").ShouldBe(isSystem);
        target.MutableNode()["active"]!.GetValue<bool>().ShouldBe(isSystem);
    }

    [Theory]
    [InlineData("System.String")]
    [InlineData("FHIR.string")]
    [InlineData("string")]
    public void GivenAStringCast_WhenWritingATarget_ThenOnlyTheDestinationBecomesAFhirElement(string castType)
    {
        var (context, target) = Execute($$"""
            src -> cast('text', '{{castType}}') as value then {
              src -> tgt.name as name then {
                value -> name.family = value as written;
              };
            };
            """);
        var value = context.GetVariable("value").ShouldBeAssignableTo<IElement>().ShouldNotBeNull();
        var written = context.GetVariable("written").ShouldBeAssignableTo<IElement>().ShouldNotBeNull();

        Evaluate(value, "is(System.String)").ShouldBe(castType == "System.String");
        Evaluate(written, "is(FHIR.string)").ShouldBe(true);
        Evaluate(written, "is(System.String)").ShouldBe(false);
        Evaluate(written, "type().namespace").ShouldBe("FHIR");
        AssertJson(target, """{"resourceType":"Patient","name":[{"family":"text"}]}""");
    }

    [Fact]
    public void GivenASystemCast_WhenPassedThroughAGroupAndExternalVariable_ThenItsIdentityIsNotUnwrapped()
    {
        var (context, target) = Execute("""
            src -> cast('text', 'System.String') as value then Check(value, tgt);
            """, additionalGroups: """
            group Check(source input, target output) {
              input check ($this.is(System.String)) -> output.active = (%input.is(System.String));
            }
            """);

        Evaluate(context.GetVariable("value").ShouldBeAssignableTo<IElement>().ShouldNotBeNull(), "is(System.String)").ShouldBe(true);
        AssertJson(target, """{"resourceType":"Patient","active":true}""");
    }

    [Theory]
    [InlineData("FHIR.string", "System.String", true)]
    [InlineData("System.String", "FHIR.string", false)]
    public void GivenRepeatedCasts_WhenChangingNamespace_ThenTheSecondCastDeterminesTheResult(
        string firstType, string secondType, bool expectedSystem)
    {
        var (context, _) = Execute($$"""
            src -> cast('text', '{{firstType}}') as first then {
              src -> cast(first, '{{secondType}}') as second;
            };
            """);

        var second = context.GetVariable("second").ShouldBeAssignableTo<IElement>().ShouldNotBeNull();
        Evaluate(second, "is(System.String)").ShouldBe(expectedSystem);
        Evaluate(second, "is(FHIR.string)").ShouldBe(!expectedSystem);
        second.Value.ShouldBe("text");
    }

    [Theory]
    [InlineData("System.String", true)]
    [InlineData("FHIR.string", false)]
    public void GivenACastReturnedAsThis_WhenTheEvaluationScopeEnds_ThenItsTypeAndValueSurvive(
        string castType, bool isSystem)
    {
        var (context, _) = Execute($$"""
            src -> cast('retained', '{{castType}}') as value then {
              src -> evaluate(value, $this) as echoed;
            };
            """);
        var echoed = context.GetVariable("echoed").ShouldBeAssignableTo<IElement>().ShouldNotBeNull();

        var original = context.GetVariable("value").ShouldBeAssignableTo<IElement>().ShouldNotBeNull();
        echoed.Meta<JsonNode>().ShouldBeSameAs(original.Meta<JsonNode>());
        echoed.InstanceType.ShouldBe("string");
        echoed.Value.ShouldBe("retained");
        Evaluate(echoed, "is(System.String)").ShouldBe(isSystem);
        Evaluate(echoed, "is(FHIR.string)").ShouldBe(!isSystem);
    }

    [Fact]
    public void GivenASystemQuantity_WhenWritingAResource_ThenOnlyTheWrittenValueUsesFhirUcumRepresentation()
    {
        var (context, target) = Execute("""
            src -> cast(2 days, 'System.Quantity') as value then {
              src -> tgt.extension as e then { value -> e.value = value as written; };
            };
            """);
        var value = context.GetVariable("value").ShouldBeAssignableTo<IElement>().ShouldNotBeNull();
        var written = context.GetVariable("written").ShouldBeAssignableTo<IElement>().ShouldNotBeNull();

        Evaluate(value, "is(System.Quantity)").ShouldBe(true);
        Evaluate(value, "type().namespace").ShouldBe("System");
        value.Value.ShouldBeOfType<FhirQuantity>().Unit.ShouldBe("days");
        Evaluate(written, "is(FHIR.Quantity)").ShouldBe(true);
        Evaluate(written, "is(System.Quantity)").ShouldBe(false);
        AssertJson(target, """
            {"resourceType":"Patient","extension":[{"valueQuantity":{
              "value":2,"unit":"days","system":"http://unitsofmeasure.org","code":"d"}}]}
            """);
    }

    [Fact]
    public void GivenASystemQuantity_WhenUsedInArithmetic_ThenItRemainsAProcessableSystemValue()
    {
        var (_, target) = Execute("""
            src -> cast(2 'mg', 'System.Quantity') as value then {
              src -> tgt.active = ((value + 1 'mg') = 3 'mg');
            };
            """);

        AssertJson(target, """{"resourceType":"Patient","active":true}""");
    }

    [Theory]
    [InlineData("'text'", "String")]
    [InlineData("9223372036854775807L", "Long")]
    [InlineData("'2024-02'", "Date")]
    [InlineData("2.5 'mg'", "Quantity")]
    public void GivenASystemCastWithoutAMutator_WhenEvaluating_ThenItsIdentityIsStillRetained(
        string literal, string typeName)
    {
        var (context, _) = Execute($"src -> cast({literal}, 'System.{typeName}') as value;", useMutator: false);

        var value = context.GetVariable("value").ShouldBeAssignableTo<IElement>().ShouldNotBeNull();
        Evaluate(value, $"is(System.{typeName})").ShouldBe(true);
        Evaluate(value, "type().namespace").ShouldBe("System");
        Evaluate(value, "type().name").ShouldBe(typeName);
    }

    [Fact]
    public void GivenAFhirCastWithoutAMutator_WhenEvaluating_ThenFailsRatherThanReturningASystemValue()
    {
        var exception = Should.Throw<InvalidOperationException>(() =>
            Execute("src -> cast('text', 'FHIR.string') as value;", useMutator: false));

        exception.Message.ShouldContain("schema-aware mapping mutator");
    }

    [Fact]
    public void GivenAnElementReturningCastResolver_WhenCastingToSystem_ThenOnlyItsValueIsWrapped()
    {
        var mutator = new JsonNodeMutator(new FhirPathEvaluator(), new FhirPathParser(), () => FhirVersion.R5.GetSchemaProvider());
        var converted = mutator.CreateElement("string", "converted");
        var (context, _) = Execute("src -> cast('input', 'System.String') as value;",
            transformResolver: (name, _) =>
            {
                name.ShouldBe("cast");
                return converted;
            });
        var value = context.GetVariable("value").ShouldBeAssignableTo<IElement>().ShouldNotBeNull();

        value.Value.ShouldBe("converted");
        Evaluate(value, "is(System.String)").ShouldBe(true);
        Evaluate(value, "is(FHIR.string)").ShouldBe(false);
    }

    private static (MappingContext Context, ResourceJsonNode Target) Execute(
        string rules, bool useMutator = true, string additionalGroups = "",
        Func<string, IEnumerable<object>, object>? transformResolver = null)
    {
        var schema = FhirVersion.R5.GetSchemaProvider();
        var source = ResourceJsonNode.Parse("""{"resourceType":"Patient"}""");
        var target = ResourceJsonNode.Parse("""{"resourceType":"Patient"}""");
        var context = new MappingContext { TransformResolver = transformResolver };
        context.SetSource("src", source.ToElement(schema));
        context.SetTarget("tgt", target.ToElement(schema));
        context.SetTargetResource("tgt", target);
        var mutator = useMutator ? new JsonNodeMutator(new FhirPathEvaluator(), new FhirPathParser(), () => schema) : null;
        var map = new MappingParser().Parse($$"""
            map 'http://example.org/cast-type' = 'CastType'
            group Main(source src : Patient, target tgt : Patient) {
              {{rules}}
            }
            {{additionalGroups}}
            """);

        new MappingEvaluator(MappingEvaluatorOptions.Default, mutator).Execute(map, context);

        context.Errors.ShouldBeEmpty();
        return (context, target);
    }

    private static object? Evaluate(IElement value, string expression) =>
        new FhirPathEvaluator().Evaluate(value, new FhirPathParser().Parse(expression),
            new EvaluationContext { Schema = FhirVersion.R5.GetSchemaProvider() }).Single().Value;

    private static void AssertJson(ResourceJsonNode target, string expected) =>
        CanonicalJson.Canonicalize(target.MutableNode().ToJsonString()).ShouldBe(CanonicalJson.Canonicalize(expected));
}
