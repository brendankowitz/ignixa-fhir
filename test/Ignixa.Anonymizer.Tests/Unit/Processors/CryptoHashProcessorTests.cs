// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation + Ignixa Contributors
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
    public class CryptoHashProcessorTests
    {
        private const string TestHashKey = "123";
        private readonly R4CoreSchemaProvider _schema = new();

        public static IEnumerable<object[]> GetNonReferenceNodesForCryptoHash()
        {
            yield return new object[] { """{"resourceType":"Patient","id":""}""", "id", string.Empty };
            yield return new object[] { """{"resourceType":"Patient","id":"a"}""", "id", "69e99e82127c1f146f50653e02b92c4bb0c3bc182a6165a5bbce5f4f94e1ccb7" };
            yield return new object[] { """{"resourceType":"Patient","id":"bb6f4872-e456-42d5-a9da-a0d82cb7ea29"}""", "id", "73defcb8fcaf4c0c3d5c77f05b479cadbe502db2ef6e9b1523d2bfee31f3b999" };
            yield return new object[] { """{"resourceType":"Observation","identifier":[{"system":"urn:oid:1.2.3.4.5","value":"urn:oid:1.2.3.4.5"}]}""", "identifier[0].value", "6aa8e3e9af18cae990adc9c26dc006ebf633dd1332886867a691ee2d5247dd15" };
            yield return new object[] { """{"resourceType":"Observation","identifier":[{"system":"urn:uuid:c757873d-ec9a-4326-a141-556f43239520","value":"urn:uuid:c757873d-ec9a-4326-a141-556f43239520"}]}""", "identifier[0].value", "23a940be8f03522b52a393c7194407796bb5ea3c02b926b0e42fdab94ca30bad" };
            yield return new object[] { """{"resourceType":"Patient","birthDate":"2020-04-12"}""", "birthDate", "1e99c6a8b99d3c8b4906c2e80911ad3b5961fd7498d3bc2b96fb128bc7148f90" };
            yield return new object[] { """{"resourceType":"Observation","effectiveDateTime":"2017-01-01T00:00:00.000Z"}""", "effectiveDateTime", "4c765fc04a6f9967d493ff39238d47993c709d3392a72060efeff285cf7b2501" };
        }

        public static IEnumerable<object[]> GetReferenceNodesForCryptoHash()
        {
            yield return new object[] { """{"resourceType":"Patient","generalPractitioner":[{"reference":""}]}""", string.Empty };
            yield return new object[] { """{"resourceType":"Patient","generalPractitioner":[{"reference":"#"}]}""", "#" };
            yield return new object[] { """{"resourceType":"Patient","generalPractitioner":[{"reference":"#p1"}]}""", "#a10e6bee4fbeb6a7804153c25688dd4dd7b9c2a005417136026350fc33ac609f" };
            yield return new object[] { """{"resourceType":"Patient","generalPractitioner":[{"reference":"Patient/example"}]}""", "Patient/698d54f0494528a759f19c8e87a9f99e75a5881b9267ee3926bcf62c992d84ba" };
            yield return new object[] { """{"resourceType":"Patient","generalPractitioner":[{"reference":"http://fhir.hl7.org/svc/StructureDefinition/c8973a22-2b5b-4e76-9c66-00639c99e61b"}]}""", "http://fhir.hl7.org/svc/StructureDefinition/b0ff9c939b3507a79e2ae3d2d3b595d62819b9b8f6ef10d4099b3058d902642f" };
            yield return new object[] { """{"resourceType":"Patient","generalPractitioner":[{"reference":"http://example.org/fhir/Observation/apo89654/_history/2"}]}""", "http://example.org/fhir/Observation/b1e85ca33baf76575ad28588af85b8f10c0dd40e9ed8cd57cdb7ae94ccd75695/_history/2" };
            yield return new object[] { """{"resourceType":"Patient","generalPractitioner":[{"reference":"urn:uuid:c757873d-ec9a-4326-a141-556f43239520"}]}""", "urn:uuid:24970eb3f915e516a2b5241c0d6979097a6357a13b89612c6a54b8ab5479df34" };
            yield return new object[] { """{"resourceType":"Patient","generalPractitioner":[{"reference":"urn:oid:1.2.3.4.5"}]}""", "urn:oid:0543fb50485f58a47073f51aad1677607aec031c2c83c25ee7b040ade95cfbcc" };
        }

        [Theory]
        [MemberData(nameof(GetNonReferenceNodesForCryptoHash))]
        public void GivenANonReferenceNode_WhenCryptoHash_HashedNodeShouldBeReturned(string json, string path, string expectedValue)
        {
            var processor = new CryptoHashProcessor(TestHashKey, _schema);
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            var node = FindNodeByPath(element, path);

            processor.Process(resourceNode, node);
            resourceNode.InvalidateCaches();
            var updated = resourceNode.ToElement(_schema);
            var updatedNode = FindNodeByPath(updated, path);
            Assert.Equal(expectedValue, updatedNode.Value?.ToString());
        }

        [Theory]
        [MemberData(nameof(GetReferenceNodesForCryptoHash))]
        public void GivenAReferenceNode_WhenCryptoHash_PartlyHashedNodeShouldBeReturned(string json, string expectedValue)
        {
            var processor = new CryptoHashProcessor(TestHashKey, _schema);
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            var referenceNode = element.Children("generalPractitioner").First().Children("reference").FirstOrDefault();

            processor.Process(resourceNode, referenceNode);
            resourceNode.InvalidateCaches();
            var updated = resourceNode.ToElement(_schema);
            var updatedReferenceNode = updated.Children("generalPractitioner").First().Children("reference").FirstOrDefault();
            Assert.Equal(expectedValue, updatedReferenceNode?.Value?.ToString());
        }

        private static Ignixa.Abstractions.IElement FindNodeByPath(Ignixa.Abstractions.IElement root, string path)
        {
            if (string.IsNullOrEmpty(path))
                return root;

            var parts = path.Split('.');
            var current = root;

            foreach (var part in parts)
            {
                if (part.Contains('['))
                {
                    var name = part.Substring(0, part.IndexOf('['));
                    var indexStr = part.Substring(part.IndexOf('[') + 1, part.IndexOf(']') - part.IndexOf('[') - 1);
                    var index = int.Parse(indexStr);
                    current = current.Children(name).ElementAt(index);
                }
                else
                {
                    current = current.Children(part).First();
                }
            }

            return current;
        }
    }
}
