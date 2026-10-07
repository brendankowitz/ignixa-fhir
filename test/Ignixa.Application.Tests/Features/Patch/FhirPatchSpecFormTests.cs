// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Patch;
using Ignixa.Application.Features.Patch.Executors;
using Ignixa.Application.Features.Patch.Validation;
using Ignixa.Application.Features.Search;
using Ignixa.FhirMappingLanguage.Mutator;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Serialization.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Features.Patch;

/// <summary>
/// Spec-form FHIRPath Patch (http://hl7.org/fhir/fhirpatch.html): 'add' with a 'name' part, anonymous-type
/// values supplied as nested parts, and schema-declared cardinality.
/// </summary>
public sealed class FhirPatchSpecFormTests : IDisposable
{
    private readonly FhirVersionContext _versionContext = new(
        Substitute.For<ILoggerFactory>(),
        new Ignixa.Search.Definition.SearchParameterResolutionOptions(),
        NullFhirBaseUriProvider.Instance);

    private readonly FhirPatchParametersParser _parser = new();

    public void Dispose() => _versionContext.Dispose();

    [Theory]
    [InlineData(FhirVersion.Stu3)]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    public async Task GivenListEntryAppendsAsNestedParts_WhenPatching_ThenEntriesAreAppendedInOrder(FhirVersion version)
    {
        // Arrange
        var list = Parse("""{"resourceType":"List","id":"session","status":"current","mode":"working","entry":[{"item":{"reference":"Encounter/existing"}}]}""");
        var patch = Parameters(AppendListEntry("DocumentReference/doc-1"), AppendListEntry("Composition/comp-1"));

        // Act
        var result = await ApplyAsync(list, patch, version);

        // Assert
        var entries = result.MutableNode()["entry"]!.AsArray();
        entries.Select(entry => entry!["item"]!["reference"]!.GetValue<string>())
            .ShouldBe(["Encounter/existing", "DocumentReference/doc-1", "Composition/comp-1"]);
    }

    [Fact]
    public async Task GivenListWithoutEntries_WhenAppendingAsNestedParts_ThenEntryArrayIsCreated()
    {
        // Arrange
        var list = Parse("""{"resourceType":"List","id":"session","status":"current","mode":"working"}""");

        // Act
        var result = await ApplyAsync(list, Parameters(AppendListEntry("DocumentReference/doc-1")), FhirVersion.R4);

        // Assert
        var entries = result.MutableNode()["entry"].ShouldBeOfType<JsonArray>();
        entries.Count.ShouldBe(1);
        entries[0]!["item"]!["reference"]!.GetValue<string>().ShouldBe("DocumentReference/doc-1");
    }

    [Fact]
    public async Task GivenAbsentSingleValuedElement_WhenAddingByName_ThenItIsSetAsAScalar()
    {
        // Arrange
        var patient = Parse("""{"resourceType":"Patient","id":"p"}""");
        var patch = Parameters(Operation("add", "Patient", "birthDate", """{"name":"value","valueDate":"1930-01-01"}"""));

        // Act
        var result = await ApplyAsync(patient, patch, FhirVersion.R4);

        // Assert
        result.MutableNode()["birthDate"]!.GetValue<string>().ShouldBe("1930-01-01");
    }

    [Fact]
    public async Task GivenPresentSingleValuedElement_WhenAddingByName_ThenPatchIsRejected()
    {
        // Arrange
        var patient = Parse("""{"resourceType":"Patient","id":"p","birthDate":"1930-01-01"}""");
        var patch = Parameters(Operation("add", "Patient", "birthDate", """{"name":"value","valueDate":"1940-01-01"}"""));

        // Act & Assert
        var exception = await Should.ThrowAsync<FhirPatchException>(() => ApplyAsync(patient, patch, FhirVersion.R4));
        exception.Message.ShouldContain("does not repeat and already has a value");
        patient.MutableNode()["birthDate"]!.GetValue<string>().ShouldBe("1930-01-01");
    }

    [Fact]
    public async Task GivenChoiceElement_WhenAddingByName_ThenTypedPropertyIsSet()
    {
        // Arrange
        var patient = Parse("""{"resourceType":"Patient","id":"p"}""");
        var patch = Parameters(Operation("add", "Patient", "deceased", """{"name":"value","valueDateTime":"2015-02-14T13:42:00+10:00"}"""));

        // Act
        var result = await ApplyAsync(patient, patch, FhirVersion.R4);

        // Assert
        result.MutableNode()["deceasedDateTime"]!.GetValue<string>().ShouldBe("2015-02-14T13:42:00+10:00");
    }

    [Theory]
    [InlineData("""{"name":"value","valueDateTime":"2015-02-14T13:42:00+10:00"}""")]
    [InlineData("""{"name":"value","valueBoolean":false}""")]
    public async Task GivenAnyChoiceVariantPresent_WhenAddingTheChoiceByName_ThenPatchIsRejected(string valuePart)
    {
        // Arrange
        var patient = Parse("""{"resourceType":"Patient","id":"p","deceasedBoolean":true}""");
        var patch = Parameters(Operation("add", "Patient", "deceased", valuePart));

        // Act & Assert
        var exception = await Should.ThrowAsync<FhirPatchException>(() => ApplyAsync(patient, patch, FhirVersion.R4));
        exception.Message.ShouldContain("deceasedBoolean");
        patient.MutableNode().ContainsKey("deceasedDateTime").ShouldBeFalse();
    }

    [Fact]
    public async Task GivenChoiceValueOfDisallowedType_WhenAddingByName_ThenPatchIsRejected()
    {
        // Arrange
        var patient = Parse("""{"resourceType":"Patient","id":"p"}""");
        var patch = Parameters(Operation("add", "Patient", "deceased", """{"name":"value","valueString":"yes"}"""));

        // Act & Assert
        var exception = await Should.ThrowAsync<FhirPatchException>(() => ApplyAsync(patient, patch, FhirVersion.R4));
        exception.Message.ShouldContain("is not allowed for choice element 'deceased'");
    }

    [Fact]
    public async Task GivenIndexedParent_WhenAddingByName_ThenOnlyThatElementIsChanged()
    {
        // Arrange
        var patient = Parse("""{"resourceType":"Patient","id":"p","name":[{"family":"Doe"},{"given":["Jane"]}]}""");
        var patch = Parameters(Operation("add", "Patient.name[1]", "family", """{"name":"value","valueString":"Smith"}"""));

        // Act
        var result = await ApplyAsync(patient, patch, FhirVersion.R4);

        // Assert
        var names = result.MutableNode()["name"]!.AsArray();
        names[0]!["family"]!.GetValue<string>().ShouldBe("Doe");
        names[1]!["family"]!.GetValue<string>().ShouldBe("Smith");
        result.MutableNode().Select(property => property.Key).ShouldBe(["resourceType", "id", "name"], ignoreOrder: true);
    }

    [Fact]
    public async Task GivenFilteredParent_WhenAddingRepeatingElementByName_ThenItIsAppendedToTheMatch()
    {
        // Arrange
        var patient = Parse("""{"resourceType":"Patient","id":"p","name":[{"use":"usual","given":["Jo"]},{"use":"official","given":["Joanna"]}]}""");
        var patch = Parameters(Operation("add", "Patient.name.where(use = 'official')", "given", """{"name":"value","valueString":"Marie"}"""));

        // Act
        var result = await ApplyAsync(patient, patch, FhirVersion.R4);

        // Assert
        var names = result.MutableNode()["name"]!.AsArray();
        names[0]!["given"]!.AsArray().Select(given => given!.GetValue<string>()).ShouldBe(["Jo"]);
        names[1]!["given"]!.AsArray().Select(given => given!.GetValue<string>()).ShouldBe(["Joanna", "Marie"]);
    }

    [Fact]
    public async Task GivenParentMatchingSeveralElements_WhenAddingByName_ThenPatchIsRejected()
    {
        // Arrange
        var patient = Parse("""{"resourceType":"Patient","id":"p","name":[{"given":["A"]},{"given":["B"]}]}""");
        var patch = Parameters(Operation("add", "Patient.name", "family", """{"name":"value","valueString":"Smith"}"""));

        // Act & Assert
        var exception = await Should.ThrowAsync<FhirPatchException>(() => ApplyAsync(patient, patch, FhirVersion.R4));
        exception.Message.ShouldContain("must resolve to a single element, but matched 2");
    }

    [Fact]
    public async Task GivenPrimitivePresentOnlyAsExtension_WhenAddingItByName_ThenPatchIsRejected()
    {
        // Arrange: _birthDate carries an extension, so the element exists even without a value
        var patient = Parse("""{"resourceType":"Patient","id":"p","_birthDate":{"extension":[{"url":"http://example.org/unknown","valueBoolean":true}]}}""");
        var patch = Parameters(Operation("add", "Patient", "birthDate", """{"name":"value","valueDate":"1930-01-01"}"""));

        // Act & Assert
        var exception = await Should.ThrowAsync<FhirPatchException>(() => ApplyAsync(patient, patch, FhirVersion.R4));
        exception.Message.ShouldContain("already has a value");
    }

    [Fact]
    public async Task GivenPathOnlyReplaceAfterNamedAdd_WhenPatching_ThenReplaceSeesTheAddedElement()
    {
        // Arrange
        var patient = Parse("""{"resourceType":"Patient","id":"p","name":[{"given":["Jo"]}]}""");
        var patch = Parameters(
            Operation("add", "Patient.name.where(given = 'Jo')", "family", """{"name":"value","valueString":"Smith"}"""),
            """{"name":"operation","part":[{"name":"type","valueCode":"replace"},{"name":"path","valueString":"Patient.name[0].family"},{"name":"value","valueString":"Jones"}]}""");

        // Act
        var result = await ApplyAsync(patient, patch, FhirVersion.R4);

        // Assert
        result.MutableNode()["name"]![0]!["family"]!.GetValue<string>().ShouldBe("Jones");
        result.MutableNode().Select(property => property.Key).ShouldBe(["resourceType", "id", "name"], ignoreOrder: true);
    }

    [Fact]
    public async Task GivenOperationDependingOnAnEarlierOne_WhenPatching_ThenItSeesTheEarlierResult()
    {
        // Arrange
        var patient = Parse("""{"resourceType":"Patient","id":"p","name":[{"use":"usual","given":["Jo"]}]}""");
        var patch = Parameters(
            """{"name":"operation","part":[{"name":"type","valueCode":"replace"},{"name":"path","valueString":"Patient.name[0].use"},{"name":"value","valueCode":"official"}]}""",
            Operation("add", "Patient.name.where(use = 'official')", "family", """{"name":"value","valueString":"Smith"}"""));

        // Act
        var result = await ApplyAsync(patient, patch, FhirVersion.R4);

        // Assert
        result.MutableNode()["name"]![0]!["family"]!.GetValue<string>().ShouldBe("Smith");
    }

    [Fact]
    public async Task GivenRecursiveNestedParts_WhenAddingByName_ThenValueIsShapedBySchema()
    {
        // Arrange
        var patient = Parse("""{"resourceType":"Patient","id":"p"}""");
        var patch = Parameters(Operation("add", "Patient", "contact", """
            {"name":"value","part":[
              {"name":"name","part":[
                {"name":"family","valueString":"Doe"},
                {"name":"given","valueString":"Ann"},
                {"name":"given","valueString":"Marie"}]},
              {"name":"gender","valueCode":"female"}]}
            """));

        // Act
        var result = await ApplyAsync(patient, patch, FhirVersion.R4);

        // Assert
        var contact = result.MutableNode()["contact"]!.AsArray().ShouldHaveSingleItem()!;
        contact["gender"]!.GetValue<string>().ShouldBe("female");
        contact["name"]!["family"]!.GetValue<string>().ShouldBe("Doe");
        contact["name"]!["given"]!.AsArray().Select(given => given!.GetValue<string>()).ShouldBe(["Ann", "Marie"]);
    }

    [Fact]
    public async Task GivenNonRepeatingNestedPartSuppliedTwice_WhenPatching_ThenPatchIsRejected()
    {
        // Arrange
        var patient = Parse("""{"resourceType":"Patient","id":"p"}""");
        var patch = Parameters(Operation("add", "Patient", "contact",
            """{"name":"value","part":[{"name":"gender","valueCode":"female"},{"name":"gender","valueCode":"male"}]}"""));

        // Act & Assert
        var exception = await Should.ThrowAsync<FhirPatchException>(() => ApplyAsync(patient, patch, FhirVersion.R4));
        exception.Message.ShouldContain("does not repeat but was supplied more than once");
    }

    [Fact]
    public async Task GivenNestedPartNamingNoElement_WhenPatching_ThenPatchIsRejected()
    {
        // Arrange: Patient.link has 'type' and 'other' children, not 'value'
        var patient = Parse("""{"resourceType":"Patient","id":"p"}""");
        var patch = Parameters(Operation("add", "Patient", "link",
            """{"name":"value","part":[{"name":"value","valueCode":"replaced-by"},{"name":"value","valueReference":{"reference":"Patient/123"}}]}"""));

        // Act & Assert
        var exception = await Should.ThrowAsync<FhirPatchException>(() => ApplyAsync(patient, patch, FhirVersion.R4));
        exception.Message.ShouldContain("'value' is not an element of Patient.link");
    }

    [Fact]
    public async Task GivenPathOnlyAdd_WhenPatching_ThenValueIsStillAppended()
    {
        // Arrange
        var patient = Parse("""{"resourceType":"Patient","id":"p","name":[{"family":"Doe"}]}""");
        var patch = Parameters("""{"name":"operation","part":[{"name":"type","valueCode":"add"},{"name":"path","valueString":"Patient.name"},{"name":"value","valueHumanName":{"family":"Smith"}}]}""");

        // Act
        var result = await ApplyAsync(patient, patch, FhirVersion.R4);

        // Assert
        result.MutableNode()["name"]!.AsArray().Select(name => name!["family"]!.GetValue<string>()).ShouldBe(["Doe", "Smith"]);
    }

    [Theory]
    [InlineData("""{"name":"operation","part":[{"name":"type","valueCode":"add"},{"name":"path","valueString":"List"},{"name":"name","valueString":"entry"},{"name":"value"}]}""", "must have a value[x] or nested parts")]
    [InlineData("""{"name":"operation","part":[{"name":"type","valueCode":"add"},{"name":"path","valueString":"List"},{"name":"name","valueInteger":1},{"name":"value","valueString":"x"}]}""", "'name' part must have a non-empty valueString")]
    [InlineData("""{"name":"operation","part":[{"name":"type","valueCode":"replace"},{"name":"path","valueString":"List.title"},{"name":"name","valueString":"title"},{"name":"value","valueString":"x"}]}""", "only valid for add operations")]
    [InlineData("""{"name":"operation","part":[{"name":"type","valueCode":"add"},{"name":"path","valueString":"List"},{"name":"name","valueString":"entry"},{"name":"value","valueString":"x","part":[{"name":"item","valueReference":{"reference":"Patient/1"}}]}]}""", "either value[x] or nested parts, not both")]
    [InlineData("""{"name":"operation","part":[{"name":"type","valueCode":"add"},{"name":"path","valueString":"List"},{"name":"name","valueString":"entry"},{"name":"value","part":[{"name":"item","valueString":"a","valueReference":{"reference":"Patient/1"}}]}]}""", "must have a single value[x]")]
    public void GivenMalformedOperation_WhenParsing_ThenPatchIsRejected(string operation, string expectedMessage)
    {
        // Act & Assert
        var exception = Should.Throw<FhirPatchException>(() => _parser.Parse(Parameters(operation)));
        exception.Message.ShouldContain(expectedMessage);
    }

    private async Task<ResourceJsonNode> ApplyAsync(ResourceJsonNode resource, ResourceJsonNode patch, FhirVersion version)
    {
        var operations = _parser.Parse(patch);
        new FhirPatchValidator().Validate(operations);
        return await CreateEngine(version).ApplyPatchAsync(resource, operations,
            _versionContext.GetBaseSchemaProvider(version), CancellationToken.None);
    }

    private FhirPatchEngine CreateEngine(FhirVersion version)
    {
        var mutator = new JsonNodeMutator(
            new Ignixa.FhirPath.Evaluation.FhirPathEvaluator(),
            new FhirPathParser(),
            () => _versionContext.GetBaseSchemaProvider(version));
        return new FhirPatchEngine(NullLogger<FhirPatchEngine>.Instance,
        [
            new AddOperationExecutor(NullLogger<AddOperationExecutor>.Instance, mutator),
            new ReplaceOperationExecutor(NullLogger<ReplaceOperationExecutor>.Instance, mutator),
        ]);
    }

    private static string AppendListEntry(string reference) =>
        Operation("add", "List", "entry",
            $$$"""{"name":"value","part":[{"name":"item","valueReference":{"reference":"{{{reference}}}"}}]}""");

    private static string Operation(string type, string path, string name, string valuePart) =>
        $$"""{"name":"operation","part":[{"name":"type","valueCode":"{{type}}"},{"name":"path","valueString":"{{path}}"},{"name":"name","valueString":"{{name}}"},{{valuePart}}]}""";

    private static ResourceJsonNode Parameters(params string[] operations) =>
        Parse($$"""{"resourceType":"Parameters","parameter":[{{string.Join(",", operations)}}]}""");

    private static ResourceJsonNode Parse(string json) => JsonSourceNodeFactory.Parse(json);
}
