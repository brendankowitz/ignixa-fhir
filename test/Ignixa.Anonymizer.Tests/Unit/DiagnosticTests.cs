// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Anonymizer.AnonymizerConfigurations;
using Ignixa.Anonymizer.Extensions;
using Ignixa.Anonymizer.Processors;
using Xunit;
using Xunit.Abstractions;

namespace Ignixa.Anonymizer.Core.UnitTests
{
    public class DiagnosticTests
    {
        private readonly ITestOutputHelper _output;
        private readonly R4CoreSchemaProvider _schema = new();

        public DiagnosticTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void DiagnosticPropertyOrder_WhenAnonymizing_ShowsFullJsonOutput()
        {
            // Arrange
            var inputJson = """
            {
                "resourceType": "Patient",
                "id": "example",
                "identifier": [
                    {
                        "system": "http://example.org/ids",
                        "value": "12345"
                    }
                ],
                "name": [
                    {
                        "use": "official",
                        "family": "Chalmers"
                    }
                ]
            }
            """;

            var resourceNode = ResourceJsonNode.Parse(inputJson);
            var element = resourceNode.ToElement(_schema);

            // Create a redact rule for Patient.name
            var rule = new AnonymizationFhirPathRule(
                path: "Patient.name",
                expression: "name",
                resourceType: "Patient",
                method: "redact",
                type: AnonymizerRuleType.FhirPathRule,
                source: "test"
            );

            var rules = new[] { rule };

            // Create processors
            var redactProcessor = new RedactProcessor(
                enablePartialDatesForRedact: true,
                enablePartialAgesForRedact: true,
                enablePartialZipCodesForRedact: true,
                restrictedZipCodeTabulationAreas: new List<string>()
            );

            var processors = new Dictionary<string, IAnonymizerProcessor>
            {
                { "REDACT", redactProcessor }
            };

            // Act
            resourceNode.Anonymize(element, rules, processors);

            // Get the JSON output with indentation
            var options = new JsonSerializerOptions { WriteIndented = true };
            var actualJson = resourceNode.MutableNode.ToJsonString(options);

            _output.WriteLine("=== FULL ANONYMIZED OUTPUT ===");
            _output.WriteLine(actualJson);
            _output.WriteLine("=== END OUTPUT ===");

            // Parse to check property order
            var jsonObj = JsonNode.Parse(actualJson) as JsonObject;
            var propertyNames = jsonObj.Select(kvp => kvp.Key).ToList();

            _output.WriteLine("\n=== PROPERTY ORDER ===");
            for (int i = 0; i < propertyNames.Count; i++)
            {
                _output.WriteLine($"{i + 1}. {propertyNames[i]}");
            }
            _output.WriteLine("=== END PROPERTY ORDER ===");

            // Check if meta exists and where it is
            if (jsonObj.ContainsKey("meta"))
            {
                var metaIndex = propertyNames.IndexOf("meta");
                var idIndex = propertyNames.IndexOf("id");
                var identifierIndex = propertyNames.IndexOf("identifier");

                _output.WriteLine($"\nPosition of 'id': {idIndex}");
                _output.WriteLine($"Position of 'meta': {metaIndex}");
                _output.WriteLine($"Position of 'identifier': {identifierIndex}");

                if (metaIndex > identifierIndex)
                {
                    _output.WriteLine("\nISSUE: 'meta' appears AFTER 'identifier' (expected: after 'id', before 'identifier')");
                }
            }

            // Also test what happens with a fresh resource to see default ordering
            var freshJson = """{"resourceType":"Patient","id":"test","name":[{"family":"Test"}]}""";
            var freshNode = ResourceJsonNode.Parse(freshJson);
            var freshMutable = freshNode.MutableNode;

            // Manually add meta.security
            var metaObj = new JsonObject
            {
                ["security"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["system"] = "http://terminology.hl7.org/CodeSystem/v3-Confidentiality",
                        ["code"] = "REDACTED"
                    }
                }
            };
            freshMutable["meta"] = metaObj;

            _output.WriteLine("\n=== FRESH RESOURCE WITH META ADDED ===");
            _output.WriteLine(freshMutable.ToJsonString(options));

            var freshPropertyNames = freshMutable.Select(kvp => kvp.Key).ToList();
            _output.WriteLine("\n=== FRESH PROPERTY ORDER ===");
            for (int i = 0; i < freshPropertyNames.Count; i++)
            {
                _output.WriteLine($"{i + 1}. {freshPropertyNames[i]}");
            }

            // Test Meta<JsonObject>() for the root resource
            _output.WriteLine("\n=== TESTING Meta<JsonObject>() ===");
            var elementFromFresh = freshNode.ToElement(_schema);
            var metaJsonObject = elementFromFresh.Meta<JsonObject>();
            _output.WriteLine($"Meta<JsonObject>() is null: {metaJsonObject == null}");
            _output.WriteLine($"Meta<JsonObject>() == freshMutable: {metaJsonObject == freshMutable}");
            _output.WriteLine($"element.Location: {elementFromFresh.Location}");
            if (metaJsonObject != null)
            {
                _output.WriteLine($"Meta<JsonObject>() properties: {string.Join(", ", metaJsonObject.Select(kvp => kvp.Key))}");
            }

            // Test with a Bundle to see nested resource handling
            var bundleJson = """
            {
                "resourceType": "Bundle",
                "entry": [
                    {
                        "resource": {
                            "resourceType": "Patient",
                            "id": "nested"
                        }
                    }
                ]
            }
            """;
            var bundleNode = ResourceJsonNode.Parse(bundleJson);
            var bundleElement = bundleNode.ToElement(_schema);
            _output.WriteLine("\n=== BUNDLE STRUCTURE ===");
            _output.WriteLine($"Bundle element location: {bundleElement.Location}");

            // Navigate to the nested Patient
            var entryArray = bundleElement.Children("entry");
            foreach (var entry in entryArray)
            {
                _output.WriteLine($"Entry location: {entry.Location}");
                var nestedResource = entry.Children("resource").FirstOrDefault();
                if (nestedResource != null)
                {
                    _output.WriteLine($"Nested resource location: {nestedResource.Location}");
                    _output.WriteLine($"Nested resource type: {nestedResource.InstanceType}");
                    _output.WriteLine($"Nested resource IsFhirResource: {nestedResource.IsFhirResource()}");
                    var nestedMeta = nestedResource.Meta<JsonObject>();
                    _output.WriteLine($"Nested Meta<JsonObject>() is null: {nestedMeta == null}");
                    if (nestedMeta != null)
                    {
                        _output.WriteLine($"Nested meta properties: {string.Join(", ", nestedMeta.Select(kvp => kvp.Key))}");
                    }
                }
            }
        }
    }
}
