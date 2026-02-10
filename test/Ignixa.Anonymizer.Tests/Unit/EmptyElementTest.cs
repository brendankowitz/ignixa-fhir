// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Generic;
using System.IO;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Anonymizer.Models;
using Xunit;

namespace Ignixa.Anonymizer.Core.UnitTests
{
    public class EmptyElementTest
    {
        private readonly R4CoreSchemaProvider _schema = new();

        public static IEnumerable<object[]> EmptyElementFile()
        {
            yield return new object[] { "patient-empty.json"};
            yield return new object[] { "bundle-empty.json" };
            yield return new object[] { "condition-empty.json" };
        }

        public static IEnumerable<object[]> NonEmptyElementFile()
        {
            yield return new object[] { "contained-basic.json" };
            yield return new object[] { "bundle-basic.json" };
        }

        public static IEnumerable<object[]> NonEmptyElementContent()
        {
            yield return new object[] { null };
            yield return new object[] { "0" };
            yield return new object[] { "empty" };
            yield return new object[] { "{\"resourceType\":\"Patient\"}" };
        }

        [Theory]
        [MemberData(nameof(EmptyElementFile))]
        public void GivenEmptyElement_WhenCheckIFEmptyElement_ResultShouldBeTrue(string file)
        {
            var json = File.ReadAllText(Path.Join("TestResources", file));
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            Assert.True(EmptyElement.IsEmptyElement(element));
            Assert.True(EmptyElement.IsEmpty(element));
        }

        [Theory]
        [MemberData(nameof(EmptyElementFile))]
        public void GivenEmptyElementJson_WhenCheckIFEmptyElement_ResultShouldBeTrue(string file)
        {
            var json = File.ReadAllText(Path.Join("TestResources", file));
            Assert.True(EmptyElement.IsEmptyElement(json));
            Assert.True(EmptyElement.IsEmpty(json));
        }

        [Theory]
        [MemberData(nameof(NonEmptyElementFile))]
        public void GivenNonEmptyElementJson_WhenCheckIFEmptyElement_ResultShouldBeFalse(string file)
        {
            var json = File.ReadAllText(Path.Join("TestResources", file));
            Assert.False(EmptyElement.IsEmptyElement(json));
            Assert.False(EmptyElement.IsEmpty(json));
        }

        [Theory]
        [MemberData(nameof(NonEmptyElementFile))]
        public void GivenNonEmptyElement_WhenCheckIFEmptyElement_ResultShouldBeFalse(string file)
        {
            var json = File.ReadAllText(Path.Join("TestResources", file));
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            Assert.False(EmptyElement.IsEmptyElement(element));
            Assert.False(EmptyElement.IsEmpty(element));
        }

        [Theory]
        [MemberData(nameof(NonEmptyElementContent))]
        public void GivenNonEmptyElemetContent_WhenCheckIFEmptyElement_ResultShouldBeFalse(string content)
        {
            Assert.False(EmptyElement.IsEmptyElement(content));
            Assert.False(EmptyElement.IsEmpty(content));
        }
    }
}
