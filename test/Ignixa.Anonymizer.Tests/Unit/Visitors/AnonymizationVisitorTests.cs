// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Anonymizer.AnonymizerConfigurations;
using Ignixa.Anonymizer.Extensions;
using Ignixa.Anonymizer.Models;
using Ignixa.Anonymizer.Processors;
using Ignixa.Anonymizer.Utility;
using Ignixa.Anonymizer.Visitors;
using Xunit;

namespace Ignixa.Anonymizer.Core.UnitTests.Visitors
{
    public class AnonymizationVisitorTests
    {
        private readonly R4CoreSchemaProvider _schema = new();

        [Fact]
        public void GivenARedactRule_WhenProcess_NodeShouldBeRedact()
        {
            AnonymizationFhirPathRule[] rules = new AnonymizationFhirPathRule[]
            {
                new AnonymizationFhirPathRule("Patient.address", "address", "Patient", "redact", AnonymizerRuleType.FhirPathRule, "Patient.address"),
            };

            AnonymizationVisitor visitor = new AnonymizationVisitor(rules, CreateTestProcessors());

            var json = CreateTestPatientJson();
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            element.Accept(resourceNode, visitor);
            ElementNodeOperationExtensions.RemoveEmptyNodes(resourceNode.MutableNode);
            resourceNode.InvalidateCaches();

            // Verify top-level address was redacted (removed from the root)
            Assert.Null(resourceNode.MutableNode["address"]);
            // contact[0].address is a different path and should NOT be affected by Patient.address rule
            var contact = resourceNode.MutableNode["contact"]?[0]?["address"];
            Assert.NotNull(contact);
        }

        [Fact]
        public void GivenACryptoHashRule_WhenProcess_NodeShouldBeHashed()
        {
            AnonymizationFhirPathRule[] rules = new AnonymizationFhirPathRule[]
            {
                new AnonymizationFhirPathRule("Patient.address", "address", "Patient", "cryptoHash", AnonymizerRuleType.FhirPathRule, "Patient.address"),
            };

            AnonymizationVisitor visitor = new AnonymizationVisitor(rules, CreateTestProcessors());

            var json = CreateTestPatientJson();
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            element.Accept(resourceNode, visitor);
            ElementNodeOperationExtensions.RemoveEmptyNodes(resourceNode.MutableNode);
            resourceNode.InvalidateCaches();

            var updated = resourceNode.ToElement(_schema);
            var patientCity = updated.Children("address").First().Children("city").First();
            Assert.Equal("c4321653de997f3029d2efa38dd4baa6c9c2f6bd67b8a52be789f157f8b286ce", patientCity.Value.ToString());
        }

        [Fact]
        public void GivenAnEncryptRule_WhenProcess_NodeShouldBeEncrypted()
        {
            AnonymizationFhirPathRule[] rules = new AnonymizationFhirPathRule[]
            {
                new AnonymizationFhirPathRule("Patient.address", "address", "Patient", "encrypt", AnonymizerRuleType.FhirPathRule, "Patient.address"),
            };

            AnonymizationVisitor visitor = new AnonymizationVisitor(rules, CreateTestProcessors());

            var json = CreateTestPatientJson();
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            element.Accept(resourceNode, visitor);
            ElementNodeOperationExtensions.RemoveEmptyNodes(resourceNode.MutableNode);
            resourceNode.InvalidateCaches();

            var updated = resourceNode.ToElement(_schema);
            var patientCity = updated.Children("address").First().Children("city").First();
            var key = Encoding.UTF8.GetBytes("1234567890123456");
            var plainValue = EncryptUtility.DecryptTextFromBase64WithAes(patientCity.Value.ToString(), key);
            Assert.Equal("patienttestcity1", plainValue);
        }

        [Fact]
        public void GivenAPrimitiveSubstituteRule_WhenProcess_NodeShouldBeSubstituted()
        {
            AnonymizationFhirPathRule[] rules = new AnonymizationFhirPathRule[]
            {
                new AnonymizationFhirPathRule("Patient.address.city", "address.city", "Patient", "substitute", AnonymizerRuleType.FhirPathRule, "Patient.address.city",
                    new Dictionary<string, object> { {"replaceWith", "ExampleCity2020" } })
            };

            AnonymizationVisitor visitor = new AnonymizationVisitor(rules, CreateTestProcessors());

            var json = CreateTestPatientJson();
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);

            var patientCity = element.Children("address").First().Children("city").First();
            Assert.Equal("patienttestcity1", patientCity.Value.ToString());

            element.Accept(resourceNode, visitor);
            ElementNodeOperationExtensions.RemoveEmptyNodes(resourceNode.MutableNode);
            resourceNode.InvalidateCaches();

            var updated = resourceNode.ToElement(_schema);
            patientCity = updated.Children("address").First().Children("city").First();
            Assert.Equal("ExampleCity2020", patientCity.Value.ToString());
        }

        [Fact]
        public void GivenAPatientWithOnlyId_WhenProcess_NodeShouldBeRedact()
        {
            AnonymizationFhirPathRule[] rules = new AnonymizationFhirPathRule[]
            {
                new AnonymizationFhirPathRule("Patient.id", "id", "Patient", "redact", AnonymizerRuleType.FhirPathRule, "Patient.id"),
            };

            AnonymizationVisitor visitor = new AnonymizationVisitor(rules, CreateTestProcessors());

            var json = """{"resourceType":"Patient","id":"Test"}""";
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            element.Accept(resourceNode, visitor);
            ElementNodeOperationExtensions.RemoveEmptyNodes(resourceNode.MutableNode);
            resourceNode.InvalidateCaches();

            Assert.Null(resourceNode.MutableNode["id"]);
        }

        [Fact]
        public void Given2ConflictRules_WhenProcess_SecondRuleShouldBeIgnored()
        {
            AnonymizationFhirPathRule[] rules = new AnonymizationFhirPathRule[]
            {
                new AnonymizationFhirPathRule("Patient", "Patient", "Patient", "keep", AnonymizerRuleType.FhirPathRule, "Patient"),
                new AnonymizationFhirPathRule("Patient.address", "address", "Patient", "redact", AnonymizerRuleType.FhirPathRule, "Patient.address"),
            };

            AnonymizationVisitor visitor = new AnonymizationVisitor(rules, CreateTestProcessors());

            var json = CreateTestPatientJson();
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);
            element.Accept(resourceNode, visitor);
            resourceNode.InvalidateCaches();

            var updated = resourceNode.ToElement(_schema);
            var patientCity = updated.Children("address").First().Children("city").First();
            Assert.Equal("patienttestcity1", patientCity.Value.ToString());
        }

        private Dictionary<string, IAnonymizerProcessor> CreateTestProcessors()
        {
            KeepProcessor keepProcessor = new KeepProcessor();
            RedactProcessor redactProcessor = new RedactProcessor(false, false, false, null);
            DateShiftProcessor dateShiftProcessor = new DateShiftProcessor("123", "123", false);
            CryptoHashProcessor cryptoHashProcessor = new CryptoHashProcessor("123", _schema);
            EncryptProcessor encryptProcessor = new EncryptProcessor("1234567890123456");
            SubstituteProcessor substituteProcessor = new SubstituteProcessor();
            PerturbProcessor perturbProcessor = new PerturbProcessor(_schema);
            Dictionary<string, IAnonymizerProcessor> processors = new Dictionary<string, IAnonymizerProcessor>()
            {
                { "KEEP", keepProcessor},
                { "REDACT", redactProcessor},
                { "DATESHIFT", dateShiftProcessor},
                { "CRYPTOHASH", cryptoHashProcessor},
                { "ENCRYPT", encryptProcessor },
                { "SUBSTITUTE", substituteProcessor },
                { "PERTURB", perturbProcessor }
            };

            return processors;
        }

        private string CreateTestPatientJson()
        {
            return """{"resourceType":"Patient","active":true,"birthDate":"2000-01-01","address":[{"city":"patienttestcity1","country":"patienttestcountry1","district":"TestDistrict"}],"contact":[{"address":{"city":"patienttestcity2","country":"patienttestcountry2","postalCode":"12345"}}]}""";
        }
    }
}
