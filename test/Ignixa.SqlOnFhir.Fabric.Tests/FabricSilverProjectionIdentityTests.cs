// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.SourceNodes;
using static Ignixa.SqlOnFhir.Fabric.Tests.SilverProjectionHarness;
using static Ignixa.SqlOnFhir.Fabric.Tests.SyntheticSilverRoot;

namespace Ignixa.SqlOnFhir.Fabric.Tests;

public class FabricSilverProjectionIdentityTests
{
    private const string Patient = """{"resourceType":"Patient","id":"p1"}""";

    /// <summary>SHA-256 of "example.org/fhir/Patient/p1", computed independently of the library.</summary>
    private const string PatientP1Id = "5133c96e332c4f3f2018ec97341dc548b5ff01b59e4d40be6cb41c1542cc9145";

    [Fact]
    public async Task GivenAResource_WhenProjected_ThenIdHashesTheStrippedNamespaceTypeAndSourceId()
    {
        var projection = await CreateAsync("Patient");

        var row = projection.Project(Patient);

        row.Id.ShouldBe(PatientP1Id);
        row["id"].GetString().ShouldBe(PatientP1Id);
        row.IdOrig.ShouldBe("p1");
        row.ResourceType.ShouldBe("Patient");
        row["resourceType"].GetString().ShouldBe("Patient");
    }

    [Theory]
    [InlineData("https://example.org/fhir")]
    [InlineData("http://example.org/fhir")]
    [InlineData("https://example.org/fhir/")]
    [InlineData("example.org/fhir")]
    [InlineData("custom_scheme1://example.org/fhir")]
    public async Task GivenNamespacesDifferingOnlyInSchemeOrTrailingSlash_WhenProjected_ThenIdsAreEqual(string sourceNamespace)
    {
        var projection = await CreateAsync("Patient");

        var row = projection.Project(Patient, sourceNamespace);

        row.Id.ShouldBe(PatientP1Id);
        row["msftSourceSystem"].GetString().ShouldBe(sourceNamespace);
    }

    [Theory]
    [InlineData("https://example.org/fhir//")]
    [InlineData("https://other.example.org/fhir")]
    [InlineData("https://example.org/FHIR")]
    [InlineData("https:/example.org/fhir")]
    [InlineData("://example.org/fhir")]
    [InlineData("urn_namespace")]
    public async Task GivenOtherNamespaces_WhenProjected_ThenIdsAreIsolated(string sourceNamespace)
    {
        var projection = await CreateAsync("Patient");

        var row = projection.Project(Patient, sourceNamespace);

        row.Id.ShouldNotBe(PatientP1Id);
        row.Id.ShouldBe(Hash(Strip(sourceNamespace), "Patient", "p1"));
    }

    [Fact]
    public async Task GivenTheSameSourceIdInAnotherResourceType_WhenProjected_ThenIdDiffers()
    {
        var patient = await CreateAsync("Patient");
        var observation = await CreateAsync("Observation");

        var observationRow = observation.Project("""{"resourceType":"Observation","id":"p1","status":"final"}""");

        observationRow.Id.ShouldBe(Hash(StrippedNamespace, "Observation", "p1"));
        observationRow.Id.ShouldNotBe(patient.Project(Patient).Id);
    }

    [Fact]
    public async Task GivenASourceId_WhenProjected_ThenTheHashUsesItsUtf8Text()
    {
        var projection = await CreateAsync("Patient");

        var row = projection.Project("""{"resourceType":"Patient","id":"A-1.b"}""");

        row.Id.ShouldBe(Hash(StrippedNamespace, "Patient", "A-1.b"));
        row.IdOrig.ShouldBe("A-1.b");
    }

    [Fact]
    public async Task GivenTheSameResourceTwice_WhenProjected_ThenRowsAreIdentical()
    {
        var projection = await CreateAsync(
            "Patient",
            Field("identifier", ArrayOf(Identifier(Reference()))),
            Field("managingOrganization", Reference()),
            Field("managingOrganization_string", "string"));
        const string Json = """
            {"resourceType":"Patient","id":"p1","identifier":[{"system":"urn:mrn","value":"7"}],
             "managingOrganization":{"reference":"Organization/o1"}}
            """;

        var first = projection.Project(Json);
        var second = projection.Project(Json);

        second.Cells.Select(cell => cell.GetRawText()).ShouldBe(first.Cells.Select(cell => cell.GetRawText()));
    }

    [Fact]
    public async Task GivenAnIngestion_WhenProjected_ThenLineageColumnsCarryItTruncatedToMicroseconds()
    {
        var projection = await CreateAsync("Patient");

        var row = projection.Project(Patient);

        var truncated = new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.Zero).AddTicks(1_234_560);
        row["msftModifiedDatetime"].GetString().ShouldBe("2024-05-06T07:08:09.123456Z");
        row["msftCreatedDatetime"].GetString().ShouldBe("2024-05-06T07:08:09.123456Z");
        row["msftFilePath"].GetString().ShouldBe(FilePath);
        row["msftSourceSystem"].GetString().ShouldBe(Namespace);
        row.ModifiedDatetime.ShouldBe(truncated);
        row.CreatedDatetime.ShouldBe(truncated);
    }

    [Fact]
    public async Task GivenAContract_WhenProjected_ThenCellsFollowContractOrder()
    {
        var projection = await CreateAsync("Patient", Field("gender", "string"), Field("active", "boolean"));

        var row = projection.Project("""{"active":true,"gender":"other","id":"p1","resourceType":"Patient"}""");

        string[] expected =
        [
            "msftModifiedDatetime", "id", "resourceType", "msftSourceSystem", "msftFilePath", "gender", "active", "msftCreatedDatetime",
        ];
        projection.ColumnNames.ShouldBe(expected);
        row.ColumnNames.ShouldBe(expected);
        row.Cells.Count.ShouldBe(expected.Length);
        row.Cells[5].GetString().ShouldBe("other");
        row.Cells[6].GetBoolean().ShouldBeTrue();
        projection.ResourceType.ShouldBe("Patient");
    }

    [Fact]
    public async Task GivenAnUnknownColumnName_WhenIndexed_ThenKeyNotFound()
    {
        var row = (await CreateAsync("Patient")).Project(Patient);

        Should.Throw<KeyNotFoundException>(() => row["Id"]);
    }

    [Fact]
    public async Task GivenARow_WhenCreatedDatetimeReplaced_ThenOnlyThatCellChanges()
    {
        var row = (await CreateAsync("Patient")).Project(Patient);
        var created = new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero).AddTicks(9_999_999);

        var replaced = row.WithCreatedDatetime(created);

        replaced["msftCreatedDatetime"].GetString().ShouldBe("2020-01-02T03:04:05.999999Z");
        replaced.CreatedDatetime.ShouldBe(created.AddTicks(-9));
        replaced.ModifiedDatetime.ShouldBe(row.ModifiedDatetime);
        replaced["msftModifiedDatetime"].GetString().ShouldBe(row["msftModifiedDatetime"].GetString());
        replaced.Id.ShouldBe(row.Id);
        replaced.IdOrig.ShouldBe(row.IdOrig);
        replaced.ColumnNames.ShouldBe(row.ColumnNames);
        row["msftCreatedDatetime"].GetString().ShouldBe("2024-05-06T07:08:09.123456Z");
    }

    [Fact]
    public async Task GivenANonUtcInstant_WhenCreatedDatetimeReplaced_ThenRejected()
    {
        var row = (await CreateAsync("Patient")).Project(Patient);

        Should.Throw<ArgumentException>(() => row.WithCreatedDatetime(new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.FromHours(1))));
    }

    [Fact]
    public async Task GivenAnotherResourceType_WhenProjected_ThenRejectedAtResourceType()
    {
        var projection = await CreateAsync("Patient");

        projection.Rejection("""{"resourceType":"Group","id":"p1"}""").Path.ShouldBe("resourceType");
    }

    [Theory]
    [InlineData("""{"resourceType":"Patient"}""")]
    [InlineData("""{"resourceType":"Patient","id":""}""")]
    [InlineData("""{"resourceType":"Patient","id":7}""")]
    [InlineData("""{"resourceType":"Patient","id":null}""")]
    public async Task GivenNoUsableId_WhenProjected_ThenRejectedAtId(string json)
    {
        var projection = await CreateAsync("Patient");

        projection.Rejection(json).Path.ShouldBe("id");
    }

    [Fact]
    public async Task GivenMetaLastUpdated_WhenProjected_ThenExposedInUtc()
    {
        var projection = await CreateAsync("Patient");

        var row = projection.Project("""{"resourceType":"Patient","id":"p1","meta":{"lastUpdated":"2023-04-05T06:07:08.123456+02:00"}}""");

        row.MetaLastUpdated.ShouldBe(new DateTimeOffset(2023, 4, 5, 4, 7, 8, TimeSpan.Zero).AddTicks(1_234_560));
        row.MetaLastUpdated!.Value.Offset.ShouldBe(TimeSpan.Zero);
        projection.Project(Patient).MetaLastUpdated.ShouldBeNull();
        projection.Project("""{"resourceType":"Patient","id":"p1","meta":{}}""").MetaLastUpdated.ShouldBeNull();
    }

    [Theory]
    [InlineData("\"yesterday\"")]
    [InlineData("12")]
    [InlineData("\"2023-04-05T06:07:08.1234567Z\"")]
    public async Task GivenAnInvalidMetaLastUpdated_WhenProjected_ThenRejectedAtItsPath(string value)
    {
        var projection = await CreateAsync("Patient");

        var error = projection.Rejection($$$"""{"resourceType":"Patient","id":"p1","meta":{"lastUpdated":{{{value}}}}}""");

        error.Path.ShouldBe("meta.lastUpdated");
        error.Message.ShouldContain("must be a FHIR date, dateTime or instant");
    }

    [Fact]
    public async Task GivenAPartialMetaLastUpdated_WhenProjected_ThenItIsItsFirstInstantInUtc()
    {
        var projection = await CreateAsync("Patient");

        var row = projection.Project("""{"resourceType":"Patient","id":"p1","meta":{"lastUpdated":"2023-04"}}""");

        row.MetaLastUpdated.ShouldBe(new DateTimeOffset(2023, 4, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task GivenARejectedValue_WhenProjected_ThenTheMessageNamesThePathButNotTheValue()
    {
        const string Secret = "invented-secret-marker";
        var projection = await CreateAsync("Patient", Field("birthDate", "date"));

        var error = projection.Rejection($$"""{"resourceType":"Patient","id":"p1","birthDate":"{{Secret}}"}""");

        error.Path.ShouldBe("birthDate");
        error.Message.ShouldContain("birthDate");
        error.Message.ShouldNotContain(Secret);
    }

    [Theory]
    [InlineData(" ", FilePath)]
    [InlineData("https://", FilePath)]
    [InlineData("/", FilePath)]
    [InlineData(Namespace, " ")]
    public void GivenBlankLineage_WhenIngestionCreated_ThenRejected(string sourceNamespace, string filePath)
    {
        Should.Throw<ArgumentException>(() => new FabricSilverIngestion(sourceNamespace, filePath, IngestedAt));
    }

    [Fact]
    public void GivenANonUtcInstant_WhenIngestionCreated_ThenRejected()
    {
        Should.Throw<ArgumentException>(
            () => new FabricSilverIngestion(Namespace, FilePath, new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.FromHours(-5))));
    }

    [Fact]
    public void GivenAnIngestion_WhenCreated_ThenItKeepsLineageVerbatimAndTruncatesTheInstant()
    {
        var ingestion = new FabricSilverIngestion("HTTPS://Example.org/fhir/", "a b/c.ndjson", IngestedAt);

        ingestion.SourceNamespace.ShouldBe("HTTPS://Example.org/fhir/");
        ingestion.FilePath.ShouldBe("a b/c.ndjson");
        ingestion.IngestedAt.ShouldBe(new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.Zero).AddTicks(1_234_560));
    }

    [Fact]
    public async Task GivenNullArguments_WhenProjected_ThenRejected()
    {
        var projection = await CreateAsync("Patient");

        Should.Throw<ArgumentNullException>(() => projection.Project(null!, Ingestion()));
        Should.Throw<ArgumentNullException>(() => projection.Project(ResourceJsonNode.Parse(Patient), null!));
    }

    private static string Strip(string sourceNamespace)
    {
        var schemeEnd = sourceNamespace.IndexOf("://", StringComparison.Ordinal);
        var stripped = schemeEnd > 0 ? sourceNamespace[(schemeEnd + 3)..] : sourceNamespace;
        return stripped.EndsWith('/') ? stripped[..^1] : stripped;
    }
}
