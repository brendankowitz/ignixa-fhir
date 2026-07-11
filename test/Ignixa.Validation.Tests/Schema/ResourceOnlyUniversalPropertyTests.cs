// <copyright file="ResourceOnlyUniversalPropertyTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Schema;

/// <summary>
/// Regression tests for GH #323: <see cref="Ignixa.Validation.Checks.UnknownPropertyCheck"/> used to
/// universally permit <c>text</c>, <c>contained</c>, <c>extension</c>, and <c>modifierExtension</c> for
/// every resource type, even a bare <see cref="Resource"/> like <c>Bundle</c> that is not a
/// <c>DomainResource</c> and has none of those elements in its own StructureDefinition metadata.
/// </summary>
public class ResourceOnlyUniversalPropertyTests
{
    private readonly ISchema _schema = new R4CoreSchemaProvider();
    private readonly StructureDefinitionSchemaBuilder _builder = new();

    [Trait("Category", "Regression")]
    [Fact]
    public void GivenBundleWithDomainResourceOnlyText_WhenValidating_ThenReportsUnknownProperty()
    {
        var typeDefinition = _schema.GetTypeDefinition("Bundle");
        var schema = _builder.BuildSchema(typeDefinition!, _schema);

        var json = JsonNode.Parse("""
            {
                "resourceType": "Bundle",
                "id": "b1",
                "type": "collection",
                "text": { "status": "generated", "div": "<div xmlns=\"http://www.w3.org/1999/xhtml\">x</div>" }
            }
            """);
        var sourceNode = JsonNodeSourceNode.Create(json);
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());

        var result = schema.Validate(element, new ValidationSettings { Depth = ValidationDepth.Spec });

        result.Issues.ShouldContain(i => i.Code == "unknown-property" && i.Message.Contains("text"));
    }

    [Trait("Category", "Regression")]
    [Theory]
    [InlineData("contained", "[{\"resourceType\": \"Patient\", \"id\": \"contained1\"}]")]
    [InlineData("extension", "[{\"url\": \"http://example.org/ext\", \"valueString\": \"x\"}]")]
    [InlineData("modifierExtension", "[{\"url\": \"http://example.org/ext\", \"valueString\": \"x\"}]")]
    public void GivenBundleWithDomainResourceOnlyProperty_WhenValidating_ThenReportsUnknownProperty(string propertyName, string propertyValueJson)
    {
        var typeDefinition = _schema.GetTypeDefinition("Bundle");
        var schema = _builder.BuildSchema(typeDefinition!, _schema);

        var json = JsonNode.Parse($$"""
            {
                "resourceType": "Bundle",
                "id": "b1",
                "type": "collection",
                "{{propertyName}}": {{propertyValueJson}}
            }
            """);
        var sourceNode = JsonNodeSourceNode.Create(json);
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());

        var result = schema.Validate(element, new ValidationSettings { Depth = ValidationDepth.Spec });

        result.Issues.ShouldContain(i => i.Code == "unknown-property" && i.Message.Contains(propertyName));
    }

    [Trait("Category", "Regression")]
    [Fact]
    public void GivenPatientWithText_WhenValidating_ThenAllowed()
    {
        // Sanity guard: Patient IS a DomainResource, so `text` must remain legal there.
        var typeDefinition = _schema.GetTypeDefinition("Patient");
        var schema = _builder.BuildSchema(typeDefinition!, _schema);

        var json = JsonNode.Parse("""
            {
                "resourceType": "Patient",
                "id": "p1",
                "text": { "status": "generated", "div": "<div xmlns=\"http://www.w3.org/1999/xhtml\">x</div>" }
            }
            """);
        var sourceNode = JsonNodeSourceNode.Create(json);
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());

        var result = schema.Validate(element, new ValidationSettings { Depth = ValidationDepth.Spec });

        result.Issues.ShouldNotContain(i => i.Code == "unknown-property");
    }
}
