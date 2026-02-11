// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Anonymizer.Models;
using Ignixa.Anonymizer.Processors;
using Xunit;

namespace Ignixa.Anonymizer.Core.UnitTests.Processors
{
    public class SubstituteProcessorTests
    {
        private readonly R4CoreSchemaProvider _schema = new();

        public static IEnumerable<object[]> GetPrimitiveNodes()
        {
            yield return new object[] { """{"resourceType":"Patient","active":true}""", "active", "{ \"replaceWith\": null }", null };
            yield return new object[] { """{"resourceType":"Patient","active":true}""", "active", "{ \"replaceWith\": \"string.Empty\" }", "string.Empty" };
            yield return new object[] { """{"resourceType":"Patient","active":true}""", "active", "{ \"replaceWith\": false }", "False" };
            yield return new object[] { """{"resourceType":"Patient","id":"123"}""", "id", "{ \"replaceWith\": \"abc\" }", "abc" };
            yield return new object[] { """{"resourceType":"Patient","birthDate":"2000"}""", "birthDate", "{ \"replaceWith\": \"2000\" }", "2000" };
        }

        [Theory]
        [MemberData(nameof(GetPrimitiveNodes))]
        public void GivenAPrimitiveNode_WhenSubstitute_SubstitutedNodeShouldBeReturned(string json, string fieldName, string configJson, string expectedValue)
        {
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            var node = element.Children(fieldName).First();
            SubstituteProcessor processor = new SubstituteProcessor();
            var context = new ProcessContext
            {
                VisitedNodes = new HashSet<string>()
            };
            var settings = JsonSerializer.Deserialize<Dictionary<string, object>>(configJson);

            var processResult = processor.Process(resourceNode, node, context, settings);
            Assert.True(processResult.IsSubstituted);
        }

        [Fact]
        public void GivenAComplexDatatypeNodeAndValidReplaceValue_WhenSubstitute_SubstituteNodeShouldBeReturned()
        {
            SubstituteProcessor processor = new SubstituteProcessor();
            var json = """{"resourceType":"Patient","address":[{"state":"DC"}]}""";
            var configJson = """{ "replaceWith": { "use": "home", "type": "both", "text": "room", "city": "Beijing", "district": "Haidian", "state": "Beijing", "postalCode": "100871", "period": { "start": "1974-12-25" } } }""";
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            var node = element.Children("address").First();
            var context = new ProcessContext
            {
                VisitedNodes = new HashSet<string>()
            };
            var settings = JsonSerializer.Deserialize<Dictionary<string, object>>(configJson);

            var processResult = processor.Process(resourceNode, node, context, settings);
            Assert.True(processResult.IsSubstituted);
        }

        [Fact]
        public void GivenAComplexDatatypeNodeAndInvalidReplaceValue_WhenSubstitute_ProcessingExceptionShouldBeThrown()
        {
            SubstituteProcessor processor = new SubstituteProcessor();
            var json = """{"resourceType":"Patient","address":[{"state":"DC"}]}""";
            var configJson = """{"replaceWith": ""}""";
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            var node = element.Children("address").First();
            var context = new ProcessContext
            {
                VisitedNodes = new HashSet<string>()
            };
            var settings = JsonSerializer.Deserialize<Dictionary<string, object>>(configJson);

            Assert.Throws<Ignixa.Anonymizer.Exceptions.ProcessingException>(() => processor.Process(resourceNode, node, context, settings));
        }
    }
}
