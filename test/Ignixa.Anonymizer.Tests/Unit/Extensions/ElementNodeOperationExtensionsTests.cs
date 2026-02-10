// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Linq;
using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Anonymizer.Extensions;
using Xunit;

namespace Ignixa.Anonymizer.Core.UnitTests.Extensions
{
    public class ElementNodeOperationExtensionsTests
    {
        private readonly R4CoreSchemaProvider _schema = new();

        [Fact]
        public void GivenAJsonObject_WhenRemoveEmptyNodes_NullChildrenShouldBeRemoved()
        {
            var json = """{"resourceType":"Patient","id":"root","name":[{"given":["child1"],"family":"child2"}]}""";
            var resourceNode = ResourceJsonNode.Parse(json);
            var mutableNode = resourceNode.MutableNode;

            // Verify initial state
            Assert.NotNull(mutableNode["name"]);

            // Set given to null
            var nameArr = mutableNode["name"] as JsonArray;
            var nameObj = nameArr[0] as JsonObject;
            nameObj["given"] = null;
            ElementNodeOperationExtensions.RemoveEmptyNodes(mutableNode);
            // family should still be there
            nameObj = (mutableNode["name"] as JsonArray)?[0] as JsonObject;
            Assert.NotNull(nameObj["family"]);
            Assert.Null(nameObj["given"]);

            // Now also set family to null
            nameObj["family"] = null;
            ElementNodeOperationExtensions.RemoveEmptyNodes(mutableNode);
            // name array item should be removed (empty object), leading to empty array removal
            Assert.Null(mutableNode["name"]);
        }
    }
}
