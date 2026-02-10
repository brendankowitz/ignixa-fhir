// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Generic;
using System.Linq;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Anonymizer.Extensions;
using Xunit;

namespace Ignixa.Anonymizer.Core.UnitTests.Extensions
{
    public class TypedElementNavExtensionsTests
    {
        private readonly R4CoreSchemaProvider _schema = new();

        [Fact]
        public void GivenASingleResourceNode_WhenGetResourceDescendantsWithoutSubResource_DescendantsShouldBeReturned()
        {
            var json = """{"resourceType":"Patient","address":[{}],"name":[{"given":["Test"]}]}""";
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);

            var result = element.ResourceDescendantsWithoutSubResource().Select(e => e.Location).ToList();

            Assert.Equal(3, result.Count);
            Assert.Contains("Patient.name[0].given[0]", result);
            Assert.Contains("Patient.name[0]", result);
            Assert.Contains("Patient.address[0]", result);
        }

        [Fact]
        public void GivenAContainedNode_WhenGetResourceDescendantsWithoutSubResource_ContainedNodesShouldNotBeReturned()
        {
            var json = """{"resourceType":"Condition","text":{"status":"generated","div":"<div xmlns=\"http://www.w3.org/1999/xhtml\">Test</div>"},"contained":[{"resourceType":"Patient","address":[{}],"name":[{"given":["Test"]}]}]}""";
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);

            var result = element.ResourceDescendantsWithoutSubResource().Select(e => e.Location).ToList();

            // Should include text and text.div and text.status but NOT contained descendants
            Assert.Contains("Condition.text", result);
            Assert.Contains("Condition.text.div", result);
            Assert.Contains("Condition.text.status", result);
            // contained[0] is a sub-resource so it should be excluded
            Assert.DoesNotContain("Condition.contained[0].address[0]", result);
        }

        [Fact]
        public void GivenASingleResourceNode_WhenGetSelfAndDescendantsWithoutSubResource_SelfAndDescendantsShouldBeReturned()
        {
            var json = """{"resourceType":"Patient","address":[{}],"name":[{"given":["Test"]}]}""";
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);

            var testNodes = new List<IElement> { element };
            var result = testNodes.SelfAndDescendantsWithoutSubResource().Select(e => e.Location).ToList();

            Assert.Equal(4, result.Count);
            Assert.Contains("Patient", result);
            Assert.Contains("Patient.name[0].given[0]", result);
            Assert.Contains("Patient.name[0]", result);
            Assert.Contains("Patient.address[0]", result);
        }

        [Fact]
        public void GivenAContainedNode_WhenSelfAndDescendantsWithoutSubResource_ContainedNodesShouldNotBeReturned()
        {
            var json = """{"resourceType":"Condition","text":{"status":"generated","div":"<div xmlns=\"http://www.w3.org/1999/xhtml\">Test</div>"},"contained":[{"resourceType":"Patient","address":[{}],"name":[{"given":["Test"]}]}]}""";
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);

            var testNodes = new List<IElement> { element };
            var result = testNodes.SelfAndDescendantsWithoutSubResource().Select(e => e.Location).ToList();

            Assert.Contains("Condition", result);
            Assert.Contains("Condition.text", result);
            Assert.Contains("Condition.text.div", result);
            Assert.Contains("Condition.text.status", result);
            Assert.DoesNotContain("Condition.contained[0].address[0]", result);
        }
    }
}
