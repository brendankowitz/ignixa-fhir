// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Serialization;

namespace Ignixa.Search.Tests.Serialization;

public class SearchValueBinaryFormatTests
{
    [Fact]
    public void GivenEveryFieldCombinationOfEveryValueType_WhenRoundTripped_ThenNoFieldDiffers()
    {
        List<(string Name, ISearchValue Value)> values = CompactSearchValueFormatTests.AllValues();

        List<ISearchValue> reloaded = RoundTrip(values.Select(value => value.Value));

        values.Count.ShouldBeGreaterThan(150);
        for (int index = 0; index < values.Count; index++)
        {
            reloaded[index].GetType().ShouldBe(values[index].Value.GetType(), values[index].Name);
            SearchValueCanon.Describe(reloaded[index]).ShouldBe(SearchValueCanon.Describe(values[index].Value), values[index].Name);
        }
    }

    [Fact]
    public void GivenTheSameStringsWrittenTwice_WhenRead_ThenTheyAreStoredOnceAndComeBackAsOneInstance()
    {
        var first = new TokenSearchValue("http://loinc.org", "8867-4", "Heart rate");
        var second = new TokenSearchValue("http://loinc.org", "8867-4", "Heart rate");

        using var once = new MemoryStream();
        Write(once, [first]);
        using var twice = new MemoryStream();
        Write(twice, [first, second]);
        List<ISearchValue> reloaded = Read(twice.ToArray(), 2);

        (twice.Length - once.Length).ShouldBeLessThan(8);
        ((TokenSearchValue)reloaded[1]).System.ShouldBeSameAs(((TokenSearchValue)reloaded[0]).System);
    }

    [Fact]
    public void GivenAQueryOnlyValue_WhenWritten_ThenItThrows()
    {
        using var stream = new MemoryStream();
        Should.Throw<NotSupportedException>(() => Write(stream, [new OfTypeTokenSearchValue("123", "http://type", "MR")]));
    }

    [Theory]
    [InlineData(new byte[] { 99 })]
    [InlineData(new byte[] { 1, 7 })]
    [InlineData(new byte[] { 4, 1, 1, (byte)'x', 4 })]
    [InlineData(new byte[] { 5, 9, 0, 0, 1, 1, (byte)'1' })]
    [InlineData(new byte[] { 8, 0 })]
    [InlineData(new byte[] { 8, 1, 1, 8, 1, 1, 1, 0, 1, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 8, 0xFF, 0xFF, 0xFF, 0xFF, 0x07 })]
    public void GivenBytesTheWriterDidNotWrite_WhenRead_ThenTheyAreRejectedAsInvalidData(byte[] bytes)
    {
        Should.Throw<InvalidDataException>(() => Read(bytes, 1));
    }

    [Fact]
    public void GivenATruncatedStream_WhenRead_ThenItIsAnEndOfStream()
    {
        using var stream = new MemoryStream();
        Write(stream, [DateTimeSearchValue.Parse("2020-01-02T03:04:05Z")]);
        byte[] bytes = stream.ToArray();

        Should.Throw<EndOfStreamException>(() => Read(bytes[..^3], 1));
    }

    [Fact]
    public void GivenADateWhoseTextNoLongerMatchesItsRange_WhenRead_ThenItIsRejected()
    {
        using var stream = new MemoryStream();
        Write(stream, [DateTimeSearchValue.Parse("2020-01-02")]);
        byte[] bytes = stream.ToArray();
        int text = Array.LastIndexOf(bytes, (byte)'2');
        bytes[text] = (byte)'3';

        Should.Throw<InvalidDataException>(() => Read(bytes, 1));
    }

    private static List<ISearchValue> RoundTrip(IEnumerable<ISearchValue> values)
    {
        List<ISearchValue> written = [.. values];
        using var stream = new MemoryStream();
        Write(stream, written);
        return Read(stream.ToArray(), written.Count);
    }

    private static void Write(MemoryStream stream, IEnumerable<ISearchValue> values)
    {
        using var binary = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        var writer = new SearchValueBinaryWriter(binary);
        foreach (ISearchValue value in values)
        {
            writer.WriteValue(value);
        }
    }

    private static List<ISearchValue> Read(byte[] bytes, int count)
    {
        using var binary = new BinaryReader(new MemoryStream(bytes));
        var reader = new SearchValueBinaryReader(binary);
        return [.. Enumerable.Range(0, count).Select(_ => reader.ReadValue())];
    }
}
