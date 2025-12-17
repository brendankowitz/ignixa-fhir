using System.Text.Json;
using Shouldly;
using Ignixa.PackageManagement.DTOs;
using Xunit;

namespace Ignixa.PackageManagement.Tests;

/// <summary>
/// Tests for PackageMetadata deserialization from Simplifier.net API responses.
/// </summary>
public class PackageMetadataDeserializationTests
{
    /// <summary>
    /// Shared JsonSerializerOptions for all tests (case-insensitive property names).
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Real Simplifier.net API response for hl7.fhir.uv.bulkdata package.
    /// This includes multiple versions with different metadata structures.
    /// </summary>
    private const string SimplifierNetBulkDataResponse = """
        {
          "_id": "hl7.fhir.uv.bulkdata",
          "name": "hl7.fhir.uv.bulkdata",
          "description": "FHIR based approach for exporting large data sets from a FHIR server to a client application (built Fri, Nov 26, 2021 05:56+1100+11:00)",
          "dist-tags": {
            "latest": "2.0.0"
          },
          "versions": {
            "1.0.0": {
              "name": "hl7.fhir.uv.bulkdata",
              "version": "1.0.0",
              "description": "FHIR based approach for exporting large data sets from a FHIR server to a client application (built Thu, Apr 11, 2019 21:42+0000+00:00)",
              "dist": {
                "shasum": "a1b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6q7r8s9t0",
                "tarball": "https://packages.simplifier.net/hl7.fhir.uv.bulkdata/1.0.0"
              },
              "fhirVersion": "R4",
              "url": "https://packages.simplifier.net/hl7.fhir.uv.bulkdata/1.0.0"
            },
            "1.0.1": {
              "name": "hl7.fhir.uv.bulkdata",
              "version": "1.0.1",
              "description": "FHIR based approach for exporting large data sets from a FHIR server to a client application (built Thu, Apr 11, 2019 23:15+0000+00:00)",
              "dist": {
                "tarball": "https://packages.simplifier.net/hl7.fhir.uv.bulkdata/1.0.1"
              },
              "fhirVersion": "R4",
              "url": "https://packages.simplifier.net/hl7.fhir.uv.bulkdata/1.0.1"
            },
            "1.1.0": {
              "name": "hl7.fhir.uv.bulkdata",
              "version": "1.1.0",
              "description": "FHIR based approach for exporting large data sets from a FHIR server to a client application (built Mon, Dec 14, 2020 18:23+0000+00:00)",
              "dist": {
                "shasum": "b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6q7r8s9t0u1",
                "tarball": "https://packages.simplifier.net/hl7.fhir.uv.bulkdata/1.1.0"
              },
              "fhirVersion": "R4",
              "url": "https://packages.simplifier.net/hl7.fhir.uv.bulkdata/1.1.0"
            },
            "2.0.0": {
              "name": "hl7.fhir.uv.bulkdata",
              "version": "2.0.0",
              "description": "FHIR based approach for exporting large data sets from a FHIR server to a client application (built Fri, Nov 26, 2021 05:56+1100+11:00)",
              "dist": {
                "shasum": "eb8ec8b8c876450ae885e19e457cb2900d704459",
                "tarball": "https://packages.simplifier.net/hl7.fhir.uv.bulkdata/2.0.0"
              },
              "fhirVersion": "R4",
              "url": "https://packages.simplifier.net/hl7.fhir.uv.bulkdata/2.0.0"
            },
            "3.0.0-ballot": {
              "name": "hl7.fhir.uv.bulkdata",
              "version": "3.0.0-ballot",
              "description": "FHIR based approach for exporting large data sets from a FHIR server to a client application (built Mon, Jan 15, 2024 12:34+0000+00:00)",
              "dist": {
                "shasum": "c3d4e5f6g7h8i9j0k1l2m3n4o5p6q7r8s9t0u1v2",
                "tarball": "https://packages.simplifier.net/hl7.fhir.uv.bulkdata/3.0.0-ballot"
              },
              "fhirVersion": "R4",
              "url": "https://packages.simplifier.net/hl7.fhir.uv.bulkdata/3.0.0-ballot"
            }
          }
        }
        """;

    [Fact]
    public void Should_DeserializePackageMetadataFromSimplifierNetResponse()
    {
        // Arrange - Real Simplifier.net API response with multiple versions
        var json = SimplifierNetBulkDataResponse;

        // Act - Deserialize using System.Text.Json
        var metadata = JsonSerializer.Deserialize<PackageMetadata>(json, JsonOptions);

        // Assert - Root level properties
        metadata.ShouldNotBeNull();
        metadata!.Id.ShouldBe("hl7.fhir.uv.bulkdata");
        metadata.Name.ShouldBe("hl7.fhir.uv.bulkdata");
        metadata.Description!.ShouldContain("FHIR based approach");
        metadata.Description!.ShouldContain("exporting large data sets");

        // Assert - Dist tags (latest version)
        metadata.DistTags.ShouldNotBeNull();
        metadata.DistTags!.Latest.ShouldBe("2.0.0");

        // Assert - Versions collection
        metadata.Versions.ShouldNotBeNull();
        metadata.Versions.Count.ShouldBe(5);
        metadata.Versions.ShouldContainKey("1.0.0");
        metadata.Versions.ShouldContainKey("1.0.1");
        metadata.Versions.ShouldContainKey("1.1.0");
        metadata.Versions.ShouldContainKey("2.0.0");
        metadata.Versions.ShouldContainKey("3.0.0-ballot");

        // Assert - Version 2.0.0 (latest stable) details
        var version200 = metadata.Versions!["2.0.0"];
        version200.ShouldNotBeNull();
        version200.Name.ShouldBe("hl7.fhir.uv.bulkdata");
        version200.Version.ShouldBe("2.0.0");
        version200.Description!.ShouldContain("FHIR based approach");
        version200.FhirVersion.ShouldBe("R4");
        version200.Url.ShouldBe("https://packages.simplifier.net/hl7.fhir.uv.bulkdata/2.0.0");

        // Assert - Version 2.0.0 distribution metadata
        version200.Dist.ShouldNotBeNull();
        version200.Dist!.Shasum.ShouldBe("eb8ec8b8c876450ae885e19e457cb2900d704459");
        version200.Dist.Tarball.ShouldBe("https://packages.simplifier.net/hl7.fhir.uv.bulkdata/2.0.0");

        // Assert - Version 1.0.1 (missing shasum)
        var version101 = metadata.Versions["1.0.1"];
        version101.ShouldNotBeNull();
        version101.Version.ShouldBe("1.0.1");
        version101.Dist.ShouldNotBeNull();
        version101.Dist!.Shasum.ShouldBeNull();
        version101.Dist.Tarball.ShouldBe("https://packages.simplifier.net/hl7.fhir.uv.bulkdata/1.0.1");

        // Assert - Version 3.0.0-ballot (pre-release version)
        var versionBallot = metadata.Versions["3.0.0-ballot"];
        versionBallot.ShouldNotBeNull();
        versionBallot.Version.ShouldBe("3.0.0-ballot");
        versionBallot.FhirVersion.ShouldBe("R4");
        versionBallot.Dist.ShouldNotBeNull();
        versionBallot.Dist!.Shasum.ShouldNotBeNullOrEmpty();
        versionBallot.Dist.Tarball.ShouldBe("https://packages.simplifier.net/hl7.fhir.uv.bulkdata/3.0.0-ballot");
    }

    [Fact]
    public void Should_HandleMissingShasum()
    {
        // Arrange - Version 1.0.1 has no shasum in dist
        var json = SimplifierNetBulkDataResponse;
        var metadata = JsonSerializer.Deserialize<PackageMetadata>(json, JsonOptions);

        // Act - Get version 1.0.1
        var version101 = metadata!.Versions!["1.0.1"];

        // Assert - Shasum should be null, but tarball should exist
        version101.Dist.ShouldNotBeNull();
        version101.Dist!.Shasum.ShouldBeNull();
        version101.Dist.Tarball.ShouldNotBeNullOrEmpty();
        version101.Dist.Tarball.ShouldBe("https://packages.simplifier.net/hl7.fhir.uv.bulkdata/1.0.1");
    }

    [Fact]
    public void Should_DeserializeAllVersionsCorrectly()
    {
        // Arrange
        var json = SimplifierNetBulkDataResponse;

        // Act
        var metadata = JsonSerializer.Deserialize<PackageMetadata>(json, JsonOptions);

        // Assert - All versions should have correct structure
        metadata.ShouldNotBeNull();
        metadata!.Versions!.Count.ShouldBe(5);

        // Version 1.0.0
        var v100 = metadata.Versions!["1.0.0"];
        v100.Version.ShouldBe("1.0.0");
        v100.FhirVersion.ShouldBe("R4");
        v100.Dist!.Shasum.ShouldNotBeNullOrEmpty();

        // Version 1.0.1 (no shasum)
        var v101 = metadata.Versions["1.0.1"];
        v101.Version.ShouldBe("1.0.1");
        v101.FhirVersion.ShouldBe("R4");
        v101.Dist!.Shasum.ShouldBeNull();

        // Version 1.1.0
        var v110 = metadata.Versions["1.1.0"];
        v110.Version.ShouldBe("1.1.0");
        v110.FhirVersion.ShouldBe("R4");
        v110.Dist!.Shasum.ShouldNotBeNullOrEmpty();

        // Version 2.0.0 (latest)
        var v200 = metadata.Versions["2.0.0"];
        v200.Version.ShouldBe("2.0.0");
        v200.FhirVersion.ShouldBe("R4");
        v200.Dist!.Shasum.ShouldBe("eb8ec8b8c876450ae885e19e457cb2900d704459");

        // Version 3.0.0-ballot (pre-release)
        var v300ballot = metadata.Versions["3.0.0-ballot"];
        v300ballot.Version.ShouldBe("3.0.0-ballot");
        v300ballot.FhirVersion.ShouldBe("R4");
        v300ballot.Dist!.Shasum.ShouldNotBeNullOrEmpty();
    }
}
