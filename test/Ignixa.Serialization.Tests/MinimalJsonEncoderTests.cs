// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Serialization.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Ignixa.Serialization.Tests;

public class MinimalJsonEncoderTests
{
    private static readonly JsonWriterOptions MinimalOptions = new() { Encoder = MinimalJsonEncoder.Instance };

    public static TheoryData<string> EdgeStrings => new()
    {
        "\t",
        "\n\r\b\f",
        "\u0000\u0001\u000b\u000e\u001a\u001f",
        "\u0085",
        "\u2028\u2029",
        "\u007f",
        "\u00a0",
        "é",
        "😀",
        "+",
        "<div>&amp;</div>",
        "'",
        "\"",
        "\\",
        "/",
        "ab\"c\\d\u0001é😀\u2028+<>&",
        new string(Enumerable.Range(0, 128).Select(i => (char)i).ToArray()),
        "日本語テキスト\ud83d\udc68\u200d\ud83d\udc69\u200d\ud83d\udc67",
        LongMixedText(),
    };

    public static TheoryData<string> Serializers => SerializerOverloads.All;

    [Theory]
    [MemberData(nameof(EdgeStrings))]
    public void GivenAStringValue_WhenSerializingWithTheMinimalEncoder_ThenEscapingMatchesNewtonsoftDefault(string value)
    {
        var resource = new ResourceJsonNode(new JsonObject { ["resourceType"] = "Basic", ["id"] = value });

        string json = resource.SerializeToString(MinimalOptions);

        json.ShouldBe("{\"resourceType\":\"Basic\",\"id\":" + Newtonsoft.Json.JsonConvert.ToString(value) + "}");
    }

    [Theory]
    [MemberData(nameof(EdgeStrings))]
    public void GivenAParsedStringValue_WhenSerializingWithTheMinimalEncoder_ThenEscapingMatchesNewtonsoftDefault(string value)
    {
        string escaped = Newtonsoft.Json.JsonConvert.ToString(value);
        var resource = JsonSourceNodeFactory.Parse<ResourceJsonNode>("{\"resourceType\":\"Basic\",\"id\":" + JsonSerializer.Serialize(value) + "}");

        resource.SerializeToString(MinimalOptions).ShouldBe("{\"resourceType\":\"Basic\",\"id\":" + escaped + "}");
    }

    [Fact]
    public void GivenAPropertyNameNeedingEscaping_WhenSerializingWithTheMinimalEncoder_ThenItIsEscapedMinimally()
    {
        var resource = new ResourceJsonNode(new JsonObject { ["resourceType"] = "Basic", ["é\"+"] = "x" });

        resource.SerializeToString(MinimalOptions).ShouldBe("{\"resourceType\":\"Basic\",\"é\\\"+\":\"x\"}");
    }

    [Fact]
    public void GivenALastUpdatedWithAnOffset_WhenSerializingWithTheMinimalEncoder_ThenThePlusSignIsLiteral()
    {
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(
            """{"resourceType":"Patient","meta":{"lastUpdated":"2020-01-01T00:00:00.100+00:00"},"text":{"status":"generated","div":"<div>\"&amp;é</div>"}}""");

        patient.SerializeToString(MinimalOptions).ShouldBe(
            """{"resourceType":"Patient","meta":{"lastUpdated":"2020-01-01T00:00:00.100+00:00"},"text":{"status":"generated","div":"<div>\"&amp;é</div>"}}""");
    }

    [Theory]
    [MemberData(nameof(Serializers))]
    public async Task GivenNoEscapingChoice_WhenSerializing_ThenMinimalEscapingIsTheDefault(string serializer)
    {
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(
            """{"resourceType":"Patient","meta":{"lastUpdated":"2020-01-01T00:00:00.100+00:00"},"text":{"div":"<div>é\u00a0😀</div>"}}""");
        const string expected =
            "{\"resourceType\":\"Patient\",\"meta\":{\"lastUpdated\":\"2020-01-01T00:00:00.100+00:00\"},\"text\":{\"div\":\"<div>é\u00a0😀</div>\"}}";

        (await SerializerOverloads.SerializeAsync(patient, serializer)).ShouldBe(expected);
    }

    [Fact]
    public void GivenALoneSurrogate_WhenSerializingWithTheMinimalEncoder_ThenItIsReplacedWithTheReplacementCharacter()
    {
        var resource = new ResourceJsonNode(new JsonObject { ["resourceType"] = "Basic", ["id"] = "a\ud800", ["language"] = "\udc00b" });

        resource.SerializeToString().ShouldBe("{\"resourceType\":\"Basic\",\"id\":\"a\ufffd\",\"language\":\"\ufffdb\"}");
    }

    [Fact]
    public void GivenAnUndefinedEscaping_WhenSerializing_ThenItThrows()
    {
        var resource = new ResourceJsonNode(new JsonObject { ["resourceType"] = "Basic" });

        Should.Throw<ArgumentOutOfRangeException>(() => resource.SerializeToString((JsonEscaping)2));
    }

    [Fact]
    public async Task GivenHtmlSafeEscaping_WhenSerializingThroughEachOverload_ThenHtmlSensitiveCharactersAreEscaped()
    {
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(
            """{"resourceType":"Patient","meta":{"lastUpdated":"2020-01-01T00:00:00.100+00:00"},"text":{"div":"<div>&amp;é</div>"}}""");
        const string expected =
            """{"resourceType":"Patient","meta":{"lastUpdated":"2020-01-01T00:00:00.100\u002B00:00"},"text":{"div":"\u003Cdiv\u003E\u0026amp;\u00E9\u003C/div\u003E"}}""";
        using var asyncStream = new MemoryStream();

        string text = patient.SerializeToString(escaping: JsonEscaping.HtmlSafe);
        string bytes = Encoding.UTF8.GetString(patient.SerializeToBytes(escaping: JsonEscaping.HtmlSafe).Span);
        string stream = SerializeToStreamSync(patient, JsonEscaping.HtmlSafe);
        await patient.SerializeToStreamAsync(asyncStream, escaping: JsonEscaping.HtmlSafe);

        text.ShouldBe(expected);
        bytes.ShouldBe(expected);
        stream.ShouldBe(expected);
        Encoding.UTF8.GetString(asyncStream.ToArray()).ShouldBe(expected);
    }

    [Fact]
    public async Task GivenWriterOptions_WhenSerializingThroughEachOverload_ThenAllProduceTheSameOutput()
    {
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(
            """{"resourceType":"Patient","id":"a","name":[{"family":"Zoë <b>","given":["\u00a0😀+"]}]}""");
        var indented = MinimalOptions with { Indented = true };
        string expected = patient.SerializeToString(indented);
        using var asyncStream = new MemoryStream();

        string bytes = Encoding.UTF8.GetString(patient.SerializeToBytes(indented).Span);
        string stream = SerializeToStreamSync(patient, indented);
        await patient.SerializeToStreamAsync(asyncStream, indented);

        expected.ShouldContain("Zoë <b>");
        expected.ShouldContain("\n");
        bytes.ShouldBe(expected);
        stream.ShouldBe(expected);
        Encoding.UTF8.GetString(asyncStream.ToArray()).ShouldBe(expected);
    }

    private static string SerializeToStreamSync(ResourceJsonNode resource, JsonWriterOptions options)
    {
        using var stream = new MemoryStream();
        resource.SerializeToStream(stream, options);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string SerializeToStreamSync(ResourceJsonNode resource, JsonEscaping escaping)
    {
        using var stream = new MemoryStream();
        resource.SerializeToStream(stream, escaping: escaping);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string LongMixedText()
    {
        string[] pieces = ["plain ascii text ", "\"", "\\", "\n", "\t", "\u0001", "é", "😀", "\u2028", "\u0085", "<b>", "+", "日本", "\u00a0", "\u007f"];
        var builder = new StringBuilder();
        for (int i = 0; builder.Length < 200_000; i++)
        {
            builder.Append(pieces[(i * 7 + (i / pieces.Length)) % pieces.Length]);
        }

        return builder.ToString();
    }
}