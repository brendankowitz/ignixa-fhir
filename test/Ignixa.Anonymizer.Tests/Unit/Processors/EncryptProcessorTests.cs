// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation + Ignixa Contributors
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Anonymizer.Processors;
using Ignixa.Anonymizer.Utility;
using Xunit;

namespace Ignixa.Anonymizer.Core.UnitTests.Processors
{
    public class EncryptProcessorTests
    {
        private const string TestEncryptKey = "704ab12c8e3e46d4bea600ef62a6bec7";
        private readonly R4CoreSchemaProvider _schema = new();

        public static IEnumerable<object[]> GetNodesForEncryption()
        {
            yield return new object[] { """{"resourceType":"Patient","id":null}""", null };
            yield return new object[] { """{"resourceType":"Patient","id":""}""", string.Empty };
            yield return new object[] { """{"resourceType":"Patient","id":"abc"}""", "abc" };
            yield return new object[] { """{"resourceType":"Patient","id":"testvalue"}""", "testvalue" };
        }

        [Theory]
        [MemberData(nameof(GetNodesForEncryption))]
        public void GivenANode_WhenEncrypt_ValueShouldBeEncryptedCorrectly(string json, string originalText)
        {
            var processor = new EncryptProcessor(TestEncryptKey);
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            var node = element.Children("id").FirstOrDefault();

            if (node == null)
            {
                // If the node doesn't exist (e.g., "id":null), skip the test
                Assert.Null(originalText);
                return;
            }

            processor.Process(resourceNode, node);
            resourceNode.InvalidateCaches();
            var updated = resourceNode.ToElement(_schema);
            var updatedNode = updated.Children("id").FirstOrDefault();

            // Here we only check the cipher text can be correctly decrypted since we are using a random IV during encryption
            Assert.Equal(originalText, DecryptText(updatedNode?.Value?.ToString()));
        }

        private string DecryptText(string text)
        {
            return EncryptUtility.DecryptTextFromBase64WithAes(text, Encoding.UTF8.GetBytes(TestEncryptKey));
        }
    }
}
