// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Domain.ElementModel;
using Ignixa.Domain.Specification;
using Hl7.FhirPath;
using Ignixa.Domain;
using Ignixa.Serialization;
using Ignixa.Specification;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Schema;
using Xunit;

namespace Microsoft.Health.Fhir.Extensions.Tests;

public class FhirJsonTextNodeTests
{
    private readonly string _patientJson = @"{
  ""resourceType"" : ""Patient"",
  ""name"" : [{
    ""id"" : ""f2"",
    ""use"" : ""official"" ,
    ""given"" : [ ""Karen"", ""May"" ],
    ""_given"" : [ null, {""id"" : ""middle""} ],
    ""family"" :  ""Van"",
    ""_family"" : {""id"" : ""a2""}
   }],
  ""text"" : {
    ""status"" : ""generated"" ,
    ""div"" : ""<div xmlns=\""http://www.w3.org/1999/xhtml\""><p>...</p></div>""
  }
}";

    [Fact]
    public void ReadShadowProperty()
    {
        ISourceNode sourceNode = JsonSourceNodeFactory.Parse(_patientJson);
        IStructureDefinitionSummaryProvider meta = InstanceInferredStructureDefinitionSummaryProvider.CreateFrom(sourceNode);

        ITypedElement node = sourceNode.ToTypedElement(meta);

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
    public void SourceNode()
    {
        ISourceNode sourceNode = JsonSourceNodeFactory.Parse(_patientJson);
        var meta = new FhirJsonSchemaStructureDefinitionSummaryProvider(FhirSpecification.R4);

        ITypedElement node = sourceNode.ToTypedElement(meta);
        ITypedElement familyType = node.Select("Patient.name.family").Single();

        IReadOnlyCollection<IElementDefinitionSummary> definitions = familyType.ChildDefinitions(meta);
    }

    [Fact]
    public void ValidateObject()
    {
        var meta = new FhirJsonSchemaStructureDefinitionSummaryProvider(FhirSpecification.R4);
        ISourceNode node = JsonSourceNodeFactory.Parse(_patientJson);

        var summary = meta.Provide(node.GetResourceTypeIndicator()) as IValidatableObject;

        ValidationResult[] results = summary.Validate(new ValidationContext(node)).ToArray();


        var sourceNode = JsonDocument.Parse("{ \"resourceType\": \"Boo\" }");

        results = summary.Validate(new ValidationContext(sourceNode)).ToArray();
    }

    [Fact]
    public void WithSchema()
    {
        ISourceNode sourceNode = JsonSourceNodeFactory.Parse(_patientJson);
        var meta = new FhirJsonSchemaStructureDefinitionSummaryProvider(FhirSpecification.R4);

        ITypedElement node = sourceNode.ToTypedElement(meta);

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
    public void WithJsonNode()
    {
        
        var sourceNode = JsonSourceNodeFactory.Parse(_patientJson);
        var meta = new FhirJsonSchemaStructureDefinitionSummaryProvider(FhirSpecification.R4);

        ITypedElement node = sourceNode.ToTypedElement(meta);

        object familyName = node.Scalar("Patient.name.family");
        object familyId = node.Scalar("Patient.name.family.id");
        Assert.Equal("Van", familyName);
        Assert.Equal("a2", familyId);

        ITypedElement familyNodes = node.Select("Patient.name.family").Single();
    }
}
