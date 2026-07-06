// <copyright file="ReferenceResolutionCheckTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Checks;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;

namespace Ignixa.Validation.Tests.Checks;

/// <summary>
/// Tests for <see cref="ReferenceResolutionCheck"/>, focused on resource-boundary scoping of
/// fragment references.
/// </summary>
public class ReferenceResolutionCheckTests
{
    private static ValidationResult Validate(string resourceJson)
    {
        var element = JsonNodeSourceNode.Create(JsonNode.Parse(resourceJson)!)
            .ToElement(TestSchemaProvider.GetR4Schema());
        var state = new ValidationState().EnterRootResource(element);
        return new ReferenceResolutionCheck().Validate(
            element,
            new ValidationSettings { Depth = ValidationDepth.Full },
            state);
    }

    [Fact]
    public void GivenFragmentReferenceResolvingWithinNestedResource_WhenValidating_ThenNoIssue()
    {
        // Parameters -> parameter.resource (Coverage) whose OWN contained holds #payer.
        // The fragment resolves within the nested resource's scope, not the Parameters root's.
        var result = Validate(@"{
            ""resourceType"": ""Parameters"",
            ""parameter"": [{
                ""name"": ""coverage"",
                ""resource"": {
                    ""resourceType"": ""Coverage"",
                    ""id"": ""c1"",
                    ""contained"": [{ ""resourceType"": ""Organization"", ""id"": ""payer"" }],
                    ""payor"": [{ ""reference"": ""#payer"" }]
                }
            }]
        }");

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldNotContain(i => i.Code == "ref-resolve");
    }

    [Fact]
    public void GivenFragmentReferenceUnresolvedWithinNestedResource_WhenValidating_ThenReportsRefResolve()
    {
        // Same shape, but the nested resource contains no matching #payer — genuinely broken.
        var result = Validate(@"{
            ""resourceType"": ""Parameters"",
            ""parameter"": [{
                ""name"": ""coverage"",
                ""resource"": {
                    ""resourceType"": ""Coverage"",
                    ""id"": ""c1"",
                    ""payor"": [{ ""reference"": ""#payer"" }]
                }
            }]
        }");

        result.Issues.ShouldContain(i => i.Code == "ref-resolve");
    }

    [Fact]
    public void GivenRootFragmentReferenceResolvingToContained_WhenValidating_ThenNoIssue()
    {
        var result = Validate(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""p1"",
            ""contained"": [{ ""resourceType"": ""Organization"", ""id"": ""org1"" }],
            ""managingOrganization"": { ""reference"": ""#org1"" }
        }");

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldNotContain(i => i.Code == "ref-resolve");
    }

    [Fact]
    public void GivenContainedResourceReferencingPeerContained_WhenValidating_ThenNoIssue()
    {
        // FHIR contained-peer reference: a contained resource references another contained resource
        // of the same container via #id. The fragment must resolve against the CONTAINER's contained
        // pool (the nested resource has no own contained), so it must not be flagged. Regression guard:
        // isolating nested fragments would falsely flag this.
        var result = Validate(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""p1"",
            ""contained"": [
                { ""resourceType"": ""Organization"", ""id"": ""org1"" },
                { ""resourceType"": ""Patient"", ""id"": ""linked"", ""managingOrganization"": { ""reference"": ""#org1"" } }
            ]
        }");

        result.Issues.ShouldNotContain(i => i.Code == "ref-resolve");
    }

    [Fact]
    public void GivenRootFragmentReferenceUnresolved_WhenValidating_ThenReportsRefResolve()
    {
        var result = Validate(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""p1"",
            ""managingOrganization"": { ""reference"": ""#missing"" }
        }");

        result.Issues.ShouldContain(i => i.Code == "ref-resolve");
    }
}
