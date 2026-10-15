// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Ignixa.CliOnFhir.Caching;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

public class CacheSearchValueConverterTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void GivenEveryFieldCombinationOfEveryValueType_WhenRoundTripped_ThenNoFieldDiffers()
    {
        List<(string Name, ISearchValue Value)> values = [.. Tokens(), .. Quantities(), .. Numbers(), .. Strings(), .. References(), .. Dates(), .. Uris(), .. Composites()];

        List<string> mismatches = [];
        foreach (var (name, value) in values)
        {
            ISearchValue reloaded = RoundTrip(value);
            string expected = SearchIndexCanon.Describe(value);
            string actual = SearchIndexCanon.Describe(reloaded);
            if (reloaded.GetType() != value.GetType() || expected != actual || !EqualsWhereDefined(value, reloaded))
            {
                mismatches.Add($"{name}\n  wrote  {expected}\n  loaded {actual}");
            }
        }

        values.Count.ShouldBeGreaterThan(150);
        mismatches.ShouldBeEmpty();
    }

    [Fact]
    public void GivenCompositeValue_WhenRoundTripped_ThenComponentStructureIsKeptWithoutCartesianExpansion()
    {
        var composite = new CompositeIndexSearchValue(
        [
            [new TokenSearchValue("http://loinc.org", "8302-2", "Body height"), new TokenSearchValue("http://snomed.info/sct", "50373000", null)],
            [new QuantitySearchValue("http://unitsofmeasure.org", "cm", 77.5m)],
        ]);

        var reloaded = (CompositeIndexSearchValue)RoundTrip(composite);

        reloaded.Components.Select(component => component.Count).ShouldBe([2, 1]);
        SearchIndexCanon.Describe(reloaded).ShouldBe(SearchIndexCanon.Describe(composite));
    }

    [Fact]
    public void GivenValueTypeTheFormatDoesNotKnow_WhenWritten_ThenItThrows()
    {
        var entries = new List<CachedEntry> { new("x", new OfTypeTokenSearchValue("123", "http://type", "MR")) };

        Should.Throw<NotSupportedException>(() => JsonSerializer.Serialize(entries, Options));
    }

    [Theory]
    [InlineData("""{"k":"mystery"}""")]
    [InlineData("""{"sys":"http://s","code":"c"}""")]
    [InlineData("""{"k":"token","bogus":1}""")]
    [InlineData("""{"k":"token"}""")]
    [InlineData("""{"k":"string"}""")]
    [InlineData("""{"k":"number"}""")]
    [InlineData("""{"k":"reference","kind":"Sideways","id":"1"}""")]
    [InlineData("""{"k":"date","start":"2020-01-01T00:00:00.0000000+00:00","end":"2020-01-02T00:00:00.0000000+00:00","text":"1999"}""")]
    [InlineData("""{"k":"composite","components":[]}""")]
    [InlineData("""{"k":"composite","components":[[{"k":"nope"}]]}""")]
    [InlineData("""{"k":"token","code":5}""")]
    public void GivenMalformedValue_WhenRead_ThenItIsRejected(string valueJson)
    {
        string json = $$"""[{"code":"x","value":{{valueJson}}}]""";

        // Whatever the failure type, FolderIndexCache classifies it as an unreadable cache file.
        Exception failure = Should.Throw<Exception>(() => JsonSerializer.Deserialize<List<CachedEntry>>(json, Options));
        failure.ShouldBeOfType<JsonException>(failure.ToString());
    }

    // The values' own Equals is only trustworthy for some: ReferenceSearchValue dereferences ResourceType, which is
    // legitimately null for urn: references, and CompositeIndexSearchValue compares its component lists by reference.
    private static bool EqualsWhereDefined(ISearchValue written, ISearchValue reloaded) =>
        written is ReferenceSearchValue { ResourceType: null } or CompositeIndexSearchValue || written.Equals(reloaded);

    private static ISearchValue RoundTrip(ISearchValue value)
    {
        string json = JsonSerializer.Serialize(new List<CachedEntry> { new("x", value) }, Options);
        return JsonSerializer.Deserialize<List<CachedEntry>>(json, Options)!.ShouldHaveSingleItem().Value;
    }

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
        foreach (string text in new[] { "Smith", "MiXed ünï \"quoted\" 中文" })
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
        string[] partialDates = ["2020", "2020-01", "2020-01-02", "2020-01-02T03:04:05+10:00", "2020-01-02T03:04:05.123Z", "2020-01-02T03:04:05-05:30", "2020-01-02T03:04-00:30", "2020-01-02T03:04:05+05:30", "2020-01-02T03:04Z"];
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
        yield return ("composite token+quantity", new CompositeIndexSearchValue([[token], [quantity]]));
        yield return ("composite multi-valued components", new CompositeIndexSearchValue([[token, textOnly], [quantity, quantity], [date]]));
        yield return ("composite string+date", new CompositeIndexSearchValue([[text], [date]]));
        yield return ("composite single component", new CompositeIndexSearchValue([[textOnly]]));
    }
}
