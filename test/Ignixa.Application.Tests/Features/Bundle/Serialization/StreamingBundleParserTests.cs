// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Ignixa.Application.Features.Bundle;
using Ignixa.Application.Features.Bundle.Serialization;

namespace Ignixa.Application.Tests.Features.Bundle.Serialization;

/// <summary>
/// Comprehensive unit tests for StreamingBundleParser.
/// Tests streaming JSON parsing with Utf8JsonReader and ArrayPool.
/// </summary>
public class StreamingBundleParserTests
{
    private readonly ILogger<StreamingBundleParser> _logger;
    private readonly StreamingBundleParser _parser;

    public StreamingBundleParserTests()
    {
        _logger = Substitute.For<ILogger<StreamingBundleParser>>();
        _parser = new StreamingBundleParser(_logger);
    }

    [Fact]
    public async Task ParseStreamAsync_WithSingleEntry_ReturnsOneEntry()
    {
        // Arrange
        var bundleJson = """
        {
          "resourceType": "Bundle",
          "type": "transaction",
          "entry": [
            {
              "resource": {
                "resourceType": "Patient",
                "id": "example-123",
                "name": [{"family": "Smith", "given": ["John"]}],
                "gender": "male"
              },
              "request": {
                "method": "PUT",
                "url": "Patient/example-123"
              }
            }
          ]
        }
        """;

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(bundleJson));

        // Act
        var context = await _parser.ParseStreamAsync(stream, CancellationToken.None);
        var entries = new List<BundleEntryContext>();
        await foreach (var entry in context.Entries)
        {
            entries.Add(entry);
        }

        // Assert
        context.BundleType.Should().Be("transaction");
        entries.Should().HaveCount(1);

        var entry0 = entries[0];
        entry0.Index.Should().Be(0);
        entry0.ResourceType.Should().Be("Patient");
        entry0.ResourceId.Should().Be("example-123");
        entry0.HttpVerb.Should().Be("PUT");
        entry0.RequestUrl.Should().Be("Patient/example-123");
        entry0.Resource.Should().NotBeNull();
        entry0.Resource!.Name.Should().Be("Patient");
        entry0.RawJson.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ParseStreamAsync_WithMultipleEntries_ReturnsAllEntriesInOrder()
    {
        // Arrange
        var bundleJson = """
        {
          "resourceType": "Bundle",
          "type": "transaction",
          "entry": [
            {
              "resource": {
                "resourceType": "Patient",
                "id": "patient-1"
              },
              "request": {
                "method": "PUT",
                "url": "Patient/patient-1"
              }
            },
            {
              "resource": {
                "resourceType": "Observation",
                "id": "obs-1",
                "status": "final"
              },
              "request": {
                "method": "POST",
                "url": "Observation"
              }
            },
            {
              "resource": {
                "resourceType": "Condition",
                "id": "condition-1"
              },
              "request": {
                "method": "PUT",
                "url": "Condition/condition-1"
              }
            }
          ]
        }
        """;

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(bundleJson));

        // Act
        var context = await _parser.ParseStreamAsync(stream, CancellationToken.None);
        var entries = new List<BundleEntryContext>();
        await foreach (var entry in context.Entries)
        {
            entries.Add(entry);
        }

        // Assert
        entries.Should().HaveCount(3);

        // Entry 0
        entries[0].Index.Should().Be(0);
        entries[0].ResourceType.Should().Be("Patient");
        entries[0].ResourceId.Should().Be("patient-1");
        entries[0].HttpVerb.Should().Be("PUT");
        entries[0].RequestUrl.Should().Be("Patient/patient-1");

        // Entry 1
        entries[1].Index.Should().Be(1);
        entries[1].ResourceType.Should().Be("Observation");
        entries[1].ResourceId.Should().BeNull(); // POST to collection
        entries[1].HttpVerb.Should().Be("POST");
        entries[1].RequestUrl.Should().Be("Observation");

        // Entry 2
        entries[2].Index.Should().Be(2);
        entries[2].ResourceType.Should().Be("Condition");
        entries[2].ResourceId.Should().Be("condition-1");
        entries[2].HttpVerb.Should().Be("PUT");
        entries[2].RequestUrl.Should().Be("Condition/condition-1");
    }

    [Fact]
    public async Task ParseStreamAsync_CapturesRawJson_ForEachEntry()
    {
        // Arrange
        var bundleJson = """
        {
          "resourceType": "Bundle",
          "type": "transaction",
          "entry": [
            {
              "resource": {
                "resourceType": "Patient",
                "id": "patient-1",
                "active": true,
                "name": [{"family": "Doe", "given": ["Jane"]}]
              },
              "request": {
                "method": "PUT",
                "url": "Patient/patient-1"
              }
            }
          ]
        }
        """;

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(bundleJson));

        // Act
        var context = await _parser.ParseStreamAsync(stream, CancellationToken.None);
        var entries = new List<BundleEntryContext>();
        await foreach (var entry in context.Entries)
        {
            entries.Add(entry);
        }

        // Assert
        entries.Should().HaveCount(1);
        var rawJson = entries[0].RawJson;

        rawJson.Should().NotBeNullOrEmpty();

        // Verify RawJson is valid JSON
        var parsedJson = JsonDocument.Parse(rawJson!);
        parsedJson.RootElement.GetProperty("resourceType").GetString().Should().Be("Patient");
        parsedJson.RootElement.GetProperty("id").GetString().Should().Be("patient-1");
        parsedJson.RootElement.GetProperty("active").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ParseStreamAsync_WithFullUrl_CapturesFullUrl()
    {
        // Arrange
        var bundleJson = """
        {
          "resourceType": "Bundle",
          "type": "transaction",
          "entry": [
            {
              "fullUrl": "urn:uuid:05efabf0-4be2-4561-91ce-51548425acb9",
              "resource": {
                "resourceType": "Patient",
                "name": [{"family": "Smith"}]
              },
              "request": {
                "method": "POST",
                "url": "Patient"
              }
            }
          ]
        }
        """;

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(bundleJson));

        // Act
        var context = await _parser.ParseStreamAsync(stream, CancellationToken.None);
        var entries = new List<BundleEntryContext>();
        await foreach (var entry in context.Entries)
        {
            entries.Add(entry);
        }

        // Assert
        entries.Should().HaveCount(1);
        entries[0].FullUrl.Should().Be("urn:uuid:05efabf0-4be2-4561-91ce-51548425acb9");
    }

    [Fact]
    public async Task ParseStreamAsync_WithEmptyBundle_ReturnsNoEntries()
    {
        // Arrange
        var bundleJson = """
        {
          "resourceType": "Bundle",
          "type": "transaction",
          "entry": []
        }
        """;

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(bundleJson));

        // Act
        var context = await _parser.ParseStreamAsync(stream, CancellationToken.None);
        var entries = new List<BundleEntryContext>();
        await foreach (var entry in context.Entries)
        {
            entries.Add(entry);
        }

        // Assert
        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseStreamAsync_WithBundleWithoutEntryArray_ReturnsNoEntries()
    {
        // Arrange
        var bundleJson = """
        {
          "resourceType": "Bundle",
          "type": "searchset",
          "total": 0
        }
        """;

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(bundleJson));

        // Act
        var context = await _parser.ParseStreamAsync(stream, CancellationToken.None);
        var entries = new List<BundleEntryContext>();
        await foreach (var entry in context.Entries)
        {
            entries.Add(entry);
        }

        // Assert
        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseStreamAsync_WithLargeBundle_ParsesAllEntries()
    {
        // Arrange - Create bundle with 100 entries
        // Tests the parser's ability to handle larger bundles and properly exit the parsing loop
        // after all entries are consumed. Previous bug: parser would loop infinitely with unconsumed
        // closing brackets after completing all entries.
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"resourceType\": \"Bundle\",");
        sb.AppendLine("  \"type\": \"transaction\",");
        sb.AppendLine("  \"entry\": [");

        for (int i = 0; i < 100; i++)
        {
            if (i > 0)
            {
                sb.AppendLine(",");
            }

            sb.AppendLine("    {");
            sb.AppendLine("      \"resource\": {");
            sb.AppendLine("        \"resourceType\": \"Patient\",");
            sb.AppendLine($"        \"id\": \"patient-{i}\"");
            sb.AppendLine("      },");
            sb.AppendLine("      \"request\": {");
            sb.AppendLine("        \"method\": \"PUT\",");
            sb.AppendLine($"        \"url\": \"Patient/patient-{i}\"");
            sb.AppendLine("      }");
            sb.Append("    }");
        }

        sb.AppendLine();
        sb.AppendLine("  ]");
        sb.AppendLine("}");

        var bundleJson = sb.ToString();
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(bundleJson));

        // Act
        var context = await _parser.ParseStreamAsync(stream, CancellationToken.None);
        var entries = new List<BundleEntryContext>();
        await foreach (var entry in context.Entries)
        {
            entries.Add(entry);
        }

        // Assert
        entries.Should().HaveCount(100);

        // Verify indices are sequential
        for (int i = 0; i < 100; i++)
        {
            entries[i].Index.Should().Be(i);
            entries[i].ResourceId.Should().Be($"patient-{i}");
        }
    }

    [Fact]
    public async Task ParseStreamAsync_WithNestedObjects_CapturesCorrectly()
    {
        // Arrange
        var bundleJson = """
        {
          "resourceType": "Bundle",
          "type": "transaction",
          "entry": [
            {
              "resource": {
                "resourceType": "Patient",
                "id": "patient-1",
                "name": [
                  {
                    "use": "official",
                    "family": "Smith",
                    "given": ["John", "Jacob"]
                  }
                ],
                "telecom": [
                  {
                    "system": "phone",
                    "value": "555-1234",
                    "use": "home"
                  }
                ],
                "address": [
                  {
                    "use": "home",
                    "line": ["123 Main St", "Apt 4B"],
                    "city": "Springfield",
                    "state": "IL"
                  }
                ]
              },
              "request": {
                "method": "PUT",
                "url": "Patient/patient-1"
              }
            }
          ]
        }
        """;

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(bundleJson));

        // Act
        var context = await _parser.ParseStreamAsync(stream, CancellationToken.None);
        var entries = new List<BundleEntryContext>();
        await foreach (var entry in context.Entries)
        {
            entries.Add(entry);
        }

        // Assert
        entries.Should().HaveCount(1);
        var rawJson = entries[0].RawJson;

        rawJson.Should().NotBeNullOrEmpty();

        // Verify nested structures are preserved
        var parsedJson = JsonDocument.Parse(rawJson!);
        var nameArray = parsedJson.RootElement.GetProperty("name");
        nameArray.GetArrayLength().Should().Be(1);
        nameArray[0].GetProperty("family").GetString().Should().Be("Smith");

        var givenArray = nameArray[0].GetProperty("given");
        givenArray.GetArrayLength().Should().Be(2);
        givenArray[0].GetString().Should().Be("John");
        givenArray[1].GetString().Should().Be("Jacob");
    }

    [Fact]
    public async Task ParseStreamAsync_WithDifferentResourceTypes_CapturesCorrectly()
    {
        // Arrange
        var bundleJson = """
        {
          "resourceType": "Bundle",
          "type": "transaction",
          "entry": [
            {
              "resource": {
                "resourceType": "Patient",
                "id": "patient-1"
              },
              "request": {
                "method": "PUT",
                "url": "Patient/patient-1"
              }
            },
            {
              "resource": {
                "resourceType": "Observation",
                "id": "obs-1",
                "status": "final",
                "code": {
                  "coding": [{"system": "http://loinc.org", "code": "8867-4"}]
                },
                "valueQuantity": {
                  "value": 120,
                  "unit": "mmHg"
                }
              },
              "request": {
                "method": "PUT",
                "url": "Observation/obs-1"
              }
            },
            {
              "resource": {
                "resourceType": "Condition",
                "id": "condition-1",
                "clinicalStatus": {
                  "coding": [{"code": "active"}]
                }
              },
              "request": {
                "method": "PUT",
                "url": "Condition/condition-1"
              }
            }
          ]
        }
        """;

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(bundleJson));

        // Act
        var context = await _parser.ParseStreamAsync(stream, CancellationToken.None);
        var entries = new List<BundleEntryContext>();
        await foreach (var entry in context.Entries)
        {
            entries.Add(entry);
        }

        // Assert
        entries.Should().HaveCount(3);
        entries[0].ResourceType.Should().Be("Patient");
        entries[1].ResourceType.Should().Be("Observation");
        entries[2].ResourceType.Should().Be("Condition");

        // Verify RawJson for Observation includes nested structures
        var obsJson = JsonDocument.Parse(entries[1].RawJson!);
        obsJson.RootElement.GetProperty("valueQuantity").GetProperty("value").GetDouble().Should().Be(120);
    }

    [Fact]
    public async Task ParseStreamAsync_WithSpecialCharacters_EscapesCorrectly()
    {
        // Arrange
        var bundleJson = """
        {
          "resourceType": "Bundle",
          "type": "transaction",
          "entry": [
            {
              "resource": {
                "resourceType": "Patient",
                "id": "patient-1",
                "name": [{"family": "O'Brien", "given": ["John\nMiddle"]}],
                "text": {
                  "div": "<div>Patient \"Smith\"</div>"
                }
              },
              "request": {
                "method": "PUT",
                "url": "Patient/patient-1"
              }
            }
          ]
        }
        """;

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(bundleJson));

        // Act
        var context = await _parser.ParseStreamAsync(stream, CancellationToken.None);
        var entries = new List<BundleEntryContext>();
        await foreach (var entry in context.Entries)
        {
            entries.Add(entry);
        }

        // Assert
        entries.Should().HaveCount(1);
        var rawJson = entries[0].RawJson;

        rawJson.Should().NotBeNullOrEmpty();

        // Verify special characters are preserved
        var parsedJson = JsonDocument.Parse(rawJson!);
        parsedJson.RootElement.GetProperty("name")[0].GetProperty("family").GetString().Should().Be("O'Brien");
    }

    [Fact]
    public async Task ParseStreamAsync_WithNumbers_PreservesNumericTypes()
    {
        // Arrange
        var bundleJson = """
        {
          "resourceType": "Bundle",
          "type": "transaction",
          "entry": [
            {
              "resource": {
                "resourceType": "Observation",
                "id": "obs-1",
                "valueQuantity": {
                  "value": 98.6,
                  "unit": "degrees F"
                },
                "component": [
                  {
                    "valueInteger": 42
                  }
                ]
              },
              "request": {
                "method": "PUT",
                "url": "Observation/obs-1"
              }
            }
          ]
        }
        """;

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(bundleJson));

        // Act
        var context = await _parser.ParseStreamAsync(stream, CancellationToken.None);
        var entries = new List<BundleEntryContext>();
        await foreach (var entry in context.Entries)
        {
            entries.Add(entry);
        }

        // Assert
        entries.Should().HaveCount(1);
        var rawJson = entries[0].RawJson;

        var parsedJson = JsonDocument.Parse(rawJson!);
        parsedJson.RootElement.GetProperty("valueQuantity").GetProperty("value").GetDouble().Should().Be(98.6);
        parsedJson.RootElement.GetProperty("component")[0].GetProperty("valueInteger").GetInt32().Should().Be(42);
    }

    [Fact]
    public async Task ParseStreamAsync_WithBooleanAndNull_PreservesTypes()
    {
        // Arrange
        var bundleJson = """
        {
          "resourceType": "Bundle",
          "type": "transaction",
          "entry": [
            {
              "resource": {
                "resourceType": "Patient",
                "id": "patient-1",
                "active": true,
                "deceased": false,
                "maritalStatus": null
              },
              "request": {
                "method": "PUT",
                "url": "Patient/patient-1"
              }
            }
          ]
        }
        """;

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(bundleJson));

        // Act
        var context = await _parser.ParseStreamAsync(stream, CancellationToken.None);
        var entries = new List<BundleEntryContext>();
        await foreach (var entry in context.Entries)
        {
            entries.Add(entry);
        }

        // Assert
        entries.Should().HaveCount(1);
        var rawJson = entries[0].RawJson;

        var parsedJson = JsonDocument.Parse(rawJson!);
        parsedJson.RootElement.GetProperty("active").GetBoolean().Should().BeTrue();
        parsedJson.RootElement.GetProperty("deceased").GetBoolean().Should().BeFalse();
        parsedJson.RootElement.GetProperty("maritalStatus").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task ParseStreamAsync_WithCancellationToken_RespectsCancellation()
    {
        // Arrange
        var bundleJson = """
        {
          "resourceType": "Bundle",
          "type": "transaction",
          "entry": [
            {
              "resource": {
                "resourceType": "Patient",
                "id": "patient-1"
              },
              "request": {
                "method": "PUT",
                "url": "Patient/patient-1"
              }
            }
          ]
        }
        """;

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(bundleJson));
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act & Assert
        // TaskCanceledException inherits from OperationCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            var context = await _parser.ParseStreamAsync(stream, cts.Token);
            await foreach (var entry in context.Entries.WithCancellation(cts.Token))
            {
                // Should not reach here
            }
        });
    }

    [Fact]
    public async Task ParseStreamAsync_WithMalformedJson_ThrowsJsonException()
    {
        // Arrange
        var malformedJson = """
        {
          "resourceType": "Bundle",
          "type": "transaction",
          "entry": [
            {
              "resource": {
                "resourceType": "Patient",
                "id": "patient-1"
              }
              // Missing closing braces
        """;

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(malformedJson));

        // Act & Assert
        // JsonReaderException inherits from JsonException
        await Assert.ThrowsAnyAsync<JsonException>(async () =>
        {
            var context = await _parser.ParseStreamAsync(stream, CancellationToken.None);
            await foreach (var entry in context.Entries)
            {
                // Should throw before yielding entries
            }
        });
    }

    [Fact]
    public async Task ParseStreamAsync_WithNullStream_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await _parser.ParseStreamAsync(null!, CancellationToken.None);
        });

        exception.Should().NotBeNull();
    }

    [Fact]
    public async Task ParseStreamAsync_WithVerySmallStream_WorksCorrectly()
    {
        // Arrange - Test with minimal valid bundle
        var bundleJson = "{\"resourceType\":\"Bundle\",\"entry\":[]}";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(bundleJson));

        // Act
        var context = await _parser.ParseStreamAsync(stream, CancellationToken.None);
        var entries = new List<BundleEntryContext>();
        await foreach (var entry in context.Entries)
        {
            entries.Add(entry);
        }

        // Assert
        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseStreamAsync_WithDELETEOperation_WorksWithoutResource()
    {
        // Arrange
        var bundleJson = """
        {
          "resourceType": "Bundle",
          "type": "transaction",
          "entry": [
            {
              "request": {
                "method": "DELETE",
                "url": "Patient/patient-1"
              }
            }
          ]
        }
        """;

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(bundleJson));

        // Act
        var context = await _parser.ParseStreamAsync(stream, CancellationToken.None);
        var entries = new List<BundleEntryContext>();
        await foreach (var entry in context.Entries)
        {
            entries.Add(entry);
        }

        // Assert
        entries.Should().HaveCount(1);
        entries[0].HttpVerb.Should().Be("DELETE");
        entries[0].RequestUrl.Should().Be("Patient/patient-1");
        entries[0].ResourceId.Should().Be("patient-1");
        entries[0].Resource.Should().BeNull();
        entries[0].RawJson.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task ParseStreamAsync_WithGETOperation_WorksWithoutResource()
    {
        // Arrange
        var bundleJson = """
        {
          "resourceType": "Bundle",
          "type": "batch",
          "entry": [
            {
              "request": {
                "method": "GET",
                "url": "Patient/patient-1"
              }
            }
          ]
        }
        """;

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(bundleJson));

        // Act
        var context = await _parser.ParseStreamAsync(stream, CancellationToken.None);
        var entries = new List<BundleEntryContext>();
        await foreach (var entry in context.Entries)
        {
            entries.Add(entry);
        }

        // Assert
        entries.Should().HaveCount(1);
        entries[0].HttpVerb.Should().Be("GET");
        entries[0].RequestUrl.Should().Be("Patient/patient-1");
        entries[0].Resource.Should().BeNull();
    }

    [Fact]
    public async Task ParseStreamAsync_WithMixedOperations_HandlesCorrectly()
    {
        // Arrange
        var bundleJson = """
        {
          "resourceType": "Bundle",
          "type": "transaction",
          "entry": [
            {
              "resource": {
                "resourceType": "Patient",
                "id": "patient-1"
              },
              "request": {
                "method": "PUT",
                "url": "Patient/patient-1"
              }
            },
            {
              "request": {
                "method": "DELETE",
                "url": "Patient/patient-2"
              }
            },
            {
              "request": {
                "method": "GET",
                "url": "Patient/patient-3"
              }
            },
            {
              "resource": {
                "resourceType": "Observation",
                "status": "final"
              },
              "request": {
                "method": "POST",
                "url": "Observation"
              }
            }
          ]
        }
        """;

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(bundleJson));

        // Act
        var context = await _parser.ParseStreamAsync(stream, CancellationToken.None);
        var entries = new List<BundleEntryContext>();
        await foreach (var entry in context.Entries)
        {
            entries.Add(entry);
        }

        // Assert
        entries.Should().HaveCount(4);

        // PUT with resource
        entries[0].HttpVerb.Should().Be("PUT");
        entries[0].Resource.Should().NotBeNull();
        entries[0].RawJson.Should().NotBeNullOrEmpty();

        // DELETE without resource
        entries[1].HttpVerb.Should().Be("DELETE");
        entries[1].Resource.Should().BeNull();
        entries[1].RawJson.Should().BeNullOrEmpty();

        // GET without resource
        entries[2].HttpVerb.Should().Be("GET");
        entries[2].Resource.Should().BeNull();

        // POST with resource
        entries[3].HttpVerb.Should().Be("POST");
        entries[3].Resource.Should().NotBeNull();
        entries[3].RawJson.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ParseStreamAsync_WithUrlQueryString_ExtractsIdCorrectly()
    {
        // Arrange
        var bundleJson = """
        {
          "resourceType": "Bundle",
          "type": "batch",
          "entry": [
            {
              "request": {
                "method": "GET",
                "url": "Patient?name=Smith&_count=10"
              }
            },
            {
              "request": {
                "method": "GET",
                "url": "Patient/patient-1?_format=json"
              }
            }
          ]
        }
        """;

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(bundleJson));

        // Act
        var context = await _parser.ParseStreamAsync(stream, CancellationToken.None);
        var entries = new List<BundleEntryContext>();
        await foreach (var entry in context.Entries)
        {
            entries.Add(entry);
        }

        // Assert
        entries.Should().HaveCount(2);

        // Query without ID
        entries[0].RequestUrl.Should().Be("Patient?name=Smith&_count=10");
        entries[0].ResourceId.Should().BeNull();

        // ID with query string
        entries[1].RequestUrl.Should().Be("Patient/patient-1?_format=json");
        entries[1].ResourceId.Should().Be("patient-1");
    }

    [Fact]
    public async Task ParseStreamAsync_ReturnsCorrectResourceType()
    {
        // Arrange
        var bundleJson = """
        {
          "resourceType": "Bundle",
          "type": "transaction",
          "entry": []
        }
        """;

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(bundleJson));

        // Act
        var context = await _parser.ParseStreamAsync(stream, CancellationToken.None);

        // Assert
        context.ResourceType.Should().Be("Bundle");
        context.ParsingIssues.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseStreamAsync_ReturnsCorrectBundleType()
    {
        // Arrange
        var bundleJson = """
        {
          "resourceType": "Bundle",
          "type": "transaction",
          "entry": []
        }
        """;

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(bundleJson));

        // Act
        var context = await _parser.ParseStreamAsync(stream, CancellationToken.None);

        // Assert
        context.BundleType.Should().Be("transaction");
    }

    [Fact]
    public async Task ParseStreamAsync_WithMultipleBundleTypes_ReturnsCorrectTypes()
    {
        // Arrange - Test various bundle types
        var testCases = new[]
        {
            ("transaction", "transaction"),
            ("batch", "batch"),
            ("searchset", "searchset"),
            ("collection", "collection"),
            ("document", "document")
        };

        foreach (var (inputType, expectedType) in testCases)
        {
            var bundleJson = $$"""
            {
              "resourceType": "Bundle",
              "type": "{{inputType}}",
              "entry": []
            }
            """;

            var stream = new MemoryStream(Encoding.UTF8.GetBytes(bundleJson));

            // Act
            var context = await _parser.ParseStreamAsync(stream, CancellationToken.None);

            // Assert
            context.BundleType.Should().Be(expectedType, $"for input type '{inputType}'");
        }
    }

    [Fact]
    public async Task ParseStreamAsync_WithInvalidResourceType_AddsParsingIssue()
    {
        // Arrange
        var bundleJson = """
        {
          "resourceType": "Patient",
          "type": "transaction",
          "entry": []
        }
        """;

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(bundleJson));

        // Act
        var context = await _parser.ParseStreamAsync(stream, CancellationToken.None);

        // Assert
        context.ResourceType.Should().Be("Patient");
        context.ParsingIssues.Should().Contain(issue => issue.Contains("Expected resourceType 'Bundle'"));
    }

    [Fact]
    public async Task ParseStreamAsync_WithInvalidBundleType_AddsParsingIssue()
    {
        // Arrange
        var bundleJson = """
        {
          "resourceType": "Bundle",
          "type": "invalid-type",
          "entry": []
        }
        """;

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(bundleJson));

        // Act
        var context = await _parser.ParseStreamAsync(stream, CancellationToken.None);

        // Assert
        context.BundleType.Should().Be("invalid-type");
        context.ParsingIssues.Should().Contain(issue => issue.Contains("Unknown bundle type"));
    }

    [Fact]
    public async Task ParseStreamAsync_WithLinks_ParsesLinksCorrectly()
    {
        // Arrange
        var bundleJson = """
        {
          "resourceType": "Bundle",
          "type": "searchset",
          "link": [
            {
              "relation": "self",
              "url": "https://example.com/Patient?_count=10"
            },
            {
              "relation": "next",
              "url": "https://example.com/Patient?_count=10&_page=2"
            }
          ],
          "entry": []
        }
        """;

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(bundleJson));

        // Act
        var context = await _parser.ParseStreamAsync(stream, CancellationToken.None);

        // Assert
        context.Links.Should().HaveCount(2);

        var selfLink = context.Links.FirstOrDefault(l => l.Relation == "self");
        selfLink.Should().NotBeNull();
        selfLink!.Url.Should().Be("https://example.com/Patient?_count=10");

        var nextLink = context.Links.FirstOrDefault(l => l.Relation == "next");
        nextLink.Should().NotBeNull();
        nextLink!.Url.Should().Be("https://example.com/Patient?_count=10&_page=2");
    }

    [Fact]
    public async Task ParseStreamAsync_WithMultipleLinks_ParsesAllLinks()
    {
        // Arrange
        var bundleJson = """
        {
          "resourceType": "Bundle",
          "type": "searchset",
          "link": [
            {
              "relation": "self",
              "url": "https://example.com/Patient?_count=10&_page=3"
            },
            {
              "relation": "first",
              "url": "https://example.com/Patient?_count=10"
            },
            {
              "relation": "prev",
              "url": "https://example.com/Patient?_count=10&_page=2"
            },
            {
              "relation": "next",
              "url": "https://example.com/Patient?_count=10&_page=4"
            },
            {
              "relation": "last",
              "url": "https://example.com/Patient?_count=10&_page=10"
            }
          ],
          "entry": []
        }
        """;

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(bundleJson));

        // Act
        var context = await _parser.ParseStreamAsync(stream, CancellationToken.None);

        // Assert
        context.Links.Should().HaveCount(5);
        var expectedRelations = new[] { "self", "first", "prev", "next", "last" };
        context.Links.Select(l => l.Relation).Should().Contain(expectedRelations);
    }

    [Fact]
    public async Task ParseStreamAsync_WithNoLinks_ReturnsEmptyLinksList()
    {
        // Arrange
        var bundleJson = """
        {
          "resourceType": "Bundle",
          "type": "transaction",
          "entry": []
        }
        """;

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(bundleJson));

        // Act
        var context = await _parser.ParseStreamAsync(stream, CancellationToken.None);

        // Assert
        context.Links.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseStreamAsync_WithNoBundleType_ReturnsNullBundleType()
    {
        // Arrange
        var bundleJson = """
        {
          "resourceType": "Bundle",
          "entry": []
        }
        """;

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(bundleJson));

        // Act
        var context = await _parser.ParseStreamAsync(stream, CancellationToken.None);

        // Assert
        context.BundleType.Should().BeNull();
        context.ResourceType.Should().Be("Bundle");
    }

    [Fact]
    public async Task ParseStreamAsync_WithCompleteMetadataAndEntries_ParsesBothCorrectly()
    {
        // Arrange
        var bundleJson = """
        {
          "resourceType": "Bundle",
          "type": "searchset",
          "total": 2,
          "link": [
            {
              "relation": "self",
              "url": "https://example.com/Patient"
            }
          ],
          "entry": [
            {
              "resource": {
                "resourceType": "Patient",
                "id": "patient-1"
              },
              "request": {
                "method": "GET",
                "url": "Patient/patient-1"
              }
            },
            {
              "resource": {
                "resourceType": "Patient",
                "id": "patient-2"
              },
              "request": {
                "method": "GET",
                "url": "Patient/patient-2"
              }
            }
          ]
        }
        """;

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(bundleJson));

        // Act
        var context = await _parser.ParseStreamAsync(stream, CancellationToken.None);

        // Assert - Metadata
        context.ResourceType.Should().Be("Bundle");
        context.BundleType.Should().Be("searchset");
        context.Links.Should().HaveCount(1);
        context.Links[0].Relation.Should().Be("self");
        context.ParsingIssues.Should().BeEmpty();

        // Assert - Entries
        var entries = new List<BundleEntryContext>();
        await foreach (var entry in context.Entries)
        {
            entries.Add(entry);
        }

        entries.Should().HaveCount(2);
        entries[0].ResourceId.Should().Be("patient-1");
        entries[1].ResourceId.Should().Be("patient-2");
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new StreamingBundleParser(null!));
    }
}
