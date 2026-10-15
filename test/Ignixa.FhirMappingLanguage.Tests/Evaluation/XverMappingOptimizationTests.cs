/*
 * Copyright (c) 2026, Ignixa Contributors
 */

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Mutator;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.FhirMappingLanguage.Registry;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;
using Shouldly;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Evaluation;

public class XverMappingOptimizationTests
{
    [Fact]
    public void GivenComplexCollectionAliases_WhenAnItemIsMoved_ThenRefreshedAliasesHaveCurrentIndices()
    {
        var schema = FhirVersion.R4.GetSchemaProvider();
        var mutator = CreateMutator(schema);
        var patient = mutator.CreateElement("Patient");
        var first = mutator.SetTarget(patient, "name", mutator.CreateElement("HumanName",
            new Dictionary<string, object?> { ["family"] = "First" }));
        var second = mutator.SetTarget(patient, "name", mutator.CreateElement("HumanName",
            new Dictionary<string, object?> { ["family"] = "Second" }));

        mutator.MoveTarget(second, 0);

        var movedSecond = mutator.Refresh(second);
        var movedFirst = mutator.Refresh(first);
        movedSecond.Location.ShouldBe("Patient.name[0]");
        movedSecond.Children("family").ShouldHaveSingleItem().Value.ShouldBe("Second");
        movedFirst.Location.ShouldBe("Patient.name[1]");
        movedFirst.Children("family").ShouldHaveSingleItem().Value.ShouldBe("First");
        patient.Meta<JsonNode>()!["name"]!.ToJsonString().ShouldBe(
            """[{"family":"Second"},{"family":"First"}]""");
    }

    [Fact]
    public void GivenPrimitiveCollectionWithShadows_WhenAnItemIsMoved_ThenValuesAndShadowsStayIndexedTogether()
    {
        var schema = FhirVersion.R4.GetSchemaProvider();
        var mutator = CreateMutator(schema);
        var source = ResourceJsonNode.Parse(
            """{"resourceType":"Patient","name":[{"given":["First","Second"],"_given":[null,{"id":"second-id"}]}]}""");
        var patient = mutator.CreateElement("Patient");
        var name = mutator.SetTarget(patient, "name", source.ToElement(schema).Children("name").ShouldHaveSingleItem());
        var given = name.Children("given");
        var first = given[0];
        var second = given[1];

        mutator.MoveTarget(second, 0);

        var refreshedName = mutator.Refresh(name);
        var refreshedGiven = refreshedName.Children("given");
        refreshedGiven.Count.ShouldBe(2);
        refreshedGiven[0].Location.ShouldBe("Patient.name[0].given[0]");
        refreshedGiven[0].Value.ShouldBe("Second");
        refreshedGiven[0].Children("id").ShouldHaveSingleItem().Value.ShouldBe("second-id");
        refreshedGiven[1].Location.ShouldBe("Patient.name[0].given[1]");
        refreshedGiven[1].Value.ShouldBe("First");
        patient.Meta<JsonNode>()!["name"]![0]!.ToJsonString().ShouldBe(
            """{"given":["Second","First"],"_given":[{"id":"second-id"},null]}""");
        mutator.Refresh(first).Location.ShouldBe(".given[1]");
    }

    [Fact]
    public void GivenMisalignedPrimitiveAndShadowArrays_WhenRefreshingShadowOnlyIndex_ThenKeepsItsMetadataAndLocation()
    {
        var schema = FhirVersion.R4.GetSchemaProvider();
        var mutator = CreateMutator(schema);
        var source = ResourceJsonNode.Parse(
            """{"resourceType":"Patient","name":[{"given":["First"],"_given":[null,{"id":"shadow-only"}]}]}""");
        var shadowOnly = source.ToElement(schema).Children("name").ShouldHaveSingleItem()
            .Children("given")[1];

        var refreshed = mutator.Refresh(shadowOnly);

        refreshed.Location.ShouldBe(".given[1]");
        refreshed.HasPrimitiveValue.ShouldBeFalse();
        refreshed.Value.ShouldBeNull();
        refreshed.Children("id").ShouldHaveSingleItem().Value.ShouldBe("shadow-only");
        refreshed.InstanceType.ShouldBe("string");
    }

    [Fact]
    public void GivenChoiceTarget_WhenWrapped_ThenUsesWireNameAndRetainsTypedChildren()
    {
        var schema = FhirVersion.R4.GetSchemaProvider();
        var mutator = CreateMutator(schema);
        var observation = mutator.CreateElement("Observation");
        var quantity = mutator.CreateElement("Quantity", new Dictionary<string, object?> { ["value"] = 2.5m });

        var result = mutator.SetTarget(observation, "value", quantity);
        mutator.SetTarget(result, "unit", mutator.CreateElement("string", "mg"));

        var refreshed = mutator.Refresh(result);
        refreshed.InstanceType.ShouldBe("Quantity");
        refreshed.Location.ShouldBe("Observation.valueQuantity");
        refreshed.Children("value").ShouldHaveSingleItem().Value.ShouldBe(2.5m);
        refreshed.Children("unit").ShouldHaveSingleItem().Value.ShouldBe("mg");
        observation.Meta<JsonNode>()!["valueQuantity"]!.ToJsonString().ShouldBe(
            """{"value":2.5,"unit":"mg"}""");
    }

    [Fact]
    public void GivenImmutableSource_WhenMappingToJsonTarget_ThenCopiesItsValueWithoutRequiringRefresh()
    {
        var schema = FhirVersion.R4.GetSchemaProvider();
        var mutator = CreateMutator(schema);
        var sourceId = new ImmutableElement("id", "string", "immutable-id");
        var source = new ImmutableElement("src", "Patient", children: [sourceId]);
        var target = ResourceJsonNode.Parse("""{"resourceType":"Patient"}""");
        var context = new MappingContext();
        context.SetSource("src", source);
        context.SetTarget("tgt", target.ToElement(schema));

        Execute(ParseMap("src.id -> tgt.id;"), context, mutator);

        target.ToSourceNavigator().Meta<JsonNode>()!["id"]!.GetValue<string>().ShouldBe("immutable-id");
        source.Children("id").ShouldHaveSingleItem().ShouldBeSameAs(sourceId);
        sourceId.Value.ShouldBe("immutable-id");
    }

    [Fact]
    public void GivenCrossVersionSourceAndReusedEvaluator_WhenExecutingAgainWithSameEditedSource_ThenCacheIsResetAndTargetViewIsPreserved()
    {
        var sourceSchema = FhirVersion.R4.GetSchemaProvider();
        var targetSchema = FhirVersion.Stu3.GetSchemaProvider();
        var mutator = new RefreshCountingMutator(CreateMutator(targetSchema));
        var evaluator = new MappingEvaluator(MappingEvaluatorOptions.Default, mutator);
        var map = ParseMap("src.name -> tgt.name; src.name -> tgt.name;");
        var source = ResourceJsonNode.Parse(
            """{"resourceType":"Patient","name":[{"family":"First","given":["R4"]}]}""");
        var firstTarget = ResourceJsonNode.Parse("""{"resourceType":"Patient"}""");
        var context = CreateContext(source, firstTarget, sourceSchema, targetSchema);
        var sourceElement = context.GetSource("src")!;

        Execute(map, context, evaluator);

        mutator.RefreshCount(sourceElement).ShouldBe(1);
        firstTarget.ToSourceNavigator().Meta<JsonNode>()!["name"]![0]!["family"]!.GetValue<string>().ShouldBe("First");
        firstTarget.ToSourceNavigator().Meta<JsonNode>()!["name"]![0]!["given"]![0]!.GetValue<string>().ShouldBe("R4");

        sourceElement.Meta<JsonNode>()!["name"]![0]!["family"] = "Second";
        sourceElement.Meta<JsonNode>()!["name"]![0]!["given"]![0] = "Updated between executions";
        var secondTarget = ResourceJsonNode.Parse("""{"resourceType":"Patient"}""");
        context.SetTarget("tgt", secondTarget.ToElement(targetSchema));

        Execute(map, context, evaluator);

        mutator.RefreshCount(sourceElement).ShouldBe(2);
        secondTarget.ToSourceNavigator().Meta<JsonNode>()!["name"]![0]!["family"]!.GetValue<string>().ShouldBe("Second");
        secondTarget.ToSourceNavigator().Meta<JsonNode>()!["name"]![0]!["given"]![0]!.GetValue<string>()
            .ShouldBe("Updated between executions");
        firstTarget.ToSourceNavigator().Meta<JsonNode>()!["name"]![0]!["family"]!.GetValue<string>().ShouldBe("First");
        context.GetSource("src").ShouldBeSameAs(sourceElement);
    }
    [Fact]
    public void GivenR4CanonicalSourceAndStu3UriTarget_WhenReusingEvaluator_ThenFirstSourceRefreshPreservesSchemaInterpretation()
    {
        var sourceSchema = FhirVersion.R4.GetSchemaProvider();
        var targetSchema = FhirVersion.Stu3.GetSchemaProvider();
        var mutator = new RefreshCountingMutator(CreateMutator(targetSchema));
        var evaluator = new MappingEvaluator(MappingEvaluatorOptions.Default, mutator);
        var map = new MappingParser().Parse("""
            map 'http://example.org/xver-canonical-uri-guard' = 'CanonicalUriGuard'
            group Main(source src : CodeSystem, target tgt : CodeSystem) {
              src.valueSet -> tgt.valueSet;
            }
            """);
        var firstSource = ResourceJsonNode.Parse(
            """{"resourceType":"CodeSystem","valueSet":"http://example.org/ValueSet/first"}""");
        var firstTarget = ResourceJsonNode.Parse("""{"resourceType":"CodeSystem"}""");
        var firstContext = CreateContext(firstSource, firstTarget, sourceSchema, targetSchema);
        var firstSourceElement = firstContext.GetSource("src")!;
        firstSourceElement.Children("valueSet").ShouldHaveSingleItem().InstanceType.ShouldBe("canonical");
        CreateMutator(targetSchema).Refresh(firstSourceElement).Children("valueSet")
            .ShouldHaveSingleItem().InstanceType.ShouldBe("canonical");

        Execute(map, firstContext, evaluator);

        firstTarget.ToSourceNavigator().Meta<JsonNode>()!.ToJsonString().ShouldBe(
            """{"resourceType":"CodeSystem","valueSet":"http://example.org/ValueSet/first"}""");
        firstSource.ToSourceNavigator().Meta<JsonNode>()!.ToJsonString().ShouldBe(
            """{"resourceType":"CodeSystem","valueSet":"http://example.org/ValueSet/first"}""");
        mutator.RefreshCount(firstSourceElement).ShouldBe(1);

        var secondSource = ResourceJsonNode.Parse(
            """{"resourceType":"CodeSystem","valueSet":"http://example.org/ValueSet/second"}""");
        var secondTarget = ResourceJsonNode.Parse("""{"resourceType":"CodeSystem"}""");
        var secondContext = CreateContext(secondSource, secondTarget, sourceSchema, targetSchema);
        var secondSourceElement = secondContext.GetSource("src")!;
        secondSourceElement.Children("valueSet").ShouldHaveSingleItem().InstanceType.ShouldBe("canonical");

        Execute(map, secondContext, evaluator);

        secondTarget.ToSourceNavigator().Meta<JsonNode>()!.ToJsonString().ShouldBe(
            """{"resourceType":"CodeSystem","valueSet":"http://example.org/ValueSet/second"}""");
        secondSource.ToSourceNavigator().Meta<JsonNode>()!.ToJsonString().ShouldBe(
            """{"resourceType":"CodeSystem","valueSet":"http://example.org/ValueSet/second"}""");
        mutator.RefreshCount(secondSourceElement).ShouldBe(1);
        firstTarget.ToSourceNavigator().Meta<JsonNode>()!["valueSet"]!.GetValue<string>()
            .ShouldBe("http://example.org/ValueSet/first");
    }
    [Fact]
    public void GivenR4Account_WhenMappingServicePeriodToStu3Period_ThenPeriodFieldsArePreserved()
    {
        var sourceSchema = FhirVersion.R4.GetSchemaProvider();
        var targetSchema = FhirVersion.Stu3.GetSchemaProvider();
        var source = ResourceJsonNode.Parse(
            """{"resourceType":"Account","servicePeriod":{"start":"2020-01-01","end":"2020-12-31"}}""");
        var target = ResourceJsonNode.Parse("""{"resourceType":"Account"}""");
        var map = new MappingParser().Parse("""
            map 'http://example.org/xver-account-period' = 'AccountPeriod'
            uses 'http://hl7.org/fhir/4.0/StructureDefinition/Account' alias Account as source
            uses 'http://hl7.org/fhir/3.0/StructureDefinition/Account' alias AccountR3 as target
            group Main(source src : Account, target tgt : AccountR3) {
              src.servicePeriod -> tgt.period;
            }
            """);

        Execute(map, CreateContext(source, target, sourceSchema, targetSchema), CreateMutator(targetSchema));

        target.ToSourceNavigator().Meta<JsonNode>()!["period"]!.ToJsonString().ShouldBe(
            """{"start":"2020-01-01","end":"2020-12-31"}""");
    }

    [Fact]
    public async Task GivenImportedPrimitiveConversion_WhenMappingAccountName_ThenPrimitiveValueIsPreserved()
    {
        var sourceSchema = FhirVersion.R4.GetSchemaProvider();
        var targetSchema = FhirVersion.Stu3.GetSchemaProvider();
        var parser = new MappingParser();
        var conversion = parser.Parse("""
            map 'http://example.org/maps/string' = 'String'
            uses 'http://hl7.org/fhir/4.0/StructureDefinition/string' alias stringR4 as source
            uses 'http://hl7.org/fhir/3.0/StructureDefinition/string' alias stringR3 as target
            group string(source src : stringR4, target tgt : stringR3) <<types>> {
              src.value as v -> tgt.value = v;
            }
            """);
        var map = parser.Parse("""
            map 'http://example.org/xver-account-name' = 'AccountName'
            uses 'http://hl7.org/fhir/4.0/StructureDefinition/Account' alias Account as source
            uses 'http://hl7.org/fhir/3.0/StructureDefinition/Account' alias AccountR3 as target
            imports 'http://example.org/maps/string'
            group Main(source src : Account, target tgt : AccountR3) {
              src.name -> tgt.name;
            }
            """);
        var registry = new MapRegistry();
        registry.Register(conversion);
        var resolver = new ImportResolver(registry, parser);
        await resolver.ResolveImportsAsync(map);
        var source = ResourceJsonNode.Parse("""{"resourceType":"Account","name":"Converted account"}""");
        var target = ResourceJsonNode.Parse("""{"resourceType":"Account"}""");
        var context = CreateContext(source, target, sourceSchema, targetSchema);

        new MappingEvaluator(MappingEvaluatorOptions.Default, CreateMutator(targetSchema), resolver).Execute(map, context);

        target.ToSourceNavigator().Meta<JsonNode>()!["name"]!.GetValue<string>().ShouldBe("Converted account");
    }

    [Fact]
    public async Task GivenImportedPrimitiveChoiceConversion_WhenMappingCarePlanSchedule_ThenStringValueIsPreserved()
    {
        var sourceSchema = FhirVersion.R4.GetSchemaProvider();
        var targetSchema = FhirVersion.Stu3.GetSchemaProvider();
        var parser = new MappingParser();
        var conversion = parser.Parse("""
            map 'http://example.org/maps/string' = 'String'
            uses 'http://hl7.org/fhir/4.0/StructureDefinition/string' alias stringR4 as source
            uses 'http://hl7.org/fhir/3.0/StructureDefinition/string' alias stringR3 as target
            group string(source src : stringR4, target tgt : stringR3) <<types>> {
              src.value as v -> tgt.value = v;
            }
            """);
        var map = parser.Parse("""
            map 'http://example.org/xver-careplan-schedule' = 'CarePlanSchedule'
            uses 'http://hl7.org/fhir/4.0/StructureDefinition/CarePlan' alias CarePlan as source
            uses 'http://hl7.org/fhir/3.0/StructureDefinition/CarePlan' alias CarePlanR3 as target
            imports 'http://example.org/maps/string'
            group Main(source src : CarePlan, target tgt : CarePlanR3) {
              src.activity as s -> tgt.activity as t then activity(s, t);
            }
            group activity(source src, target tgt) {
              src.detail as s -> tgt.detail as t then detail(s, t);
            }
            group detail(source src, target tgt) {
              src.scheduled : string as s -> tgt.scheduled = create('string') as t then string(s, t);
            }
            """);
        var registry = new MapRegistry();
        registry.Register(conversion);
        var resolver = new ImportResolver(registry, parser);
        await resolver.ResolveImportsAsync(map);
        var source = ResourceJsonNode.Parse("""
            {"resourceType":"CarePlan","activity":[{"detail":{"scheduledString":"Scheduled visit"}}]}
            """);
        var target = ResourceJsonNode.Parse("""{"resourceType":"CarePlan"}""");
        var mutator = CreateMutator(targetSchema);
        var context = CreateContext(source, target, sourceSchema, targetSchema);
        context.ErrorMode = ErrorMode.Strict;
        context.ResourceCreator = type => mutator.CreateElement(type);

        new MappingEvaluator(MappingEvaluatorOptions.Default, mutator, resolver).Execute(map, context);

        var actual = target.ToSourceNavigator().Meta<JsonNode>()!;
        actual.ToJsonString().ShouldBe(
            """{"resourceType":"CarePlan","activity":[{"detail":{"scheduledString":"Scheduled visit"}}]}""");
    }

    [Fact]
    public async Task GivenImportedXhtmlConversion_WhenMappingNarrativeDiv_ThenMarkupIsPreserved()
    {
        var sourceSchema = FhirVersion.R4.GetSchemaProvider();
        var targetSchema = FhirVersion.Stu3.GetSchemaProvider();
        var parser = new MappingParser();
        var conversion = parser.Parse("""
            map 'http://example.org/maps/xhtml' = 'Xhtml'
            uses 'http://hl7.org/fhir/4.0/StructureDefinition/xhtml' alias xhtmlR4 as source
            uses 'http://hl7.org/fhir/3.0/StructureDefinition/xhtml' alias xhtmlR3 as target
            group xhtml(source src : xhtmlR4, target tgt : xhtmlR3) <<types>> {
              src.value as v -> tgt.value = v;
            }
            """);
        var map = parser.Parse("""
            map 'http://example.org/xver-patient-narrative' = 'PatientNarrative'
            uses 'http://hl7.org/fhir/4.0/StructureDefinition/Patient' alias Patient as source
            uses 'http://hl7.org/fhir/3.0/StructureDefinition/Patient' alias PatientR3 as target
            imports 'http://example.org/maps/xhtml'
            group Main(source src : Patient, target tgt : PatientR3) {
              src.text.div as s -> tgt.text.div = create('xhtml') as t then xhtml(s, t);
            }
            """);
        var registry = new MapRegistry();
        registry.Register(conversion);
        var resolver = new ImportResolver(registry, parser);
        await resolver.ResolveImportsAsync(map);
        var source = ResourceJsonNode.Parse(
            """{"resourceType":"Patient","text":{"status":"generated","div":"<div xmlns=\"http://www.w3.org/1999/xhtml\"><p>Visible narrative</p></div>"}}""");
        var div = source.ToElement(sourceSchema).Children("text").ShouldHaveSingleItem().Children("div").ShouldHaveSingleItem();
        div.InstanceType.ShouldBe("xhtml");
        var target = ResourceJsonNode.Parse("""{"resourceType":"Patient"}""");
        var mutator = CreateMutator(targetSchema);
        var context = CreateContext(source, target, sourceSchema, targetSchema);
        context.ErrorMode = ErrorMode.Strict;
        context.ResourceCreator = type => mutator.CreateElement(type);

        new MappingEvaluator(MappingEvaluatorOptions.Default, mutator, resolver).Execute(map, context);

        target.ToSourceNavigator().Meta<JsonNode>()!["text"]!["div"]!.GetValue<string>().ShouldBe(
            "<div xmlns=\"http://www.w3.org/1999/xhtml\"><p>Visible narrative</p></div>");
    }

    [Fact]
    public async Task GivenR4StructureDefinitionDifferentialFixedQuantity_WhenMappedToStu3WithImportedQuantityGroup_ThenValueIsPreserved()
    {
        var sourceSchema = FhirVersion.R4.GetSchemaProvider();
        var targetSchema = FhirVersion.Stu3.GetSchemaProvider();
        var parser = new MappingParser();
        var quantityConversion = parser.Parse("""
            map 'http://example.org/maps/quantity-r4-to-stu3' = 'QuantityR4ToStu3'
            uses 'http://hl7.org/fhir/4.0/StructureDefinition/Quantity' alias QuantityR4 as source
            uses 'http://hl7.org/fhir/3.0/StructureDefinition/Quantity' alias QuantityStu3 as target
            group Quantity(source src : QuantityR4, target tgt : QuantityStu3) <<types>> {
              src.value -> tgt.value;
            }
            """);
        var map = parser.Parse("""
            map 'http://example.org/xver-structure-definition-fixed-quantity' = 'StructureDefinitionFixedQuantity'
            uses 'http://hl7.org/fhir/4.0/StructureDefinition/StructureDefinition' alias StructureDefinitionR4 as source
            uses 'http://hl7.org/fhir/3.0/StructureDefinition/StructureDefinition' alias StructureDefinitionStu3 as target
            imports 'http://example.org/maps/quantity-r4-to-stu3'
            group Main(source src : StructureDefinitionR4, target tgt : StructureDefinitionStu3) {
              src.differential as s -> tgt.differential as t then differential(s, t);
            }
            group differential(source src, target tgt) {
              src.element as s -> tgt.element as t then element(s, t);
            }
            group element(source src, target tgt) {
              src.fixed : Quantity as s -> tgt.fixed = create('Quantity') as t then Quantity(s, t);
            }
            """);
        var registry = new MapRegistry();
        registry.Register(quantityConversion);
        var resolver = new ImportResolver(registry, parser);
        await resolver.ResolveImportsAsync(map);
        var source = ResourceJsonNode.Parse("""
            {"resourceType":"StructureDefinition","differential":{"element":[{"id":"Observation.value","path":"Observation.value","fixedQuantity":{"value":4.5}}]}}
            """);
        var target = ResourceJsonNode.Parse("""{"resourceType":"StructureDefinition"}""");
        var mutator = CreateMutator(targetSchema);
        var context = CreateContext(source, target, sourceSchema, targetSchema);
        context.ErrorMode = ErrorMode.Strict;
        context.ResourceCreator = type => mutator.CreateElement(type);
        var fixedQuantity = source.ToElement(sourceSchema).Children("differential").ShouldHaveSingleItem()
            .Children("element").ShouldHaveSingleItem().Children("fixed").ShouldHaveSingleItem();
        fixedQuantity.InstanceType.ShouldBe("Quantity");
        fixedQuantity.Children("value").ShouldHaveSingleItem().Value.ShouldBe(4.5m);
        var refreshedFixedQuantity = mutator.Refresh(fixedQuantity);
        refreshedFixedQuantity.Children("value").ShouldHaveSingleItem().Value.ShouldBe(4.5m);

        new MappingEvaluator(MappingEvaluatorOptions.Default, mutator, resolver).Execute(map, context);

        target.ToSourceNavigator().Meta<JsonNode>()!["differential"]!["element"]![0]!["fixedQuantity"]!
            .ToJsonString().ShouldBe("""{"value":4.5}""");
    }

    [Fact]
    public void GivenR4BodyStructure_WhenMappingToStu3BodySite_ThenTargetUsesTheRenamedResourceType()
    {
        var sourceSchema = FhirVersion.R4.GetSchemaProvider();
        var targetSchema = FhirVersion.Stu3.GetSchemaProvider();
        var source = ResourceJsonNode.Parse(
            """{"resourceType":"BodyStructure","id":"body-1","active":true,"location":{"coding":[{"code":"arm"}]}}""");
        var target = ResourceJsonNode.Parse("""{"resourceType":"BodySite"}""");
        var map = new MappingParser().Parse("""
            map 'http://example.org/xver-body-structure' = 'BodyStructure'
            uses 'http://hl7.org/fhir/4.0/StructureDefinition/BodyStructure' alias BodyStructure as source
            uses 'http://hl7.org/fhir/3.0/StructureDefinition/BodySite' alias BodySiteR3 as target
            group Main(source src : BodyStructure, target tgt : BodySiteR3) {
              src.id -> tgt.id;
              src.active -> tgt.active;
              src.location -> tgt.code;
            }
            """);

        Execute(map, CreateContext(source, target, sourceSchema, targetSchema), CreateMutator(targetSchema));

        var actual = target.ToSourceNavigator().Meta<JsonNode>()!;
        actual["resourceType"]!.GetValue<string>().ShouldBe("BodySite");
        actual["id"]!.GetValue<string>().ShouldBe("body-1");
        actual["active"]!.GetValue<bool>().ShouldBeTrue();
        actual["code"]!["coding"]![0]!["code"]!.GetValue<string>().ShouldBe("arm");
    }

    [Fact]
    public async Task GivenImportedCanonicalConversion_WhenMappingToReference_ThenValueMetadataAndRenderedValueAreConverted()
    {
        var sourceSchema = FhirVersion.R4.GetSchemaProvider();
        var targetSchema = FhirVersion.Stu3.GetSchemaProvider();
        var parser = new MappingParser();
        var conversion = parser.Parse("""
            map 'http://example.org/maps/canonical-reference' = 'CanonicalReference'
            uses 'http://hl7.org/fhir/4.0/StructureDefinition/canonical' alias canonical as source
            uses 'http://hl7.org/fhir/3.0/StructureDefinition/Reference' alias ReferenceR3 as target
            group canonical2Reference(source src : canonical, target tgt : ReferenceR3) <<types>> {
              src.id -> tgt.id;
              src.extension where (url != 'http://hl7.org/fhir/StructureDefinition/rendered-value') -> tgt.extension;
              src.value as v -> tgt.reference as r, r.value = v "reference";
              src.extension as e where (url = 'http://hl7.org/fhir/StructureDefinition/rendered-value') then {
                e.value : string -> tgt.display;
              };
            }
            """);
        var map = parser.Parse("""
            map 'http://example.org/xver-code-system-reference' = 'CodeSystemReference'
            uses 'http://hl7.org/fhir/4.0/StructureDefinition/canonical' alias canonical as source
            uses 'http://hl7.org/fhir/3.0/StructureDefinition/Patient' alias PatientR3 as target
            imports 'http://example.org/maps/canonical-reference'
            group Main(source src : canonical, target tgt : PatientR3) {
              src -> tgt.managingOrganization;
            }
            """);
        var registry = new MapRegistry();
        registry.Register(conversion);
        var resolver = new ImportResolver(registry, parser);
        await resolver.ResolveImportsAsync(map);
        var source = ResourceJsonNode.Parse("""
            {"resourceType":"PlanDefinition","library":["http://example.org/codes"],"_library":[{"id":"canonical-id","extension":[{"url":"http://hl7.org/fhir/StructureDefinition/rendered-value","valueString":"Visible code system"},{"url":"http://example.org/source-extension","valueString":"extra metadata"}]}]}
            """);
        var target = ResourceJsonNode.Parse("""{"resourceType":"Patient"}""");
        var context = new MappingContext();
        context.SetSource("src", source.ToElement(sourceSchema).Children("library").ShouldHaveSingleItem());
        context.SetTarget("tgt", target.ToElement(targetSchema));
        context.GetSource("src")!.InstanceType.ShouldBe("canonical");
        var evaluator = new MappingEvaluator(MappingEvaluatorOptions.Default, CreateMutator(targetSchema), resolver);

        Execute(map, context, evaluator);

        var actual = target.ToSourceNavigator().Meta<JsonNode>()!["managingOrganization"]!;
        actual["id"]!.GetValue<string>().ShouldBe("canonical-id");
        actual["reference"]!.GetValue<string>().ShouldBe("http://example.org/codes");
        actual["display"]!.GetValue<string>().ShouldBe("Visible code system");
        actual["extension"]![0]!["url"]!.GetValue<string>().ShouldBe("http://example.org/source-extension");
        actual["extension"]![0]!["valueString"]!.GetValue<string>().ShouldBe("extra metadata");
    }

    [Fact]
    public async Task GivenImportedGroupsWithSameTypeNamesAcrossVersions_WhenMappingR4Canonical_ThenOnlyR4GroupRuns()
    {
        var sourceSchema = FhirVersion.R4.GetSchemaProvider();
        var targetSchema = FhirVersion.Stu3.GetSchemaProvider();
        var parser = new MappingParser();
        var r4Conversion = parser.Parse("""
            map 'http://example.org/maps/canonical-r4' = 'CanonicalR4'
            uses 'http://hl7.org/fhir/4.0/StructureDefinition/canonical' alias canonicalR4 as source
            uses 'http://hl7.org/fhir/3.0/StructureDefinition/Reference' alias ReferenceR3 as target
            group Convert(source src : canonicalR4, target tgt : ReferenceR3) <<types>> {
              src.value as v -> tgt.reference as r, r.value = v "reference";
            }
            """);
        var r5Conversion = parser.Parse("""
            map 'http://example.org/maps/canonical-r5' = 'CanonicalR5'
            uses 'http://hl7.org/fhir/5.0/StructureDefinition/canonical' alias canonicalR5 as source
            uses 'http://hl7.org/fhir/3.0/StructureDefinition/Reference' alias ReferenceR3 as target
            group Convert(source src : canonicalR5, target tgt : ReferenceR3) <<types>> {
              src.value -> tgt.display;
            }
            """);
        var map = parser.Parse("""
            map 'http://example.org/xver-versioned-imports' = 'VersionedImports'
            uses 'http://hl7.org/fhir/4.0/StructureDefinition/canonical' alias canonical as source
            uses 'http://hl7.org/fhir/3.0/StructureDefinition/Patient' alias PatientR3 as target
            imports 'http://example.org/maps/canonical-*'
            group Main(source src : canonical, target tgt : PatientR3) {
              src -> tgt.managingOrganization;
            }
            """);
        var registry = new MapRegistry();
        registry.Register(r4Conversion);
        registry.Register(r5Conversion);
        var resolver = new ImportResolver(registry, parser);
        await resolver.ResolveImportsAsync(map);
        var source = ResourceJsonNode.Parse("""{"resourceType":"PlanDefinition","library":["http://example.org/codes"]}""");
        var target = ResourceJsonNode.Parse("""{"resourceType":"Patient"}""");
        var context = new MappingContext();
        context.SetSource("src", source.ToElement(sourceSchema).Children("library").ShouldHaveSingleItem());
        context.SetTarget("tgt", target.ToElement(targetSchema));
        var evaluator = new MappingEvaluator(MappingEvaluatorOptions.Default, CreateMutator(targetSchema), resolver);

        Execute(map, context, evaluator);

        var actual = target.ToSourceNavigator().Meta<JsonNode>()!["managingOrganization"]!;
        actual["reference"]!.GetValue<string>().ShouldBe("http://example.org/codes");
        actual["display"].ShouldBeNull();
    }

    [Fact]
    public void GivenSourceAndTargetAreTheSameJson_WhenAWriteIsFollowedByAnotherSourceRead_ThenTheLaterReadSeesTheWrite()
    {
        var schema = FhirVersion.R4.GetSchemaProvider();
        var mutator = CreateMutator(schema);
        var resource = ResourceJsonNode.Parse(
            """{"resourceType":"Patient","id":"Old","name":[{"family":"New"}]}""");
        var element = resource.ToElement(schema);
        var context = new MappingContext();
        context.SetSource("src", element);
        context.SetTarget("tgt", element);

        Execute(ParseMap("src.name.family -> tgt.id;\nsrc.id -> tgt.name.given;"), context, mutator);

        var actualJson = resource.ToSourceNavigator().Meta<JsonNode>()!.ToJsonString();
        actualJson.ShouldBe(
            """{"resourceType":"Patient","id":"New","name":[{"family":"New"},{"given":["New"]}]}""");
    }

    [Fact]
    public void GivenDependentGroupUsesSourceAsTarget_WhenQualifiedPathsAreWritten_ThenSourceAliasesRemainLive()
    {
        var schema = FhirVersion.R4.GetSchemaProvider();
        var mutator = CreateMutator(schema);
        var source = ResourceJsonNode.Parse(
            """{"resourceType":"Patient","id":"Old","name":[{"family":"New"}]}""");
        var target = ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"separate-target"}""");
        var context = CreateContext(source, target, schema, schema);
        var map = ParseMap(
            "src -> tgt then Mutate(src, src);",
            """
            group Mutate(source src : Patient, target tgt : Patient) {
              src.name.family -> tgt.id;
              src.id -> tgt.name.given;
            }
            """);

        Execute(map, context, mutator);

        var actualSourceJson = source.ToSourceNavigator().Meta<JsonNode>()!.ToJsonString();
        actualSourceJson.ShouldBe(
            """{"resourceType":"Patient","id":"New","name":[{"family":"New"},{"given":["New"]}]}""");
        target.ToSourceNavigator().Meta<JsonNode>()!.ToJsonString().ShouldBe(
            """{"resourceType":"Patient","id":"separate-target"}""");
    }

    [Fact]
    public void GivenTargetChildrenWereReadBeforeMutation_WhenTargetChanges_ThenRefreshReturnsLatestValue()
    {
        var schema = FhirVersion.R4.GetSchemaProvider();
        var mutator = CreateMutator(schema);
        var patient = mutator.CreateElement("Patient");
        var priorNames = patient.Children("name");
        var name = mutator.SetTarget(patient, "name", mutator.CreateElement("HumanName",
            new Dictionary<string, object?> { ["family"] = "Initial" }));

        mutator.SetTarget(name, "family", mutator.CreateElement("string", "Latest"));

        priorNames.ShouldBeEmpty();
        var latestName = mutator.Refresh(patient).Children("name").ShouldHaveSingleItem();
        latestName.Children("family").ShouldHaveSingleItem().Value.ShouldBe("Latest");
        patient.Meta<JsonNode>()!["name"]![0]!["family"]!.GetValue<string>().ShouldBe("Latest");
    }

    private static JsonNodeMutator CreateMutator(ISchema schema) =>
        new(new FhirPathEvaluator(), new FhirPathParser(), () => schema);

    private static MappingContext CreateContext(
        ResourceJsonNode source,
        ResourceJsonNode target,
        ISchema sourceSchema,
        ISchema targetSchema)
    {
        var context = new MappingContext();
        context.SetSource("src", source.ToElement(sourceSchema));
        context.SetTarget("tgt", target.ToElement(targetSchema));
        return context;
    }

    private static MapExpression ParseMap(string rules, string additionalGroups = "") =>
        new MappingParser().Parse($$"""
            map 'http://example.org/xver-optimization-guards' = 'XverOptimizationGuards'
            group Main(source src : Patient, target tgt : Patient) {
              {{rules}}
            }
            {{additionalGroups}}
            """);

    private static void Execute(MapExpression map, MappingContext context, IMappingTargetMutator mutator) =>
        new MappingEvaluator(MappingEvaluatorOptions.Default, mutator).Execute(map, context);

    private static void Execute(MapExpression map, MappingContext context, MappingEvaluator evaluator) =>
        evaluator.Execute(map, context);

    private sealed class RefreshCountingMutator(IMappingTargetMutator inner) : IMappingTargetMutator
    {
        private readonly List<IElement> _refreshedElements = [];

        public int RefreshCount(IElement element) =>
            _refreshedElements.Count(refreshed => ReferenceEquals(refreshed, element));

        public IElement CreateElement(string typeName, object? value = null) => inner.CreateElement(typeName, value);
        public IType GetTargetDefinition(IElement parent, string propertyName) => inner.GetTargetDefinition(parent, propertyName);
        public string GetTargetType(IElement parent, string propertyName) => inner.GetTargetType(parent, propertyName);
        public IElement SetTarget(IElement parent, string propertyName, IElement? value,
            PropertyMutationMode mode = PropertyMutationMode.AutoDetect, bool insertFirst = false) =>
            inner.SetTarget(parent, propertyName, value, mode, insertFirst);
        public IElement Refresh(IElement element)
        {
            _refreshedElements.Add(element);
            return inner.Refresh(element);
        }
        public void MoveTarget(IElement element, int? index = null) => inner.MoveTarget(element, index);
        public void SetProperty(ResourceJsonNode resource, string path, IElement value,
            PropertyMutationMode mode = PropertyMutationMode.AutoDetect) => inner.SetProperty(resource, path, value, mode);
        public void SetProperty(ResourceJsonNode resource, string path, JsonNode value,
            PropertyMutationMode mode = PropertyMutationMode.AutoDetect) => inner.SetProperty(resource, path, value, mode);
        public JsonNode EnsurePropertyPath(ResourceJsonNode resource, string path) => inner.EnsurePropertyPath(resource, path);
        public void DeleteProperty(ResourceJsonNode resource, string path) => inner.DeleteProperty(resource, path);
        public void InsertIntoArray(ResourceJsonNode resource, string path, JsonNode value, int index) =>
            inner.InsertIntoArray(resource, path, value, index);
        public void ReplaceArrayElement(ResourceJsonNode resource, string path, JsonNode value) =>
            inner.ReplaceArrayElement(resource, path, value);
        public IEnumerable<JsonNode> Evaluate(ResourceJsonNode resource, string path) => inner.Evaluate(resource, path);
        public JsonNode EvaluateSingle(ResourceJsonNode resource, string path) => inner.EvaluateSingle(resource, path);
    }
    private sealed class ImmutableElement(
        string name,
        string instanceType,
        object? value = null,
        IReadOnlyList<IElement>? children = null) : IElement
    {
        private readonly IReadOnlyList<IElement> _children = children ?? [];

        public string Name { get; } = name;
        public string InstanceType { get; } = instanceType;
        public object? Value { get; } = value;
        public string Location => Name;
        public IType? Type => null;
        public bool HasPrimitiveValue => Value is not null;
        public IReadOnlyList<IElement> Children(string? name = null) => name is null
            ? _children
            : _children.Where(child => child.Name == name).ToList();
        public T? Meta<T>() where T : class => null;
    }
}
