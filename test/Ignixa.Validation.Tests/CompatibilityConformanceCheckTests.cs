// <copyright file="CompatibilityConformanceCheckTests.cs" company="Microsoft Corporation">
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

namespace Ignixa.Validation.Tests;

/// <summary>
/// Regression tests for GH #320: <see cref="ValidationDepth.Compatibility"/> must run the
/// CodeSystem/ValueSet conformance checks (which Microsoft FHIR Server's fallback Firely validator
/// also enforces) even though it does not run the rest of the profile tier (invariants, slicing,
/// reference resolution — see <see cref="ValidationDepthTests"/> for that regression coverage).
/// </summary>
public class CompatibilityConformanceCheckTests
{
    private readonly ISchema _schema = new R4CoreSchemaProvider();
    private readonly StructureDefinitionSchemaBuilder _builder = new();

    [Trait("Category", "Regression")]
    [Fact]
    public void GivenCompatibilityDepth_WhenValueSetIncludeSystemIsRelative_ThenReportsIssue()
    {
        var typeDefinition = _schema.GetTypeDefinition("ValueSet");
        var schema = _builder.BuildSchema(typeDefinition!, _schema);

        var json = JsonNode.Parse("""
            {
                "resourceType": "ValueSet",
                "id": "vs1",
                "status": "active",
                "compose": {
                    "include": [
                        { "system": "not-an-absolute-uri" }
                    ]
                }
            }
            """);
        var sourceNode = JsonNodeSourceNode.Create(json);
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());

        var compatSettings = new ValidationSettings { Depth = ValidationDepth.Compatibility };
        var compatResult = schema.Validate(element, compatSettings);

        compatResult.Issues.ShouldContain(i => i.Code == "vs-1");
    }

    [Trait("Category", "Regression")]
    [Fact]
    public void GivenCompatibilityDepth_WhenCodeSystemSupplementHasWrongContent_ThenReportsIssue()
    {
        var typeDefinition = _schema.GetTypeDefinition("CodeSystem");
        var schema = _builder.BuildSchema(typeDefinition!, _schema);

        var json = JsonNode.Parse("""
            {
                "resourceType": "CodeSystem",
                "id": "cs1",
                "status": "active",
                "content": "complete",
                "supplements": "http://example.org/CodeSystem/base"
            }
            """);
        var sourceNode = JsonNodeSourceNode.Create(json);
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());

        var compatSettings = new ValidationSettings { Depth = ValidationDepth.Compatibility };
        var compatResult = schema.Validate(element, compatSettings);

        compatResult.Issues.ShouldContain(i => i.Code == "csc-1");
    }
}
