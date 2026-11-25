// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using static Ignixa.SqlOnFhir.Fabric.Tests.SilverProjectionHarness;
using static Ignixa.SqlOnFhir.Fabric.Tests.SyntheticSilverRoot;

namespace Ignixa.SqlOnFhir.Fabric.Tests;

/// <summary>
/// How source values become cells: the column type alone decides the shape, string columns take any token as text,
/// and every other leaf must hold the matching token.
/// </summary>
public class FabricSilverProjectionValueTests
{
    [Theory]
    [InlineData("\"female\"", "female")]
    [InlineData("5", "5")]
    [InlineData("1.50", "1.50")]
    [InlineData("true", "true")]
    [InlineData("""{"b":[1,null],"a":"x"}""", """{"b":[1,null],"a":"x"}""")]
    [InlineData("""[ {"url" : "u"} ]""", """[{"url":"u"}]""")]
    [InlineData("\"a/b é\"", "a/b é")]
    public async Task GivenAnyToken_WhenReadIntoAStringColumn_ThenItIsTheStringOrCompactJsonText(string value, string expected)
    {
        var cell = await Cell("value", "string", value);

        cell.GetString().ShouldBe(expected);
    }

    [Fact]
    public async Task GivenStructuredTextWithSpecialCharacters_WhenReadIntoAStringColumn_ThenOnlyQuotesBackslashesAndControlsAreEscaped()
    {
        var cell = await Cell("extension", "string", """[{"url":"a\"b\\c/d\u0001\u001f\b\t\n\f\ré<>&'"}]""");

        cell.GetString().ShouldBe("""[{"url":"a\"b\\c/d\u0001\u001F\b\t\n\f\ré<>&'"}]""");
    }

    [Fact]
    public async Task GivenContainedResources_WhenReadIntoAStringColumn_ThenTheyAreJsonText()
    {
        var cell = await Cell("contained", "string", """[{"resourceType":"Organization","id":"o1","active":false}]""");

        cell.GetString().ShouldBe("""[{"resourceType":"Organization","id":"o1","active":false}]""");
    }

    [Fact]
    public async Task GivenAMissingOrNullValue_WhenProjected_ThenTheCellIsNull()
    {
        var projection = await CreateAsync("Patient", Field("gender", "string"), Field("name", ArrayOf(Strings("text"))));

        var missing = projection.Project("""{"resourceType":"Patient","id":"p1"}""");
        var nulls = projection.Project("""{"resourceType":"Patient","id":"p1","gender":null,"name":null}""");

        missing["gender"].ValueKind.ShouldBe(JsonValueKind.Null);
        missing["name"].ValueKind.ShouldBe(JsonValueKind.Null);
        nulls["gender"].ValueKind.ShouldBe(JsonValueKind.Null);
        nulls["name"].ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task GivenAnEmptyObject_WhenReadIntoAStruct_ThenItIsAnAllNullStruct()
    {
        var projection = await CreateAsync("Patient", Field("maritalStatus", CodeableConcept()), Field("maritalStatus_string", "string"));

        var row = projection.Project("""{"resourceType":"Patient","id":"p1","maritalStatus":{}}""");

        row["maritalStatus"].ShouldBeJson(NullConcept);
        row["maritalStatus_string"].GetString().ShouldBe(NullConcept);
    }

    [Fact]
    public async Task GivenPropertiesTheSchemaDoesNotName_WhenProjected_ThenTheyAreIgnored()
    {
        var projection = await CreateAsync("Patient", Field("maritalStatus", Strings("text")), Field("birthDate", "date"));

        var row = projection.Project("""
            {"resourceType":"Patient","id":"p1","_birthDate":{"extension":[]},"birthDate":"2001-02-03",
             "maritalStatus":{"text":"M","_text":{"id":"t"},"unknown":[1]},"unknown":{"x":1}}
            """);

        row["maritalStatus"].ShouldBeJson("""{"text":"M"}""");
        row["birthDate"].GetString().ShouldBe("2001-02-03");
    }

    [Theory]
    [InlineData("boolean", "true", "true")]
    [InlineData("boolean", "false", "false")]
    [InlineData("integer", "-2147483648", "-2147483648")]
    [InlineData("integer", "2147483647", "2147483647")]
    [InlineData("long", "9223372036854775807", "9223372036854775807")]
    [InlineData("short", "-32768", "-32768")]
    [InlineData("byte", "127", "127")]
    [InlineData("date", "\"2001-02-03\"", "\"2001-02-03\"")]
    [InlineData("date", "\"2001-02\"", "\"2001-02-01\"")]
    [InlineData("date", "\"2001\"", "\"2001-01-01\"")]
    [InlineData("date", "\"2000-02-29\"", "\"2000-02-29\"")]
    [InlineData("date", "\"0001\"", "\"0001-01-01\"")]
    [InlineData("date", "\"2001-12-01\"", "\"2001-12-01\"")]
    [InlineData("short", "32767", "32767")]
    [InlineData("byte", "-128", "-128")]
    [InlineData("timestamp", "\"0001-01-01T00:00:00Z\"", "\"0001-01-01T00:00:00.000000Z\"")]
    [InlineData("timestamp", "\"2001-02-03T23:59:59+05:59\"", "\"2001-02-03T18:00:59.000000Z\"")]
    [InlineData("timestamp", "\"2001-02-03T04:05:06.5\"", "\"2001-02-03T04:05:06.500000Z\"")]
    [InlineData("decimal(36,18)", "1200E-20", "0.000000000000000012")]
    [InlineData("decimal(36,18)", "0e-99999999999", "0.000000000000000000")]
    [InlineData("timestamp", "\"2001-02-03T04:05:06.123456+02:00\"", "\"2001-02-03T02:05:06.123456Z\"")]
    [InlineData("timestamp", "\"2001-02-03T04:05:06-05:30\"", "\"2001-02-03T09:35:06.000000Z\"")]
    [InlineData("timestamp", "\"2001-02-03T04:05:06.1Z\"", "\"2001-02-03T04:05:06.100000Z\"")]
    [InlineData("timestamp", "\"2001-02-03T04:05:06.123456000Z\"", "\"2001-02-03T04:05:06.123456Z\"")]
    [InlineData("timestamp", "\"2001-02-03T23:30:00-14:00\"", "\"2001-02-04T13:30:00.000000Z\"")]
    [InlineData("timestamp", "\"2001-02-03T04:05:06\"", "\"2001-02-03T04:05:06.000000Z\"")]
    [InlineData("timestamp", "\"2001-02-03\"", "\"2001-02-03T00:00:00.000000Z\"")]
    [InlineData("timestamp", "\"2001-02\"", "\"2001-02-01T00:00:00.000000Z\"")]
    [InlineData("timestamp", "\"2001\"", "\"2001-01-01T00:00:00.000000Z\"")]
    [InlineData("decimal(36,18)", "1.5", "1.500000000000000000")]
    [InlineData("decimal(36,18)", "-0.25", "-0.250000000000000000")]
    [InlineData("decimal(36,18)", "1E+2", "100.000000000000000000")]
    [InlineData("decimal(36,18)", "2.50000000000000000000", "2.500000000000000000")]
    [InlineData("decimal(36,18)", "999999999999999999.999999999999999999", "999999999999999999.999999999999999999")]
    [InlineData("decimal(5,0)", "12345", "12345")]
    [InlineData("decimal(5,0)", "0", "0")]
    [InlineData("decimal(4,2)", "-0.0", "0.00")]
    public async Task GivenAValidLeaf_WhenProjected_ThenTheCellHasTheRowEncoding(string type, string value, string expected)
    {
        var cell = await Cell("value", type, value);

        cell.GetRawText().ShouldBe(expected);
    }

    [Theory]
    [InlineData("boolean", "\"true\"")]
    [InlineData("boolean", "1")]
    [InlineData("integer", "2147483648")]
    [InlineData("integer", "1.0")]
    [InlineData("integer", "1e2")]
    [InlineData("integer", "\"1\"")]
    [InlineData("short", "32768")]
    [InlineData("byte", "-129")]
    [InlineData("long", "9223372036854775808")]
    [InlineData("date", "\"2001-02-30\"")]
    [InlineData("date", "\"2001-13\"")]
    [InlineData("date", "\"0000\"")]
    [InlineData("date", "\"2001-00\"")]
    [InlineData("date", "\"2001-01-00\"")]
    [InlineData("date", "\"2001-1\"")]
    [InlineData("date", "\"2001-1x\"")]
    [InlineData("timestamp", "\"2001-02-03T04:05:06.12345601Z\"")]
    [InlineData("decimal(36,18)", "1e99999999999")]
    [InlineData("decimal(36,18)", "15e-19")]
    [InlineData("decimal(36,18)", "1e-20")]
    [InlineData("decimal(36,18)", "105e-20")]
    [InlineData("date", "\"01-02-03\"")]
    [InlineData("date", "\"2001-02-03T00:00:00Z\"")]
    [InlineData("date", "20010203")]
    [InlineData("timestamp", "\"2001-02-03T04:05:06.1234567Z\"")]
    [InlineData("timestamp", "\"2001-02-03T04:05:06.Z\"")]
    [InlineData("timestamp", "\"2001-02-03T24:00:00Z\"")]
    [InlineData("timestamp", "\"2001-02-03T04:60:00Z\"")]
    [InlineData("timestamp", "\"2001-02-03T04:05:60Z\"")]
    [InlineData("timestamp", "\"2001-02-03T04:05Z\"")]
    [InlineData("timestamp", "\"2001-02-03T04:05:06+14:01\"")]
    [InlineData("timestamp", "\"2001-02-03T04:05:06+15:00\"")]
    [InlineData("timestamp", "\"2001-02-03T04:05:06+02:60\"")]
    [InlineData("timestamp", "\"2001-02-03T04:05:06+0200\"")]
    [InlineData("timestamp", "\"2001-02-03T04:05:06Zjunk\"")]
    [InlineData("timestamp", "\"2001-02-03 04:05:06Z\"")]
    [InlineData("timestamp", "\"0001-01-01T00:00:00+01:00\"")]
    [InlineData("timestamp", "\"9999-12-31T23:59:59-01:00\"")]
    [InlineData("timestamp", "1")]
    [InlineData("decimal(36,18)", "0.0000000000000000001")]
    [InlineData("decimal(36,18)", "1000000000000000000")]
    [InlineData("decimal(36,18)", "1e-1000")]
    [InlineData("decimal(36,18)", "\"1.5\"")]
    [InlineData("decimal(5,0)", "0.5")]
    [InlineData("decimal(4,2)", "100")]
    public async Task GivenAnInvalidLeaf_WhenProjected_ThenRejectedAtItsPath(string type, string value)
    {
        var projection = await CreateAsync("Patient", Field("value", type));

        projection.Rejection($$"""{"resourceType":"Patient","id":"p1","value":{{value}}}""").Path.ShouldBe("value");
    }

    [Fact]
    public async Task GivenALongFractionAndAnExponentPastSixDigits_WhenProjected_ThenTheExactValueIsKept()
    {
        // 0.<999,999 zeros>1 × 10^1,000,001 is exactly 10.
        var value = "0." + new string('0', 999_999) + "1e1000001";

        var cell = await Cell("value", "decimal(36,18)", value);

        cell.GetRawText().ShouldBe("10.000000000000000000");
    }

    [Fact]
    public async Task GivenALongFractionAndANegativeExponentPastSixDigits_WhenProjected_ThenRejected()
    {
        // A negative exponent only moves the digit further right, so it stays below every column scale.
        var projection = await CreateAsync("Patient", Field("value", "decimal(36,18)"));
        var value = "0." + new string('0', 999_999) + "1e-1000001";

        projection.Rejection($$"""{"resourceType":"Patient","id":"p1","value":{{value}}}""").Path.ShouldBe("value");
    }

    [Theory]
    [InlineData("1e999999999999")]
    [InlineData("1e1000000000000")]
    [InlineData("1e99999999999999999999999999999999999999")]
    [InlineData("1e-99999999999999999999999999999999999999")]
    public async Task GivenAHugeExponent_WhenProjected_ThenRejected(string value)
    {
        var projection = await CreateAsync("Patient", Field("value", "decimal(36,18)"));

        projection.Rejection($$"""{"resourceType":"Patient","id":"p1","value":{{value}}}""").Path.ShouldBe("value");
    }

    [Fact]
    public async Task GivenALongFractionAndASaturatedExponent_WhenProjected_ThenRejected()
    {
        var projection = await CreateAsync("Patient", Field("value", "decimal(36,18)"));
        var value = "0." + new string('0', 999_999) + "1e" + new string('9', 40);

        projection.Rejection($$"""{"resourceType":"Patient","id":"p1","value":{{value}}}""").Path.ShouldBe("value");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenDerivedColumnsDeclaredBeforeOrAfterTheirBase_WhenProjected_ThenTheyHoldTheSameValues(bool baseFirst)
    {
        var concept = CodeableConcept();
        JsonNode[] bases =
        [
            Field("maritalStatus", CodeableConcept()),
            Field("x", Struct(Field("code", concept))),
        ];
        JsonNode[] derived =
        [
            Field("x_code_string", "string", nullable: false),
            Field("maritalStatus_string", "string", nullable: false),
            Field("x_code", concept.DeepClone(), nullable: false),
        ];
        var projection = await CreateAsync("Patient", baseFirst ? [.. bases, .. derived] : [.. derived, .. bases]);
        const string Concept = """{"extension":null,"id":null,"coding":null,"text":"T"}""";

        var row = projection.Project("""{"resourceType":"Patient","id":"p1","maritalStatus":{"text":"T"},"x":{"code":{"text":"T"}}}""");
        JsonElement At(string name) => row.Cells[row.ColumnNames.ToList().IndexOf(name)];

        projection.ColumnNames.Skip(5).Take(5).ShouldBe(
            baseFirst
                ? ["maritalStatus", "x", "x_code_string", "maritalStatus_string", "x_code"]
                : ["x_code_string", "maritalStatus_string", "x_code", "maritalStatus", "x"]);
        At("maritalStatus_string").GetString().ShouldBe(Concept);
        At("x_code").ShouldBeJson(Concept);
        At("x_code_string").GetString().ShouldBe(Concept);
        At("maritalStatus").ShouldBeJson(Concept);
    }

    [Fact]
    public async Task GivenAnOrigColumn_WhenProjected_ThenItHoldsTheSourceValueAsText()
    {
        var projection = await CreateAsync(
            "Patient",
            Field("idOrig", "string"),
            Field("birthDate", "date"),
            Field("birthDateOrig", "string"));

        var row = projection.Project("""{"resourceType":"Patient","id":"p1","birthDate":"1970"}""");

        row["idOrig"].GetString().ShouldBe("p1");
        row["birthDate"].GetString().ShouldBe("1970-01-01");
        row["birthDateOrig"].GetString().ShouldBe("1970");
        row["id"].GetString().ShouldBe(Hash(StrippedNamespace, "Patient", "p1"));
    }

    [Fact]
    public async Task GivenAnOrigFieldInsideAStruct_WhenProjected_ThenItHoldsTheSourceValueOfItsSibling()
    {
        var projection = await CreateAsync(
            "Patient",
            Field("period", Struct(Field("start", "timestamp"), Field("startOrig", "string"))));

        var row = projection.Project("""{"resourceType":"Patient","id":"p1","period":{"start":"2001-02-03T04:05:06+01:00"}}""");

        row["period"].ShouldBeJson("""{"start":"2001-02-03T03:05:06.000000Z","startOrig":"2001-02-03T04:05:06+01:00"}""");
    }

    [Fact]
    public async Task GivenAFieldThatExtendsASiblingNameWithoutTheOrigSuffix_WhenProjected_ThenItIsReadByItsOwnName()
    {
        var projection = await CreateAsync(
            "Patient",
            Field("concept", Struct(Field("code", "string"), Field("codeText", "string"))));

        var row = projection.Project("""{"resourceType":"Patient","id":"p1","concept":{"code":"c","codeText":"t"}}""");

        row["concept"].ShouldBeJson("""{"code":"c","codeText":"t"}""");
    }

    [Fact]
    public async Task GivenAComplexColumn_WhenProjected_ThenItsCompanionIsJsonTextOfTheFinalValue()
    {
        var projection = await CreateAsync(
            "Observation",
            Field("valueQuantity", Struct(Field("value", "decimal(36,18)"), Field("unit", "string"), Field("flag", "boolean"))),
            Field("valueQuantity_string", "string"),
            Field("effectivePeriod", Period()),
            Field("effectivePeriod_string", "string"),
            Field("component", ArrayOf(Struct(Field("valueInteger", "integer"), Field("valueDate", "date")))),
            Field("component_string", "string"));

        var row = projection.Project("""
            {"resourceType":"Observation","id":"o1","status":"final",
             "valueQuantity":{"value":0,"unit":"a\"b\u0002é"},
             "effectivePeriod":{"start":"2001-02-03T04:05:06.987654+01:00"},
             "component":[{"valueInteger":-3,"valueDate":"2001"},{}]}
            """);

        row["valueQuantity"].ShouldBeJson("""{"value":0.000000000000000000,"unit":"a\"b\u0002é","flag":null}""");
        row["valueQuantity_string"].GetString().ShouldBe("""{"value":0E-18,"unit":"a\"b\u0002é","flag":null}""");
        row["effectivePeriod"].ShouldBeJson("""{"extension":null,"id":null,"start":"2001-02-03T03:05:06.987654Z","end":null}""");
        row["effectivePeriod_string"].GetString().ShouldBe("""{"extension":null,"id":null,"start":"2001-02-03T03:05:06.987Z","end":null}""");
        row["component_string"].GetString().ShouldBe("""[{"valueInteger":-3,"valueDate":"2001-01-01"},{"valueInteger":null,"valueDate":null}]""");
    }

    [Theory]
    [InlineData("1.5", "1.500000000000000000")]
    [InlineData("-12.25", "-12.250000000000000000")]
    [InlineData("0.000001", "0.000001000000000000")]
    [InlineData("0.000000123", "1.23000000000E-7")]
    [InlineData("-0.000000000000000001", "-1E-18")]
    [InlineData("123456789012345678", "123456789012345678.000000000000000000")]
    public async Task GivenADecimalInAComplexColumn_WhenProjected_ThenTheCompanionUsesTheCanonicalJvmText(string value, string expected)
    {
        var projection = await CreateAsync(
            "Observation",
            Field("valueQuantity", Struct(Field("value", "decimal(36,18)"))),
            Field("valueQuantity_string", "string"));

        var row = projection.Project($$$"""{"resourceType":"Observation","id":"o1","valueQuantity":{"value":{{{value}}}}}""");

        row["valueQuantity_string"].GetString().ShouldBe($$"""{"value":{{expected}}}""");
    }

    [Fact]
    public async Task GivenAFlattenedColumn_WhenProjected_ThenItCopiesTheFinalStructField()
    {
        var projection = await CreateAsync(
            "Patient",
            Field("meta", Struct(Field("versionId", "string"), Field("lastUpdated", "timestamp"))),
            Field("meta_lastUpdated", "timestamp"),
            Field("meta_string", "string"));

        var row = projection.Project("""
            {"resourceType":"Patient","id":"p1","meta":{"versionId":"3","lastUpdated":"2023-04-05T06:07:08.123456+02:00"}}
            """);

        row["meta_lastUpdated"].GetString().ShouldBe("2023-04-05T04:07:08.123456Z");
        row["meta_string"].GetString().ShouldBe("""{"versionId":"3","lastUpdated":"2023-04-05T04:07:08.123Z"}""");
        row.MetaLastUpdated.ShouldBe(new DateTimeOffset(2023, 4, 5, 4, 7, 8, TimeSpan.Zero).AddTicks(1_234_560));
    }

    [Fact]
    public async Task GivenAFlattenedColumnOfAMissingStruct_WhenProjected_ThenItIsNull()
    {
        var projection = await CreateAsync(
            "Patient",
            Field("meta", Struct(Field("lastUpdated", "timestamp"))),
            Field("meta_lastUpdated", "timestamp"));

        var row = projection.Project("""{"resourceType":"Patient","id":"p1"}""");

        row["meta_lastUpdated"].ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task GivenAFieldNameWithAnUnderscore_WhenFlattened_ThenEveryUnderscoreIsTried()
    {
        var projection = await CreateAsync(
            "Patient",
            Field("x", Struct(Field("y_z", "string"))),
            Field("x_y_z", "string"),
            Field("q_r", Struct(Field("s", "integer"))),
            Field("q_r_s", "integer"));

        var row = projection.Project("""{"resourceType":"Patient","id":"p1","x":{"y_z":"v"},"q_r":{"s":4}}""");

        row["x_y_z"].GetString().ShouldBe("v");
        row["q_r_s"].GetInt32().ShouldBe(4);
    }

    [Fact]
    public async Task GivenAnUnderscoreColumnWithoutAMatchingStruct_WhenProjected_ThenItIsReadByName()
    {
        var projection = await CreateAsync(
            "Patient",
            Field("x", Struct(Field("y", "string"))),
            Field("x_q", "string"),
            Field("nothing_string", "string"));

        var row = projection.Project("""{"resourceType":"Patient","id":"p1","x":{"y":"v"},"x_q":"direct","nothing_string":"n"}""");

        row["x_q"].GetString().ShouldBe("direct");
        row["nothing_string"].GetString().ShouldBe("n");
    }

    [Theory]
    [InlineData("""{"name":"x"}""", "name")]
    [InlineData("""{"name":[5]}""", "name[0]")]
    [InlineData("""{"name":[null]}""", "name[0]")]
    [InlineData("""{"name":[{"given":"x"}]}""", "name[0].given")]
    [InlineData("""{"name":[{"given":[null]}]}""", "name[0].given[0]")]
    [InlineData("""{"name":[{"given":["a"],"family":"1"}]}""", "name[0].family")]
    public async Task GivenAShapeTheSchemaCannotHold_WhenProjected_ThenRejectedAtItsPath(string members, string path)
    {
        var projection = await CreateAsync(
            "Patient",
            Field("name", ArrayOf(Struct(
                Field("given", ArrayOf("string", containsNull: false)),
                Field("family", "integer")))));
        var resource = JsonNode.Parse(members)!.AsObject();
        resource["resourceType"] = "Patient";
        resource["id"] = "p1";

        projection.Rejection(resource.ToJsonString()).Path.ShouldBe(path);
    }

    [Fact]
    public async Task GivenANullItemInANullableArray_WhenProjected_ThenItIsKept()
    {
        var projection = await CreateAsync("Patient", Field("given", ArrayOf("string")), Field("ages", ArrayOf("integer")));

        var row = projection.Project("""{"resourceType":"Patient","id":"p1","given":["a",null,3],"ages":[1,null]}""");

        row["given"].ShouldBeJson("""["a",null,"3"]""");
        row["ages"].ShouldBeJson("[1,null]");
    }

    [Fact]
    public async Task GivenAMissingNonNullableColumn_WhenProjected_ThenRejectedAtTheColumn()
    {
        var projection = await CreateAsync("Patient", Field("gender", "string", nullable: false));

        projection.Rejection("""{"resourceType":"Patient","id":"p1"}""").Path.ShouldBe("gender");
        projection.Project("""{"resourceType":"Patient","id":"p1","gender":"other"}""")["gender"].GetString().ShouldBe("other");
    }

    [Fact]
    public async Task GivenAMissingNonNullableStructField_WhenProjected_ThenRejectedAtTheField()
    {
        var projection = await CreateAsync("Patient", Field("contact", Struct(Field("name", "string", nullable: false))));

        projection.Rejection("""{"resourceType":"Patient","id":"p1","contact":{}}""").Path.ShouldBe("contact.name");
        projection.Project("""{"resourceType":"Patient","id":"p1"}""")["contact"].ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task GivenANonNullableCompanionOfAMissingColumn_WhenProjected_ThenRejectedAtTheCompanion()
    {
        var projection = await CreateAsync(
            "Patient",
            Field("name", ArrayOf(Strings("text"))),
            Field("name_string", "string", nullable: false));

        projection.Rejection("""{"resourceType":"Patient","id":"p1"}""").Path.ShouldBe("name_string");
    }

    [Fact]
    public async Task GivenAMissingNonNullableReferenceField_WhenProjected_ThenRejectedAtTheNormalizedField()
    {
        var reference = Reference();
        reference["fields"]![0]!["nullable"] = false;
        var projection = await CreateAsync("Patient", Field("managingOrganization", reference));

        projection.Rejection("""{"resourceType":"Patient","id":"p1","managingOrganization":{"display":"x"}}""")
            .Path.ShouldBe("managingOrganization.reference");
    }

    private static async Task<JsonElement> Cell(string column, JsonNode type, string value)
    {
        var projection = await CreateAsync("Patient", Field(column, type));
        return projection.Project($$"""{"resourceType":"Patient","id":"p1","{{column}}":{{value}}}""")[column];
    }
}
