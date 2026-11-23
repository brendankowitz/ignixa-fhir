// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Buffers;
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

    [Theory]
    [MemberData(nameof(EdgeStrings))]
    public void GivenTextSplitAcrossBlocksAndSmallDestinations_WhenEncodingUtf16_ThenTheOutputMatchesNewtonsoftDefault(string value)
    {
        string expected = Newtonsoft.Json.JsonConvert.ToString(value)[1..^1];

        foreach ((int blockSize, int destinationSize) in BlockAndDestinationSizes)
        {
            EncodeUtf16InBlocks(value, blockSize, destinationSize).ShouldBe(expected);
        }
    }

    [Theory]
    [MemberData(nameof(EdgeStrings))]
    public void GivenTextSplitAcrossBlocksAndSmallDestinations_WhenEncodingUtf8_ThenTheOutputMatchesNewtonsoftDefault(string value)
    {
        string expected = Newtonsoft.Json.JsonConvert.ToString(value)[1..^1];

        foreach ((int blockSize, int destinationSize) in BlockAndDestinationSizes)
        {
            EncodeUtf8InBlocks(Encoding.UTF8.GetBytes(value), blockSize, destinationSize).ShouldBe(expected);
        }
    }

    [Fact]
    public void GivenADestinationEndingInsideASurrogatePair_WhenEncodingUtf16_ThenThePairIsLeftForTheNextCall()
    {
        char[] destination = new char[3];

        OperationStatus status = MinimalJsonEncoder.Instance.Encode("ab\ud83d\ude00c", destination, out int consumed, out int written);

        status.ShouldBe(OperationStatus.DestinationTooSmall);
        consumed.ShouldBe(2);
        new string(destination, 0, written).ShouldBe("ab");
    }

    [Fact]
    public void GivenADestinationEndingInsideAMultiByteSequence_WhenEncodingUtf8_ThenTheSequenceIsLeftForTheNextCall()
    {
        byte[] destination = new byte[4];

        OperationStatus status = MinimalJsonEncoder.Instance.EncodeUtf8(Encoding.UTF8.GetBytes("ab\u65e5c"), destination, out int consumed, out int written);

        status.ShouldBe(OperationStatus.DestinationTooSmall);
        consumed.ShouldBe(2);
        Encoding.UTF8.GetString(destination, 0, written).ShouldBe("ab");
    }

    [Fact]
    public void GivenADestinationTooSmallForAnEscape_WhenEncoding_ThenTheEscapedCharacterIsNotConsumed()
    {
        char[] chars = new char[6];
        byte[] bytes = new byte[6];

        OperationStatus utf16 = MinimalJsonEncoder.Instance.Encode("ab\u0001", chars, out int charsConsumed, out int charsWritten);
        OperationStatus utf8 = MinimalJsonEncoder.Instance.EncodeUtf8("ab\u0001"u8, bytes, out int bytesConsumed, out int bytesWritten);

        utf16.ShouldBe(OperationStatus.DestinationTooSmall);
        (charsConsumed, charsWritten).ShouldBe((2, 2));
        utf8.ShouldBe(OperationStatus.DestinationTooSmall);
        (bytesConsumed, bytesWritten).ShouldBe((2, 2));
    }

    [Theory]
    [InlineData(false, OperationStatus.NeedMoreData, 1, "a")]
    [InlineData(true, OperationStatus.Done, 2, "a\ufffd")]
    public void GivenAHighSurrogateAtTheEndOfABlock_WhenEncodingUtf16_ThenOnlyAFinalBlockReplacesIt(
        bool isFinalBlock, OperationStatus expectedStatus, int expectedConsumed, string expected)
    {
        char[] destination = new char[16];

        OperationStatus status = MinimalJsonEncoder.Instance.Encode("a\ud83d", destination, out int consumed, out int written, isFinalBlock);

        status.ShouldBe(expectedStatus);
        consumed.ShouldBe(expectedConsumed);
        new string(destination, 0, written).ShouldBe(expected);
    }

    [Theory]
    [InlineData(false, OperationStatus.NeedMoreData, 1, "a")]
    [InlineData(true, OperationStatus.Done, 4, "a\ufffd")]
    public void GivenATruncatedSequenceAtTheEndOfABlock_WhenEncodingUtf8_ThenOnlyAFinalBlockReplacesIt(
        bool isFinalBlock, OperationStatus expectedStatus, int expectedConsumed, string expected)
    {
        byte[] source = Encoding.UTF8.GetBytes("a\ud83d\ude00")[..^1];
        byte[] destination = new byte[16];

        OperationStatus status = MinimalJsonEncoder.Instance.EncodeUtf8(source, destination, out int consumed, out int written, isFinalBlock);

        status.ShouldBe(expectedStatus);
        consumed.ShouldBe(expectedConsumed);
        Encoding.UTF8.GetString(destination, 0, written).ShouldBe(expected);
    }

    [Fact]
    public void GivenIllFormedBytesInsideLongRuns_WhenEncodingUtf8_ThenEachIsReplacedAndTheRestIsKept()
    {
        string run = new('x', 5_000);
        byte[] source = [.. Encoding.UTF8.GetBytes(run), 0xFF, .. Encoding.UTF8.GetBytes(run + "\u00e9\""), 0xE3, 0x81, .. "z"u8];
        byte[] destination = new byte[source.Length * 6];

        OperationStatus status = MinimalJsonEncoder.Instance.EncodeUtf8(source, destination, out int consumed, out int written);

        status.ShouldBe(OperationStatus.Done);
        consumed.ShouldBe(source.Length);
        Encoding.UTF8.GetString(destination, 0, written).ShouldBe(run + "\ufffd" + run + "\u00e9\\\"\ufffdz");
    }

    [Fact]
    public void GivenLoneSurrogatesAmongEscapes_WhenEncodingUtf16_ThenEachIsReplacedAndTheRestIsKept()
    {
        char[] destination = new char[64];

        OperationStatus status = MinimalJsonEncoder.Instance.Encode("a\"\udc00b\ud83d\ude00\ud800\nc", destination, out int consumed, out int written);

        status.ShouldBe(OperationStatus.Done);
        consumed.ShouldBe(9);
        new string(destination, 0, written).ShouldBe("a\\\"\ufffdb\ud83d\ude00\ufffd\\nc");
    }

    private static readonly (int BlockSize, int DestinationSize)[] BlockAndDestinationSizes =
        [(1, 6), (2, 7), (3, 6), (7, 11), (64, 6), (4096, 4096)];

    // Feeds the encoder blockSize units at a time, final only on the last block, and drains a
    // destination of destinationSize whenever it fills, as a streaming caller would.
    private static string EncodeUtf16InBlocks(string value, int blockSize, int destinationSize)
    {
        var output = new StringBuilder();
        char[] destination = new char[destinationSize];
        string pending = string.Empty;
        int offset = 0;
        bool isFinalBlock;
        do
        {
            int take = Math.Min(blockSize, value.Length - offset);
            ReadOnlySpan<char> source = string.Concat(pending, value.AsSpan(offset, take));
            offset += take;
            isFinalBlock = offset == value.Length;
            OperationStatus status;
            do
            {
                status = MinimalJsonEncoder.Instance.Encode(source, destination, out int consumed, out int written, isFinalBlock);
                if (status == OperationStatus.DestinationTooSmall)
                {
                    (consumed + written).ShouldBeGreaterThan(0);
                }

                output.Append(destination, 0, written);
                source = source[consumed..];
            }
            while (status == OperationStatus.DestinationTooSmall);

            status.ShouldBe(source.IsEmpty ? OperationStatus.Done : OperationStatus.NeedMoreData);
            pending = source.ToString();
        }
        while (!isFinalBlock);

        pending.ShouldBeEmpty();
        return output.ToString();
    }

    private static string EncodeUtf8InBlocks(byte[] value, int blockSize, int destinationSize)
    {
        using var output = new MemoryStream();
        byte[] destination = new byte[destinationSize];
        byte[] pending = [];
        int offset = 0;
        bool isFinalBlock;
        do
        {
            int take = Math.Min(blockSize, value.Length - offset);
            byte[] block = [.. pending, .. value.AsSpan(offset, take)];
            ReadOnlySpan<byte> source = block;
            offset += take;
            isFinalBlock = offset == value.Length;
            OperationStatus status;
            do
            {
                status = MinimalJsonEncoder.Instance.EncodeUtf8(source, destination, out int consumed, out int written, isFinalBlock);
                if (status == OperationStatus.DestinationTooSmall)
                {
                    (consumed + written).ShouldBeGreaterThan(0);
                }

                output.Write(destination, 0, written);
                source = source[consumed..];
            }
            while (status == OperationStatus.DestinationTooSmall);

            status.ShouldBe(source.IsEmpty ? OperationStatus.Done : OperationStatus.NeedMoreData);
            pending = source.ToArray();
        }
        while (!isFinalBlock);

        pending.ShouldBeEmpty();
        return Encoding.UTF8.GetString(output.ToArray());
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