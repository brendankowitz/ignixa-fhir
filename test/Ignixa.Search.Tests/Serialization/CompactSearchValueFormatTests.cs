// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Serialization;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Tests.Serialization;

/// <summary>
/// Regressions for the compact search value format the FileSystem sidecars persist: at 206143a it could not read back
/// composites, text-only tokens or high-only ranges, and lost token text case, identifier types, reference kinds, date and
/// string min/max flags, canonical version and fragment, and empty-versus-absent quantity systems and codes. The
/// malformed-value rows the old reader also rejected (an unknown kind, an empty token, number or string, a token code
/// of the wrong JSON type, an empty or unknown composite member) are guards on the new reader's strictness.
/// </summary>
public class CompactSearchValueFormatTests
{
    private static readonly JsonSerializerOptions IndexOptions = new() { Converters = { new CompactSearchIndexConverter() } };

    private static readonly JsonSerializerOptions ValueOptions = new() { Converters = { new CompactSearchValueConverter() } };

    [Fact]
    public void GivenEveryFieldCombinationOfEveryValueType_WhenRoundTrippedAsAnIndex_ThenNoFieldDiffers()
    {
        List<(string Name, ISearchValue Value)> values = AllValues();

        List<string> mismatches = [];
        foreach ((string name, ISearchValue value) in values)
        {
            ISearchValue reloaded = RoundTripIndex(value);
            if (reloaded.GetType() != value.GetType() || SearchValueCanon.Describe(value) != SearchValueCanon.Describe(reloaded))
            {
                mismatches.Add($"{name}\n  wrote  {SearchValueCanon.Describe(value)}\n  loaded {SearchValueCanon.Describe(reloaded)}");
            }
        }

        values.Count.ShouldBeGreaterThan(150);
        mismatches.ShouldBeEmpty();
    }

    [Fact]
    public void GivenEveryValueType_WhenRoundTrippedAsOneValue_ThenNoFieldDiffers()
    {
        foreach ((string name, ISearchValue value) in AllValues())
        {
            ISearchValue reloaded = JsonSerializer.Deserialize<ISearchValue>(JsonSerializer.Serialize(value, ValueOptions), ValueOptions)!;

            SearchValueCanon.Describe(reloaded).ShouldBe(SearchValueCanon.Describe(value), name);
        }
    }

    [Fact]
    public void GivenACompositeValue_WhenRoundTripped_ThenItsComponentStructureIsKeptWithoutCartesianExpansion()
    {
        var composite = new CompositeIndexSearchValue(
        [
            [new TokenSearchValue("http://loinc.org", "8302-2", "Body height"), new TokenSearchValue("http://snomed.info/sct", "50373000", null)],
            [new QuantitySearchValue("http://unitsofmeasure.org", "cm", 77.5m)],
        ]);

        var reloaded = (CompositeIndexSearchValue)RoundTripIndex(composite);

        reloaded.Components.Select(component => component.Count).ShouldBe([2, 1]);
        SearchValueCanon.Describe(reloaded).ShouldBe(SearchValueCanon.Describe(composite));
    }

    [Fact]
    public void GivenSeveralParameters_WhenRoundTripped_ThenEachValueKeepsItsParameterCodeAndType()
    {
        List<SearchIndexEntry> entries =
        [
            new(new SearchParameterInfo("code", "code", SearchParamType.Token), new TokenSearchValue("http://loinc.org", "1", null)),
            new(new SearchParameterInfo("code", "code", SearchParamType.Token), new TokenSearchValue(null, null, "Only Text")),
            new(new SearchParameterInfo("code-value-quantity", "code-value-quantity", SearchParamType.Composite), new CompositeIndexSearchValue([[new TokenSearchValue(null, "1", null)], [new QuantitySearchValue(null, "mg", null, 5m)]])),
        ];

        List<SearchIndexEntry> reloaded = JsonSerializer.Deserialize<List<SearchIndexEntry>>(JsonSerializer.Serialize(entries, IndexOptions), IndexOptions)!;

        reloaded.Select(entry => (entry.SearchParameter.Code, entry.SearchParameter.Type, SearchValueCanon.Describe(entry.Value)))
            .ShouldBe(entries.Select(entry => (entry.SearchParameter.Code, entry.SearchParameter.Type, SearchValueCanon.Describe(entry.Value))));
    }

    [Fact]
    public void GivenAValueTypeTheFormatDoesNotKnow_WhenWritten_ThenItThrows()
    {
        Should.Throw<NotSupportedException>(() => JsonSerializer.Serialize<ISearchValue>(new OfTypeTokenSearchValue("123", "http://type", "MR"), ValueOptions));
    }

    [Theory]
    [InlineData("""{"k":"mystery"}""")]
    [InlineData("""{"s":"http://s","c":"c"}""")]
    [InlineData("""{"n_s":"SMITH"}""")]
    [InlineData("""{"k":"token","c":"x","bogus":1}""")]
    [InlineData("""{"k":"token"}""")]
    [InlineData("""{"k":"string"}""")]
    [InlineData("""{"k":"number"}""")]
    [InlineData("""{"k":"reference","rk":"Sideways","ri":"1"}""")]
    [InlineData("""{"k":"date","st":"2020-01-01T00:00:00.0000000+00:00","et":"2020-01-02T00:00:00.0000000+00:00","dt":"1999"}""")]
    [InlineData("""{"k":"composite","cs":[]}""")]
    [InlineData("""{"k":"composite","cs":[[{"k":"nope"}]]}""")]
    [InlineData("""{"k":"token","c":5}""")]
    public void GivenAMalformedOrOlderFormatValue_WhenRead_ThenItIsRejectedAsJson(string valueJson)
    {
        string json = $$"""{"x":[{{valueJson}}]}""";

        Exception failure = Should.Throw<Exception>(() => JsonSerializer.Deserialize<List<SearchIndexEntry>>(json, IndexOptions));
        failure.ShouldBeOfType<JsonException>(failure.ToString());
    }

    private static ISearchValue RoundTripIndex(ISearchValue value)
    {
        List<SearchIndexEntry> entries = [new(new SearchParameterInfo("x", "x", SearchParamType.Token), value)];
        string json = JsonSerializer.Serialize(entries, IndexOptions);
        return JsonSerializer.Deserialize<List<SearchIndexEntry>>(json, IndexOptions)!.ShouldHaveSingleItem().Value;
    }

    private static List<(string Name, ISearchValue Value)> AllValues() =>
        [.. Tokens(), .. Quantities(), .. Numbers(), .. Strings(), .. References(), .. Dates(), .. Uris(), .. Composites()];

    private static IEnumerable<(string, ISearchValue)> Tokens()
    {
        string?[] systems = [null, string.Empty, "http://s"];
        string?[] codes = [null, string.Empty, "AbC"];
        string?[] texts = [null, "Mixed Case Text"];
        (string?, string?)[] identifierTypes = [(null, null), ("http://type", "MR"), ("http://type", null), (null, "MR")];
        foreach (string? system in systems)
        {
            foreach (string? code in codes)
            {
                foreach (string? text in texts)
                {
                    foreach ((string? typeSystem, string? typeCode) in identifierTypes)
                    {
                        if (string.IsNullOrWhiteSpace(system) && string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(text))
                        {
                            continue;
                        }

                        yield return ($"token system={system} code={code} text={text} type={typeSystem}|{typeCode}", new TokenSearchValue(system, code, text, typeSystem, typeCode));
                    }
                }
            }
        }
    }

    private static IEnumerable<(string, ISearchValue)> Quantities()
    {
        string?[] systems = [null, string.Empty, "http://unitsofmeasure.org"];
        string?[] codes = [null, string.Empty, "mg"];
        (decimal?, decimal?)[] ranges = [(1.50m, 1.50m), (1m, 2m), (1m, null), (null, 2m), (77.55994173028746784m, 77.55994173028746784m), (-0.0001m, 1000000m)];
        foreach (string? system in systems)
        {
            foreach (string? code in codes)
            {
                foreach ((decimal? low, decimal? high) in ranges)
                {
                    yield return ($"quantity system={system} code={code} {low}..{high}", new QuantitySearchValue(system, code, low, high));
                }
            }
        }
    }

    private static IEnumerable<(string, ISearchValue)> Numbers()
    {
        (decimal?, decimal?)[] ranges = [(2.50m, 2.50m), (0m, 0m), (1m, 2m), (1m, null), (null, 2m), (-3.125m, 99999999999.99999m)];
        foreach ((decimal? low, decimal? high) in ranges)
        {
            yield return ($"number {low}..{high}", new NumberSearchValue(low, high));
        }
    }

    private static IEnumerable<(string, ISearchValue)> Strings()
    {
        foreach (string text in new[] { "Smith", "MiXed ünï \"quoted\" 中文", @"a\b", @"a\\b", "a,b", @"a\,b", "x$y", @"x\$y", "p|q", @"p\|q", @"\", @"trailing\", @"\\", @"\|\,\$", "a, b | c $ d", @"\\\\", @"\\\|" })
        {
            foreach (bool isMin in new[] { false, true })
            {
                foreach (bool isMax in new[] { false, true })
                {
                    yield return ($"string {text} min={isMin} max={isMax}", new StringSearchValue(text) { IsMin = isMin, IsMax = isMax });
                }
            }
        }
    }

    private static IEnumerable<(string, ISearchValue)> References()
    {
        foreach (ReferenceKind kind in new[] { ReferenceKind.InternalOrExternal, ReferenceKind.Internal })
        {
            yield return ($"reference {kind} typed", new ReferenceSearchValue(kind, null!, "Patient", "p1"));
            yield return ($"reference {kind} untyped", new ReferenceSearchValue(kind, null!, null!, "urn:uuid:0f3c"));
        }

        yield return ("reference External", new ReferenceSearchValue(ReferenceKind.External, new Uri("http://other.example/fhir/"), "Patient", "p1"));
        yield return ("reference InternalOrExternal with base", new ReferenceSearchValue(ReferenceKind.InternalOrExternal, new Uri("http://other.example/fhir/"), "Patient", "p1"));
    }

    private static IEnumerable<(string, ISearchValue)> Dates()
    {
        string[] partialDates = ["2020", "2020-01", "2020-01-02", "2020-01-02T03:04:05+10:00", "2020-01-02T03:04:05.123Z", "2020-01-02T03:04:05-05:30", "2020-01-02T03:04-00:30", "2020-01-02T03:04:05+05:30", "2020-01-02T03:04Z", "0042-03-04", "0999"];
        foreach (string text in partialDates)
        {
            foreach (bool isMin in new[] { false, true })
            {
                foreach (bool isMax in new[] { false, true })
                {
                    yield return ($"date {text} min={isMin} max={isMax}", new DateTimeSearchValue(PartialDateTime.Parse(text)) { IsMin = isMin, IsMax = isMax });
                }
            }
        }

        yield return ("date range", new DateTimeSearchValue(PartialDateTime.Parse("2020-01"), PartialDateTime.Parse("2020-03")));
        yield return ("date instant", new DateTimeSearchValue(new DateTimeOffset(2020, 5, 6, 7, 8, 9, TimeSpan.FromHours(2))));
        yield return ("date open-ended end", new DateTimeSearchValue(PartialDateTime.Parse("2020-01-01"), PartialDateTime.Parse("9999-12-31")) { IsMax = true });
    }

    private static IEnumerable<(string, ISearchValue)> Uris()
    {
        yield return ("uri plain", new UriSearchValue("http://example.org/ValueSet/x", separateCanonicalComponents: false));
        yield return ("uri with unsplit canonical suffix", new UriSearchValue("http://example.org/ValueSet/x|1.0#frag", separateCanonicalComponents: false));
        yield return ("uri version and fragment", new UriSearchValue("http://example.org/ValueSet/x|1.0#frag", separateCanonicalComponents: true));
        yield return ("uri version only", new UriSearchValue("http://example.org/ValueSet/x|1.0", separateCanonicalComponents: true));
        yield return ("uri fragment only", new UriSearchValue("http://example.org/ValueSet/x#frag", separateCanonicalComponents: true));
    }

    private static IEnumerable<(string, ISearchValue)> Composites()
    {
        var token = new TokenSearchValue("http://loinc.org", "8302-2", "Body height");
        var textOnly = new TokenSearchValue(null, null, "Only Text");
        var quantity = new QuantitySearchValue("http://unitsofmeasure.org", "cm", 77.5m);
        var date = new DateTimeSearchValue(PartialDateTime.Parse("2020-01-02"));
        var text = new StringSearchValue("Abc") { IsMin = true };
        var escaped = new StringSearchValue(@"a\\\,b") { IsMax = true };
        yield return ("composite token+quantity", new CompositeIndexSearchValue([[token], [quantity]]));
        yield return ("composite multi-valued components", new CompositeIndexSearchValue([[token, textOnly], [quantity, quantity], [date]]));
        yield return ("composite string+date", new CompositeIndexSearchValue([[text], [date]]));
        yield return ("composite with escaped string members", new CompositeIndexSearchValue([[escaped, text], [date]]));
        yield return ("composite single component", new CompositeIndexSearchValue([[textOnly]]));
    }
}
