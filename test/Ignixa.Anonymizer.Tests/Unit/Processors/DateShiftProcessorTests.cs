// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation + Ignixa Contributors
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Linq;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Anonymizer.Processors;
using Xunit;

namespace Ignixa.Anonymizer.Core.UnitTests.Processors
{
    public class DateShiftProcessorTests
    {
        private readonly R4CoreSchemaProvider _schema = new();

        [Fact]
        public void GivenADateNode_WhenDateShift_DateShiftedNodeShouldBeReturned()
        {
            DateShiftProcessor processor = new DateShiftProcessor(dateShiftKey: "dummy", string.Empty, enablePartialDatesForRedact: true);

            var json = """{"resourceType":"Patient","birthDate":"2015-02-07"}""";
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            var node = element.Children("birthDate").First();
            var processResult = processor.Process(resourceNode, node);
            resourceNode.InvalidateCaches();
            var updated = resourceNode.ToElement(_schema);
            var updatedNode = updated.Children("birthDate").First();
            Assert.Equal("2015-01-17", updatedNode.Value.ToString());
            Assert.True(processResult.IsPerturbed);

            json = """{"resourceType":"Patient","birthDate":"2015-02"}""";
            resourceNode = ResourceJsonNode.Parse(json);
            element = resourceNode.ToElement(_schema);
            node = element.Children("birthDate").First();
            processResult = processor.Process(resourceNode, node);
            resourceNode.InvalidateCaches();
            updated = resourceNode.ToElement(_schema);
            updatedNode = updated.Children("birthDate").First();
            Assert.Equal("2015", updatedNode.Value.ToString());
            Assert.True(processResult.IsRedacted);

            processor = new DateShiftProcessor(dateShiftKey: "dummy", string.Empty, enablePartialDatesForRedact: false);
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
        public void GivenADateTimeNode_WhenDateShift_DateShiftedNodeShouldBeReturn()
        {
            DateShiftProcessor processor = new DateShiftProcessor(dateShiftKey: "dummy", string.Empty, enablePartialDatesForRedact: true);
            var json = """{"resourceType":"Observation","effectiveDateTime":"2015-02-07T13:28:17-05:00"}""";
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            var node = element.Children("effectiveDateTime").First();
            var processResult = processor.Process(resourceNode, node);
            resourceNode.InvalidateCaches();
            var updated = resourceNode.ToElement(_schema);
            var updatedNode = updated.Children("effectiveDateTime").First();
            Assert.Equal("2015-01-17T00:00:00-05:00", updatedNode.Value.ToString());
            Assert.True(processResult.IsPerturbed);
        }

        [Fact]
        public void GivenAInstantNode_WhenDateShift_DateShiftedNodeShouldBeReturn()
        {
            DateShiftProcessor processor = new DateShiftProcessor(dateShiftKey: "dummy", string.Empty, enablePartialDatesForRedact: true);
            var json = """{"resourceType":"Observation","issued":"2015-02-07T01:01:01+00:00"}""";
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            var node = element.Children("issued").First();
            var processResult = processor.Process(resourceNode, node);
            resourceNode.InvalidateCaches();
            var updated = resourceNode.ToElement(_schema);
            var updatedNode = updated.Children("issued").First();
            Assert.Equal("2015-01-17T00:00:00+00:00", updatedNode.Value.ToString());
            Assert.True(processResult.IsPerturbed);
        }
    }
}
