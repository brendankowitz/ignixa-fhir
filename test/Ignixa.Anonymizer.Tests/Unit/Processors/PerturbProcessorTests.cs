// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation + Ignixa Contributors
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Anonymizer.Models;
using Ignixa.Anonymizer.Processors;
using Xunit;

namespace Ignixa.Anonymizer.Core.UnitTests.Processors
{
    public class PerturbProcessorTests
    {
        private readonly R4CoreSchemaProvider _schema = new();

        public static IEnumerable<object[]> GetPrimitiveNodesToPerturbFixedSpan()
        {
            yield return new object[] { """{"resourceType":"Observation","valueInteger":5}""", "valueInteger", 0m, 0m, 5m, 5m };
            yield return new object[] { """{"resourceType":"Observation","valueQuantity":{"value":5.234}}""", null, 6m, 2m, 2.23m, 8.23m };
        }

        public static IEnumerable<object[]> GetQuantityNodesToPerturbFixedSpan()
        {
            yield return new object[]
            {
                """{"resourceType":"Condition","onsetAge":{"value":20,"unit":"a","system":"http://unitsofmeasure.org","code":"a"}}""",
                0m, 2m, 20m, 20m
            };
            yield return new object[]
            {
                """{"resourceType":"Observation","referenceRange":[{"low":{"value":7000.12345678}}]}""",
                2000m, 4m, 6000.1235m, 8000.1235m
            };
        }

        [Theory]
        [MemberData(nameof(GetPrimitiveNodesToPerturbFixedSpan))]
        public void GivenAPrimitiveNode_WhenPerturbFixedSpan_PerturbedNodeShouldBeReturned(string json, string fieldName, decimal span, decimal roundTo, decimal lowerBound, decimal upperBound)
        {
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            Ignixa.Abstractions.IElement node;
            if (fieldName != null)
            {
                node = element.Children(fieldName).First();
            }
            else
            {
                // For valueQuantity, navigate to the value child
                node = element.Children("valueQuantity").First().Children("value").First();
            }

            PerturbProcessor processor = new PerturbProcessor(_schema);
            var context = new ProcessContext
            {
                VisitedNodes = new HashSet<string>()
            };
            var settings = new Dictionary<string, object> { { "span", span }, { "roundTo", roundTo } };

            var processResult = processor.Process(resourceNode, node, context, settings);
            Assert.True(processResult.IsPerturbed);
            resourceNode.InvalidateCaches();
            var updated = resourceNode.ToElement(_schema);
            Ignixa.Abstractions.IElement updatedNode;
            if (fieldName != null)
            {
                updatedNode = updated.Children(fieldName).First();
            }
            else
            {
                updatedNode = updated.Children("valueQuantity").First().Children("value").First();
            }
            var perturbedValue = decimal.Parse(updatedNode.Value.ToString());
            Assert.InRange(perturbedValue, lowerBound, upperBound);
            Assert.True(GetDecimalPlaces(perturbedValue) <= roundTo);
        }

        [Theory]
        [MemberData(nameof(GetQuantityNodesToPerturbFixedSpan))]
        public void GivenAQuantityNode_WhenPerturbFixedSpan_PerturbedNodeShouldBeReturned(string json, decimal span, decimal roundTo, decimal lowerBound, decimal upperBound)
        {
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            // Navigate to the quantity node (the parent containing "value")
            Ignixa.Abstractions.IElement quantityNode;
            if (json.Contains("onsetAge"))
            {
                quantityNode = element.Children("onsetAge").First();
            }
            else
            {
                quantityNode = element.Children("referenceRange").First().Children("low").First();
            }

            PerturbProcessor processor = new PerturbProcessor(_schema);
            var context = new ProcessContext
            {
                VisitedNodes = new HashSet<string>()
            };
            var settings = new Dictionary<string, object> { { "span", span }, { "roundTo", roundTo } };

            var processResult = processor.Process(resourceNode, quantityNode, context, settings);
            Assert.True(processResult.IsPerturbed);
            resourceNode.InvalidateCaches();
            var updated = resourceNode.ToElement(_schema);
            Ignixa.Abstractions.IElement updatedQuantityNode;
            if (json.Contains("onsetAge"))
            {
                updatedQuantityNode = updated.Children("onsetAge").First();
            }
            else
            {
                updatedQuantityNode = updated.Children("referenceRange").First().Children("low").First();
            }
            var perturbedValue = decimal.Parse(updatedQuantityNode.Children("value").First().Value.ToString());
            Assert.InRange(perturbedValue, lowerBound, upperBound);
            Assert.True(GetDecimalPlaces(perturbedValue) <= roundTo);
        }

        private int GetDecimalPlaces(decimal n)
        {
            n = Math.Abs(n);
            n -= (int)n;
            var decimalPlaces = 0;
            while (n > 0)
            {
                decimalPlaces++;
                n *= 10;
                n -= (int)n;
            }
            return decimalPlaces;
        }
    }
}
