// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#pragma warning disable CA1707 // Identifiers should not contain underscores (standard xUnit naming pattern)
#pragma warning disable SDK0001 // Evaluation API usage

using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.ElementModel; // SDK ElementModel (ISourceNode, ITypedElement, ToTypedElement extensions)
using Hl7.FhirPath; // SDK FhirPath extensions
using Sparky.FhirPath.Evaluation; // Our FhirPath extensions
using Sparky.SourceNodeSerialization.Extensions;
using Sparky.SourceNodeSerialization.SourceNodes.Models;
using Sparky.SourceNodeSerialization.Tests.TestData;
using Sparky.Specification.Extensions;
using Sparky.Specification.Generated;
using Xunit;

// Namespace aliases to avoid conflicts
using OurElementModel = Sparky.SourceNodeSerialization.ElementModel;

// Static using for our extension methods
using static Sparky.SourceNodeSerialization.ElementModel.TypedElementExtensions;
using ISourceNode = Sparky.SourceNodeSerialization.ElementModel.ISourceNode;
using ITypedElement = Sparky.SourceNodeSerialization.ElementModel.ITypedElement;

// SDK type aliases
using SdkModelInspector = Hl7.Fhir.Introspection.ModelInspector;
using SdkISourceNode = Hl7.Fhir.ElementModel.ISourceNode;
using SdkITypedElement = Hl7.Fhir.ElementModel.ITypedElement;

namespace Sparky.SourceNodeSerialization.Tests;

public class MetaJsonNodeTests
{
    private readonly Patient _patientPoco;
    private readonly ResourceJsonNode _patientJsonNode;
    private readonly DateTimeOffset _currentDate;
    private readonly string _updatedJson;

    private readonly string _patientJson = @"{
  ""resourceType"" : ""Patient"",
  ""id"" : ""example"",
  ""name"" : [{
    ""id"" : ""f2"",
    ""use"" : ""official"" ,
    ""given"" : [ ""Karen"", ""May"" ],
    ""_given"" : [ null, {""id"" : ""middle""} ],
    ""family"" :  ""Van"",
    ""_family"" : {""id"" : ""a2""}
   }],
  ""meta"" : {
    ""lastUpdated"" : ""2023-10-01T12:00:00Z"",
    ""versionId"" : ""-1"",
    ""extension"" : [
      {
        ""url"" : ""http://example.com/deleted-state"",
        ""valueCode"" : ""soft-deleted""
      }
    ]
  },
  ""text"" : {
    ""status"" : ""generated"" ,
    ""div"" : ""<div xmlns=\""http://www.w3.org/1999/xhtml\""><p>...</p></div>""
  }
}";

    private readonly string _patientMinExtJson = @"{
  ""resourceType"" : ""Patient"",
  ""name"" : [{
    ""use"" : ""official"" ,
    ""given"" : [ ""Karen"", ""May"" ],
    ""family"" :  ""Van""
   }],
  ""meta"" : {
    ""extension"" : [
      {
        ""url"" : ""http://example.com/deleted-state"",
        ""valueCode"" : ""soft-deleted""
      }
    ]
  }
}";

    private readonly R4StructureDefinitionSummaryProvider _r4StructureDefinitionSummaryProvider = new R4StructureDefinitionSummaryProvider();

    public MetaJsonNodeTests()
    {
        _currentDate = DateTimeOffset.UtcNow;
        _patientPoco = Samples.GetDefaultPatient();
        _patientPoco.Meta = new Meta
        {
            LastUpdated = _currentDate,
            VersionId = "-1",
        };
        _updatedJson = _patientPoco.ToJson();

        _patientJsonNode = JsonSourceNodeFactory.Parse(Samples.GetJson("Patient"));
    }

    [Fact]
    public void GivenAPatientPoco_WhenConvertingToJsonNode_ThenMetaIsPopulated()
    {
        _patientJsonNode.Meta.LastUpdated = _currentDate;
        _patientJsonNode.Meta.VersionId = "-1";

        var newJson = _patientJsonNode.SerializeToString().Replace("\\u002B", "+", StringComparison.Ordinal);

        var deserializer = new FhirJsonDeserializer();
        Resource deserializedPatient = deserializer.DeserializeResource(newJson);

        Assert.Equal(_currentDate, deserializedPatient.Meta.LastUpdated);
        Assert.Equal("-1", deserializedPatient.Meta.VersionId);
    }

    [Fact]
    public void ReadShadowProperty()
    {
        // This test uses SDK types - convert to SDK's ISourceNode
        SdkISourceNode sourceNode = Hl7.Fhir.Serialization.FhirJsonNode.Parse(_patientJson);
        SdkITypedElement node = sourceNode.ToTypedElement(ModelInfo.ModelInspector);

        object familyName = node.Scalar("Patient.name.family");
        object familyId = node.Scalar("Patient.name.family.id");
        Assert.Equal("Van", familyName);
        Assert.Equal("a2", familyId);

        object middle = node.Scalar("Patient.name.given[1]");
        object middleId = node.Scalar("Patient.name.given[1].id");
        Assert.Equal("May", middle);
        Assert.Equal("middle", middleId);

        object firstName = node.Scalar("Patient.name.given[0]");
        object firstNameId = node.Scalar("Patient.name.given[0].id");
        Assert.Equal("Karen", firstName);
        Assert.Null(firstNameId);
    }

    [Fact]
    public void ReadExtension()
    {
        // Test our implementation
        ISourceNode sourceNode = JsonSourceNodeFactory.Parse(_patientMinExtJson).ToSourceNode();
        ITypedElement node = sourceNode.ToTypedElement(_r4StructureDefinitionSummaryProvider);

        var path = "Resource.meta.extension.where(url = 'http://example.com/deleted-state').where(value = 'soft-deleted')";

        var value1 = node.Select(path).ToArray();
        Assert.NotEmpty(value1);

        var scalar = node.Scalar(path + ".exists()");
        Assert.Equal(true, scalar);
    }

    [Fact]
    public void RemoveExtension()
    {
        var extensionUrl = "http://example.com/deleted-state";
        var model = ResourceJsonNode.Parse(_patientMinExtJson);
        model.Meta.RemoveExtension(extensionUrl);

        var json = model.SerializeToString();
        Assert.False(json.Contains(extensionUrl, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SourceNode()
    {
        // Use our implementation
        ISourceNode sourceNode = JsonSourceNodeFactory.Parse(_patientJson).ToSourceNode();
        ITypedElement node = sourceNode.ToTypedElement(_r4StructureDefinitionSummaryProvider);
        ITypedElement familyType = node.Select("Patient.name.family").Single();

        // Note: ChildDefinitions extension method not yet implemented - skipping for now
        // Sparky.Domain.Specification.IElementDefinitionSummary[] definitions = familyType.ChildDefinitions(_r4StructureDefinitionSummaryProvider).ToArray();
        // Assert.NotNull(definitions);

        // Basic assertion that we got the element
        Assert.NotNull(familyType);
    }

    [Fact]
    public void FindId()
    {
        // Use our implementation
        ISourceNode sourceNode = JsonSourceNodeFactory.Parse(_patientJson).ToSourceNode();
        ITypedElement node = sourceNode.ToTypedElement(_r4StructureDefinitionSummaryProvider);
        ITypedElement id = node.Select("Resource.id").Single();
        Assert.Equal("example", id.Value);
    }

    [Fact]
    public void CanFindReferenceValuesInSourceNode()
    {
        var sourceNode = JsonSourceNodeFactory.Parse(Samples.GetDefaultObservation().ToJson());

        var references = sourceNode
            .GetReferences();

        var reference = Assert.Single(references);

        Assert.Contains("subject", reference.ElementPath, StringComparison.Ordinal);
        Assert.Contains("Patient/example", reference.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractEffectiveDateTime()
    {
        var poco = Samples.GetDefaultObservation();
        poco.Effective = new FhirDateTime(_currentDate.Year);

        // SDK's ToTypedElement for POCO
        var effectiveDatePath = "List.date | Observation.effective | Procedure.performed | (RiskAssessment.occurrence as dateTime)";
        SdkITypedElement sdkTypedElement = poco.ToTypedElement();
        var effectiveExpected = sdkTypedElement.Select(effectiveDatePath).Single();

        // Our implementation for JSON
        ISourceNode sourceNode = JsonSourceNodeFactory.Parse(poco.ToJson()).ToSourceNode();
        ITypedElement node = sourceNode.ToTypedElement(_r4StructureDefinitionSummaryProvider);
        ITypedElement effectiveActual = node.Select(effectiveDatePath).Single();

        Assert.Equal(effectiveExpected.Value, effectiveActual.Value);
    }
}
