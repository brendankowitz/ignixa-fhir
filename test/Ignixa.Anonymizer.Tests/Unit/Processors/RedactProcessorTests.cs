// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Generic;
using System.Linq;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Anonymizer.Processors;
using Xunit;

namespace Ignixa.Anonymizer.Core.UnitTests.Processors
{
    public class RedactProcessorTests
    {
        private readonly R4CoreSchemaProvider _schema = new();

        [Fact]
        public void GivenADateNode_WhenRedact_RedactedNodeShouldBeReturn()
        {
            RedactProcessor processor = new RedactProcessor(enablePartialDatesForRedact: true, true, true, new List<string>());
            var json = """{"resourceType":"Patient","birthDate":"2015-02"}""";
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            var node = element.Children("birthDate").First();
            var processResult = processor.Process(resourceNode, node);
            resourceNode.InvalidateCaches();
            var updated = resourceNode.ToElement(_schema);
            var updatedNode = updated.Children("birthDate").FirstOrDefault();
            Assert.Equal("2015", updatedNode?.Value?.ToString());
            Assert.True(processResult.IsRedacted);

            processor = new RedactProcessor(enablePartialDatesForRedact: false, true, true, new List<string>());
            json = """{"resourceType":"Patient","birthDate":"2015-02"}""";
            resourceNode = ResourceJsonNode.Parse(json);
            element = resourceNode.ToElement(_schema);
            node = element.Children("birthDate").First();
            processResult = processor.Process(resourceNode, node);
            resourceNode.InvalidateCaches();
            updated = resourceNode.ToElement(_schema);
            updatedNode = updated.Children("birthDate").FirstOrDefault();
            Assert.Null(updatedNode?.Value);
            Assert.True(processResult.IsRedacted);
        }

        [Fact]
        public void GivenADateTimeNode_WhenRedact_RedactedNodeShouldBeReturn()
        {
            RedactProcessor processor = new RedactProcessor(enablePartialDatesForRedact: true, true, true, new List<string>());
            var json = """{"resourceType":"Observation","effectiveDateTime":"2015-02-07T13:28:17-05:00"}""";
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            var node = element.Children("effectiveDateTime").First();
            var processResult = processor.Process(resourceNode, node);
            resourceNode.InvalidateCaches();
            var updated = resourceNode.ToElement(_schema);
            var updatedNode = updated.Children("effectiveDateTime").FirstOrDefault();
            Assert.Equal("2015", updatedNode?.Value?.ToString());
            Assert.True(processResult.IsRedacted);

            processor = new RedactProcessor(enablePartialDatesForRedact: false, true, true, new List<string>());
            resourceNode = ResourceJsonNode.Parse(json);
            element = resourceNode.ToElement(_schema);
            node = element.Children("effectiveDateTime").First();
            processResult = processor.Process(resourceNode, node);
            resourceNode.InvalidateCaches();
            updated = resourceNode.ToElement(_schema);
            updatedNode = updated.Children("effectiveDateTime").FirstOrDefault();
            Assert.Null(updatedNode?.Value);
            Assert.True(processResult.IsRedacted);
        }

        [Fact]
        public void GivenAInstantNode_WhenRedact_RedactedNodeShouldBeReturn()
        {
            RedactProcessor processor = new RedactProcessor(enablePartialDatesForRedact: true, true, true, new List<string>());
            var json = """{"resourceType":"Observation","issued":"2015-01-01T00:00:00+00:00"}""";
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            var node = element.Children("issued").First();
            var processResult = processor.Process(resourceNode, node);
            resourceNode.InvalidateCaches();
            var updated = resourceNode.ToElement(_schema);
            var updatedNode = updated.Children("issued").FirstOrDefault();
            Assert.Equal("2015", updatedNode?.Value?.ToString());
            Assert.True(processResult.IsRedacted);

            processor = new RedactProcessor(enablePartialDatesForRedact: false, true, true, new List<string>());
            resourceNode = ResourceJsonNode.Parse(json);
            element = resourceNode.ToElement(_schema);
            node = element.Children("issued").First();
            processResult = processor.Process(resourceNode, node);
            resourceNode.InvalidateCaches();
            updated = resourceNode.ToElement(_schema);
            updatedNode = updated.Children("issued").FirstOrDefault();
            Assert.Null(updatedNode?.Value);
            Assert.True(processResult.IsRedacted);
        }

        [Fact]
        public void GivenAnAgeNode_WhenRedact_RedactedNodeShouldBeReturn()
        {
            RedactProcessor processor = new RedactProcessor(true, enablePartialAgesForRedact: true, true, new List<string>());
            var json = """{"resourceType":"Condition","onsetAge":{"value":91,"unit":"a","system":"http://unitsofmeasure.org","code":"a"}}""";
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            var node = element.Children("onsetAge").First().Children("value").First();
            var processResult = processor.Process(resourceNode, node);
            resourceNode.InvalidateCaches();
            var updated = resourceNode.ToElement(_schema);
            var updatedNode = updated.Children("onsetAge").First().Children("value").FirstOrDefault();
            Assert.Null(updatedNode?.Value);
            Assert.True(processResult.IsRedacted);

            processor = new RedactProcessor(true, enablePartialAgesForRedact: false, true, new List<string>());
            resourceNode = ResourceJsonNode.Parse(json);
            element = resourceNode.ToElement(_schema);
            node = element.Children("onsetAge").First().Children("value").First();
            processResult = processor.Process(resourceNode, node);
            resourceNode.InvalidateCaches();
            updated = resourceNode.ToElement(_schema);
            updatedNode = updated.Children("onsetAge").First().Children("value").FirstOrDefault();
            Assert.Null(updatedNode?.Value);
            Assert.True(processResult.IsRedacted);

            processor = new RedactProcessor(true, enablePartialAgesForRedact: true, true, new List<string>());
            json = """{"resourceType":"Condition","onsetAge":{"value":89,"unit":"a","system":"http://unitsofmeasure.org","code":"a"}}""";
            resourceNode = ResourceJsonNode.Parse(json);
            element = resourceNode.ToElement(_schema);
            node = element.Children("onsetAge").First().Children("value").First();
            processResult = processor.Process(resourceNode, node);
            resourceNode.InvalidateCaches();
            updated = resourceNode.ToElement(_schema);
            updatedNode = updated.Children("onsetAge").First().Children("value").First();
            Assert.Equal("89", updatedNode.Value.ToString());
            Assert.True(processResult.IsRedacted);
        }

        [Fact]
        public void GivenAPostalCodeNode_WhenRedact_RedactedNodeShouldBeReturn()
        {
            RedactProcessor processor = new RedactProcessor(true, true, enablePartialZipCodesForRedact: true, restrictedZipCodeTabulationAreas: new List<string>() { "123" });
            var json = """{"resourceType":"Patient","address":[{"postalCode":"12345"}]}""";
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            var node = element.Children("address").First().Children("postalCode").First();
            var processResult = processor.Process(resourceNode, node);
            resourceNode.InvalidateCaches();
            var updated = resourceNode.ToElement(_schema);
            var updatedNode = updated.Children("address").First().Children("postalCode").First();
            Assert.Equal("00000", updatedNode.Value.ToString());
            Assert.True(processResult.IsAbstracted);

            json = """{"resourceType":"Patient","address":[{"postalCode":"54321"}]}""";
            resourceNode = ResourceJsonNode.Parse(json);
            element = resourceNode.ToElement(_schema);
            node = element.Children("address").First().Children("postalCode").First();
            processResult = processor.Process(resourceNode, node);
            resourceNode.InvalidateCaches();
            updated = resourceNode.ToElement(_schema);
            updatedNode = updated.Children("address").First().Children("postalCode").First();
            Assert.Equal("54300", updatedNode.Value.ToString());
            Assert.True(processResult.IsAbstracted);

            processor = new RedactProcessor(true, true, enablePartialZipCodesForRedact: false, restrictedZipCodeTabulationAreas: new List<string>() { });
            json = """{"resourceType":"Patient","address":[{"postalCode":"54321"}]}""";
            resourceNode = ResourceJsonNode.Parse(json);
            element = resourceNode.ToElement(_schema);
            node = element.Children("address").First().Children("postalCode").First();
            processResult = processor.Process(resourceNode, node);
            resourceNode.InvalidateCaches();
            updated = resourceNode.ToElement(_schema);
            updatedNode = updated.Children("address").First().Children("postalCode").FirstOrDefault();
            Assert.Null(updatedNode?.Value);
            Assert.True(processResult.IsRedacted);
        }

        [Fact]
        public void GivenAnOtherNode_WhenRedact_RedactedNodeShouldBeReturn()
        {
            RedactProcessor processor = new RedactProcessor(true, true, true, new List<string>());
            var json = """{"resourceType":"Patient","id":"TestString"}""";
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            var node = element.Children("id").First();
            var processResult = processor.Process(resourceNode, node);
            resourceNode.InvalidateCaches();
            var updated = resourceNode.ToElement(_schema);
            var updatedNode = updated.Children("id").FirstOrDefault();
            Assert.Null(updatedNode?.Value);
            Assert.True(processResult.IsRedacted);
        }
    }
}
