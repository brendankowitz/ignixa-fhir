// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Anonymizer.Models;
using Ignixa.Anonymizer.Processors;
using Xunit;

namespace Ignixa.Anonymizer.Core.UnitTests.Processors
{
    public class ResourceProcessorTests
    {
        private readonly ResourceProcessor _resourceProcessor;
        private readonly R4CoreSchemaProvider _schema = new();

        public ResourceProcessorTests()
        {
            _resourceProcessor = new ResourceProcessor(null, null);
        }

        [Fact]
        public void GivenAResourceWithoutSecurityLabels_WhenTryAddSecurityLabels_SecurityLabelsShouldBeAdded()
        {
            var json = """{"resourceType":"Patient"}""";
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            var result = new ProcessResult();
            result.AddProcessRecord(AnonymizationOperations.Redact, element);

            _resourceProcessor.AddSecurityTag(resourceNode, element, result);
            resourceNode.InvalidateCaches();

            var metaNode = resourceNode.MutableNode["meta"] as JsonObject;
            Assert.NotNull(metaNode);
            var securityArr = metaNode["security"] as JsonArray;
            Assert.NotNull(securityArr);
            Assert.Single(securityArr);
            Assert.Equal(SecurityLabels.REDACT.Code, (securityArr[0] as JsonObject)?["code"]?.GetValue<string>());
        }

        [Fact]
        public void GivenAResourceWithDifferentSecurityLabels_WhenTryAddSecurityLabels_SecurityLabelsShouldBeAddedWithoutRemovingOriginalOnes()
        {
            var json = """{"resourceType":"Patient","meta":{"security":[{"code":"MASKED"}]}}""";
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            var result = new ProcessResult();
            result.AddProcessRecord(AnonymizationOperations.Redact, element);

            _resourceProcessor.AddSecurityTag(resourceNode, element, result);
            resourceNode.InvalidateCaches();

            var metaNode = resourceNode.MutableNode["meta"] as JsonObject;
            var securityArr = metaNode["security"] as JsonArray;
            Assert.Equal(2, securityArr.Count);
            Assert.Equal("MASKED", (securityArr[0] as JsonObject)?["code"]?.GetValue<string>());
            Assert.Equal(SecurityLabels.REDACT.Code, (securityArr[1] as JsonObject)?["code"]?.GetValue<string>());
        }

        [Fact]
        public void GivenAResourceWithVersionId_WhenTryAddSecurityLabels_VersionIdShouldBeKept()
        {
            var json = """{"resourceType":"Patient","meta":{"versionId":"Test"}}""";
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            var result = new ProcessResult();
            result.AddProcessRecord(AnonymizationOperations.Redact, element);

            _resourceProcessor.AddSecurityTag(resourceNode, element, result);
            resourceNode.InvalidateCaches();

            var metaNode = resourceNode.MutableNode["meta"] as JsonObject;
            Assert.Equal("Test", metaNode["versionId"]?.GetValue<string>());
        }

        [Fact]
        public void GivenAResourceWithSameSecurityLabels_WhenTryAddSecurityLabels_SecurityLabelsShouldNotBeAddedAgain()
        {
            var json = """{"resourceType":"Patient","meta":{"security":[{"code":"REDACTED"}]}}""";
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            var result = new ProcessResult();
            result.AddProcessRecord(AnonymizationOperations.Redact, element);

            _resourceProcessor.AddSecurityTag(resourceNode, element, result);
            resourceNode.InvalidateCaches();

            var metaNode = resourceNode.MutableNode["meta"] as JsonObject;
            var securityArr = metaNode["security"] as JsonArray;
            Assert.Single(securityArr);
            Assert.Equal(SecurityLabels.REDACT.Code, (securityArr[0] as JsonObject)?["code"]?.GetValue<string>());
        }

        [Fact]
        public void GivenAResourceWithNoSecurityLabels_WhenTryAddMultipleSecurityLabels_SecurityLabelsShouldBeAddedAgain()
        {
            var json = """{"resourceType":"Patient"}""";
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            var result = new ProcessResult();

            result.AddProcessRecord(AnonymizationOperations.Redact, element);
            result.AddProcessRecord(AnonymizationOperations.Abstract, element);
            result.AddProcessRecord(AnonymizationOperations.Perturb, element);

            _resourceProcessor.AddSecurityTag(resourceNode, element, result);
            resourceNode.InvalidateCaches();

            var metaNode = resourceNode.MutableNode["meta"] as JsonObject;
            var securityArr = metaNode["security"] as JsonArray;
            Assert.Equal(3, securityArr.Count);

            var codes = securityArr.Select(s => (s as JsonObject)?["code"]?.GetValue<string>()).ToList();
            Assert.Contains(SecurityLabels.REDACT.Code, codes);
            Assert.Contains(SecurityLabels.ABSTRED.Code, codes);
            Assert.Contains(SecurityLabels.PERTURBED.Code, codes);
        }

        [Fact]
        public void GivenAResourceWithSecurityLabels_WhenTryAddMultipleSecurityLabels_SecurityLabelsShouldBeAddedAgain()
        {
            var json = """{"resourceType":"Patient","meta":{"security":[{"code":"REDACTED"},{"code":"ADDITION"}]}}""";
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            var result = new ProcessResult();

            result.AddProcessRecord(AnonymizationOperations.Redact, element);
            result.AddProcessRecord(AnonymizationOperations.Abstract, element);
            result.AddProcessRecord(AnonymizationOperations.Perturb, element);

            _resourceProcessor.AddSecurityTag(resourceNode, element, result);
            resourceNode.InvalidateCaches();

            var metaNode = resourceNode.MutableNode["meta"] as JsonObject;
            var securityArr = metaNode["security"] as JsonArray;
            Assert.Equal(4, securityArr.Count);

            var codes = securityArr.Select(s => (s as JsonObject)?["code"]?.GetValue<string>()).ToList();
            Assert.Contains(SecurityLabels.REDACT.Code, codes);
            Assert.Contains(SecurityLabels.ABSTRED.Code, codes);
            Assert.Contains(SecurityLabels.PERTURBED.Code, codes);
        }

        [Fact]
        public void GivenAResource_WhenTryToAddSecuritytagWithNoResult_MetaShouldNotBeChange()
        {
            var json = """{"resourceType":"Patient","meta":{"security":[{"code":"REDACTED"},{"code":"ADDITION"}]}}""";
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            var result = new ProcessResult();

            _resourceProcessor.AddSecurityTag(resourceNode, element, result);
            resourceNode.InvalidateCaches();

            var metaNode = resourceNode.MutableNode["meta"] as JsonObject;
            var securityArr = metaNode["security"] as JsonArray;
            Assert.Equal(2, securityArr.Count);

            json = """{"resourceType":"Patient"}""";
            resourceNode = ResourceJsonNode.Parse(json);
            element = resourceNode.ToElement(_schema);
            result = new ProcessResult();
            _resourceProcessor.AddSecurityTag(resourceNode, element, result);
            Assert.Null(resourceNode.MutableNode["meta"]);
        }
    }
}
