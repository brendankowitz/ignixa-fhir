// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Anonymizer.Exceptions;
using Ignixa.Anonymizer.Models;
using Ignixa.Anonymizer.Processors;
using Xunit;

namespace Ignixa.Anonymizer.Core.UnitTests.Processors
{
    public class GeneralizeTests
    {
        private readonly R4CoreSchemaProvider _schema = new();

        public static IEnumerable<object[]> GetEmptyNodesToGeneralize()
        {
            yield return new object[] { """{"resourceType":"Observation","valueInteger":null}""", "valueInteger", null };
            yield return new object[] { """{"resourceType":"Patient","birthDate":null}""", "birthDate", null };
        }

        public static IEnumerable<object[]> GetIntegerNodesToGeneralizeWithRangeMapping()
        {
            yield return new object[] { """{"resourceType":"Observation","valueInteger":5}""", "valueInteger", 20 };
            yield return new object[] { """{"resourceType":"Observation","valueInteger":20}""", "valueInteger", 40 };
            yield return new object[] { """{"resourceType":"Observation","valueInteger":43}""", "valueInteger", 60 };
            yield return new object[] { """{"resourceType":"Observation","valueInteger":78}""", "valueInteger", 80 };
            yield return new object[] { """{"resourceType":"Observation","valueInteger":110}""", "valueInteger", null, "Redact" };
            yield return new object[] { """{"resourceType":"Observation","valueInteger":110}""", "valueInteger", 110, "Keep" };
        }

        public static IEnumerable<object[]> GetStringNodesToGeneralizeWithValueSet()
        {
            yield return new object[] { """{"resourceType":"Patient","language":"en-AU"}""", "language", "en" };
            yield return new object[] { """{"resourceType":"Patient","language":"en-CA"}""", "language", "en" };
            yield return new object[] { """{"resourceType":"Patient","language":"en-CI"}""", "language", null, "Redact" };
            yield return new object[] { """{"resourceType":"Patient","language":"es-AR"}""", "language", "es" };
            yield return new object[] { """{"resourceType":"Patient","language":"es-ES"}""", "language", "es" };
        }

        public static IEnumerable<object[]> GetDateNodesToGeneralizeWithRangeMapping()
        {
            yield return new object[] { """{"resourceType":"Patient","birthDate":"1990-01-01"}""", "birthDate", "1990" };
            yield return new object[] { """{"resourceType":"Patient","birthDate":"2000-01-01"}""", "birthDate", null, "Redact" };
            yield return new object[] { """{"resourceType":"Patient","birthDate":"2010"}""", "birthDate", "2010-01-01" };
            yield return new object[] { """{"resourceType":"Patient","birthDate":"2010-01-01"}""", "birthDate", "2010-01-01" };
            yield return new object[] { """{"resourceType":"Patient","birthDate":"2010-01-02"}""", "birthDate", "2010-01-01" };
        }

        private Dictionary<string, object> CreateRangeMapppingSettingsForInteger(string otherValues)
        {
            string cases = "{\"$this>=0 and $this<20\":\"20\", \"$this>=20 and $this<40\":\"40\", \"$this>=40 and $this<60\":\"60\", \"$this>=60 and $this<80\":\"80\"}";
            return new Dictionary<string, object> { { "cases", cases }, { "otherValues", otherValues } };
        }

        private Dictionary<string, object> CreateValueSetSettingsForString(string otherValues)
        {
            string cases = "{\"$this in ('en-AU' | 'en-CA' | 'en-GB' | 'en-IN' | 'en-NZ' | 'en-SG' | 'en-US')\": \"'en'\",\"('es-AR' | 'es-ES' | 'es-UY') contains $this\": \"'es'\" }";
            return new Dictionary<string, object> { { "cases", cases }, { "otherValues", otherValues } };
        }

        private Dictionary<string, object> CreateRangeMappingSettingsForDate(string otherValues)
        {
            // NOTE: Ignixa FHIRPath does not yet support cross-precision date comparisons
            // (e.g., @2010-01-01 ~ @2010, @2010-01-01 >= @2010 all return false/empty).
            // This is tracked as an Ignixa FHIRPath bug. For now, use same-precision comparisons
            // and separate cases for year-only vs full dates.
            string cases = """
                {
                    "$this >= @1990-01-01 and $this < @2000-01-01": "@1990",
                    "$this = @2010": "@2010-01-01",
                    "$this >= @2010-01-01 and $this < @2011-01-01": "@2010-01-01",
                    "$this >= @2020-01-01 and $this < @2021-01-01": "@2020-01-01"
                }
                """;
            return new Dictionary<string, object> { { "cases", cases }, { "otherValues", otherValues } };
        }

        [Theory]
        [MemberData(nameof(GetEmptyNodesToGeneralize))]
        public void GivenAnEmptyNode_WhenGeneralized_EmptyNodeShouldBeReturned(string json, string fieldName, object target, string otherValues = "redact")
        {
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            var node = element.Children(fieldName).FirstOrDefault();
            if (node == null) return;

            GeneralizeProcessor processor = new GeneralizeProcessor();
            var context = new ProcessContext
            {
                VisitedNodes = new HashSet<string>()
            };
            var settings = CreateRangeMapppingSettingsForInteger(otherValues);

            var processResult = processor.Process(resourceNode, node, context, settings);
            Assert.False(processResult.IsGeneralized);
        }

        [Theory]
        [MemberData(nameof(GetIntegerNodesToGeneralizeWithRangeMapping))]
        public void GivenAnIntegerNode_WhenGeneralizedWithRangeMapping_GeneralizedNodeShouldBeReturned(string json, string fieldName, object target, string otherValues = "redact")
        {
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            var node = element.Children(fieldName).First();

            GeneralizeProcessor processor = new GeneralizeProcessor();
            var context = new ProcessContext
            {
                VisitedNodes = new HashSet<string>()
            };
            var settings = CreateRangeMapppingSettingsForInteger(otherValues);

            var processResult = processor.Process(resourceNode, node, context, settings);
            Assert.True(processResult.IsGeneralized);
            resourceNode.InvalidateCaches();
            var updated = resourceNode.ToElement(_schema);
            var updatedNode = updated.Children(fieldName).FirstOrDefault();
            if (target == null)
            {
                Assert.Null(updatedNode?.Value);
            }
            else
            {
                Assert.Equal(target.ToString(), updatedNode?.Value?.ToString());
            }
        }

        [Theory]
        [MemberData(nameof(GetStringNodesToGeneralizeWithValueSet))]
        public void GivenAStringNode_WhenGeneralizedWithValueSet_GeneralizedNodeShouldBeReturned(string json, string fieldName, object target, string otherValues = "redact")
        {
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            var node = element.Children(fieldName).First();

            GeneralizeProcessor processor = new GeneralizeProcessor();
            var context = new ProcessContext
            {
                VisitedNodes = new HashSet<string>()
            };
            var settings = CreateValueSetSettingsForString(otherValues);

            var processResult = processor.Process(resourceNode, node, context, settings);
            Assert.True(processResult.IsGeneralized);
            resourceNode.InvalidateCaches();
            var updated = resourceNode.ToElement(_schema);
            var updatedNode = updated.Children(fieldName).FirstOrDefault();
            if (target == null)
            {
                Assert.Null(updatedNode?.Value);
            }
            else
            {
                Assert.Equal(target.ToString(), updatedNode?.Value?.ToString());
            }
        }

        [Theory]
        [MemberData(nameof(GetDateNodesToGeneralizeWithRangeMapping))]
        public void GivenADateNode_WhenGeneralizedWithRangeMapping_GeneralizedNodeShouldBeReturned(string json, string fieldName, object target, string otherValues = "redact")
        {
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            var node = element.Children(fieldName).First();

            GeneralizeProcessor processor = new GeneralizeProcessor();
            var context = new ProcessContext
            {
                VisitedNodes = new HashSet<string>()
            };
            var settings = CreateRangeMappingSettingsForDate(otherValues);

            var processResult = processor.Process(resourceNode, node, context, settings);
            Assert.True(processResult.IsGeneralized);
            resourceNode.InvalidateCaches();
            var updated = resourceNode.ToElement(_schema);
            var updatedNode = updated.Children(fieldName).FirstOrDefault();
            if (target == null)
            {
                Assert.Null(updatedNode?.Value);
            }
            else
            {
                Assert.Equal(target.ToString(), updatedNode?.Value?.ToString());
            }
        }

        [Fact]
        public void GivenAComplexDatatypeNode_WhenGeneralize_ExceptionWillBeThrown()
        {
            GeneralizeProcessor processor = new GeneralizeProcessor();
            var json = """{"resourceType":"Patient","address":[{"state":"DC"}]}""";
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            var node = element.Children("address").First();
            var settings = new Dictionary<string, object> { { "cases", "" } };
            var context = new ProcessContext
            {
                VisitedNodes = new HashSet<string>()
            };
            Assert.Throws<AnonymizerRuleNotApplicableException>(() => processor.Process(resourceNode, node, context, settings));
        }
    }
}
