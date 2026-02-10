// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Generic;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Anonymizer.Extensions;
using Ignixa.Anonymizer.Visitors;
using Xunit;

namespace Ignixa.Anonymizer.Core.UnitTests.Extensions
{
    public class ElementNodeVisitorExtensionsTests
    {
        private readonly R4CoreSchemaProvider _schema = new();

        [Fact]
        public void GivenAPatientNode_WhenVisit_AllNodesShouldBeVisited()
        {
            var json = """{"resourceType":"Patient","active":true,"address":[{"city":"Test"},{}]}""";
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            var result = new HashSet<string>();
            element.Accept(resourceNode, new TestVisitor(result));

            Assert.Contains("Patient", result);
            Assert.Contains("Patient.active", result);
            Assert.Contains("Patient.address[0]", result);
            Assert.Contains("Patient.address[0].city", result);
            Assert.Contains("Patient.address[1]", result);
        }

        [Fact]
        public void GivenAPatientNodeWithContained_WhenVisit_AllNodesShouldBeVisited()
        {
            var json = """{"resourceType":"Patient","active":true,"address":[{"city":"Test"},{}],"contained":[{"resourceType":"Observation","status":"unknown"}]}""";
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            var result = new HashSet<string>();
            element.Accept(resourceNode, new TestVisitor(result));

            Assert.Contains("Patient", result);
            Assert.Contains("Patient.active", result);
            Assert.Contains("Patient.address[0]", result);
            Assert.Contains("Patient.address[0].city", result);
            Assert.Contains("Patient.address[1]", result);
            Assert.Contains("Patient.contained[0]", result);
            Assert.Contains("Patient.contained[0].status", result);
        }

        [Fact]
        public void GivenABundleNode_WhenVisit_AllNodesShouldBeVisited()
        {
            var json = """{"resourceType":"Bundle","type":"document","entry":[{"fullUrl":"http://example.org/fhir/Patient/1","resource":{"resourceType":"Patient","active":true}}]}""";
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            var result = new HashSet<string>();
            element.Accept(resourceNode, new TestVisitor(result));

            Assert.Contains("Bundle", result);
            Assert.Contains("Bundle.type", result);
            Assert.Contains("Bundle.entry[0]", result);
            Assert.Contains("Bundle.entry[0].fullUrl", result);
            Assert.Contains("Bundle.entry[0].resource", result);
            Assert.Contains("Bundle.entry[0].resource.active", result);
        }

        private class TestVisitor : AbstractElementNodeVisitor
        {
            private readonly HashSet<string> _result;
            public TestVisitor(HashSet<string> result)
            {
                _result = result;
            }

            public override bool Visit(ResourceJsonNode resource, IElement node)
            {
                _result.Add(node.Location);
                return true;
            }
        }
    }
}
