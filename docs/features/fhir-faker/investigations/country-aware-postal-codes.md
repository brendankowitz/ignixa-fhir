# Investigation: Country-Aware Postal Code Sampling

**Feature**: fhir-faker
**Status**: Implemented
**Created**: 2026-07-04
**Implemented**: 2026-07-04 (PR #300)

## Approach

Replace `DemographicsDataProvider.SampleZipCode`'s single hardcoded numeric-suffix format with a
small, data-driven `PostalCodeFormat` enum carried on `CityDemographics` itself, and a `switch` in
`SampleZipCode` over that enum instead of one-size-fits-all string concatenation.

### The bug is already shipping, not just a future UK problem

This surfaced while investigating [uk-core-patient-profile](uk-core-patient-profile.md), but it isn't
UK-specific — it's already producing wrong-shaped postal codes for the two international cities that
exist today:

```csharp
// DemographicsDataProvider.cs — current implementation
public string SampleZipCode(CityDemographics city, Bogus.Randomizer randomizer)
{
    var suffix = randomizer.Int(0, 99).ToString("D2");
    return city.ZipCodePrefix + suffix;
}
```

| City | `ZipCodePrefix` | Current output | Real format |
|---|---|---|---|
| Boston (US) | `"021"` | `"02105"` — correct | 5-digit ZIP |
| Melbourne (AU) | `"3000"` | `"300047"` — **6 digits, invalid** | 4-digit postcode, no suffix |
| Sydney (AU) | `"2000"` | `"200012"` — **6 digits, invalid** | 4-digit postcode, no suffix |
| Amsterdam (NL) | `"1011"` | `"101123"` — **6 digits, wrong shape entirely** | 4 digits + space + 2 letters (e.g. `"1011 AB"`) |
| London (UK, proposed) | `"SW1A"` | `"SW1A05"` — **wrong shape entirely** | outward code + space + digit + 2 letters (e.g. `"SW1A 1AA"`) |

Both call sites are affected equally: `PatientBuilder.FromCity` (`PatientBuilder.cs:726`) and
`OrganizationState.GenerateAddress` (`OrganizationState.cs:349`) both call `SampleZipCode`
unconditionally with no country branch today.

**Related, separately-discovered bug (out of scope here, noting for visibility):**
`OrganizationState.GenerateAddress` also hardcodes `Country: "USA"` (`OrganizationState.cs:362`)
regardless of which city was actually sampled — so an organization generated from Melbourne gets
`"USA"` as its country. Same root cause (country-specific address shaping wasn't threaded through
when international cities were added) but a distinct fix; flagging for a follow-up, not folding into
this investigation.

## Options Considered

### Option 1 — Inline switch on `city.Country` inside `DemographicsDataProvider`

Add private per-country helpers (`SampleUsZipCode`, `SampleAuPostcode`, `SampleNlPostcode`,
`SampleUkPostcode`); `SampleZipCode` becomes a switch expression on `city.Country`.

- **Pros**: smallest diff; matches the existing "small dictionary/switch keyed by country" style already
  used by `DefaultNameGenerationStrategy.CountryToLocaleMapping` and
  `AUBasePatientProfile.IndigenousStatusDisplay`.
- **Cons**: format is keyed by country code as a string, duplicating a piece of knowledge that also
  lives in `PatientProfileFactory`'s country-keyed registry, but in a second, unsynchronized place —
  a consumer registering a new country profile via `PatientProfileFactory.RegisterProfile` gets no
  signal that they also need to add a case here. Silent fallthrough to the wrong format for any new
  country.

### Option 2 — New `IPostalCodeFormatStrategy` + registry, parallel to `PatientProfileFactory`

A new interface (`string Sample(string prefix, Bogus.Randomizer randomizer)`), a
`PostalCodeFormatFactory` with `RegisterFormat(countryCode, strategy)`, mirroring the existing
profile-registration extensibility story exactly.

- **Pros**: fully consistent, pluggable extensibility across all three country-varying axes (profile
  extensions, name locale, postal format) — a consumer adding a new country registers all three the
  same way, discoverable by pattern-matching what US/AU/UK already did.
- **Cons**: a fourth small interface/factory pair for what is fundamentally a handful of string-format
  branches. `SampleAge`, `SampleGender`, and `SampleAreaCode` — the other three demographic sampling
  methods on this same class — are plain code today, not strategy objects, because they don't need
  country-specific *behavior*, just country-specific *data* (a distribution, a ratio, a list). Postal
  code shape is the same kind of thing: data-shaped, not behavior-shaped. Promoting it to a strategy
  interface would be inconsistent with its three neighbors on the same class.

### Option 3 — `PostalCodeFormat` enum as a `CityDemographics` property (recommended)

Add an enum and a property on the record itself, declared explicitly per city at construction time —
the same way `AreaCodes` and `ZipCodePrefix` already are:

```csharp
public enum PostalCodeFormat
{
    /// <summary>US-style: append a 2-digit numeric suffix (e.g. "021" -> "02105"). Default — preserves current behavior for existing cities.</summary>
    NumericSuffix,

    /// <summary>Fixed code, no suffix (e.g. Australian postcodes: "3000").</summary>
    FixedNumeric,

    /// <summary>Dutch-style: 4 digits + space + 2 letters (e.g. "1011 AB").</summary>
    DutchAlphaNumeric,

    /// <summary>UK-style: alpha-numeric outward code + space + digit + 2 letters (e.g. "SW1A 1AA").</summary>
    UkAlphaNumeric
}
```

```csharp
// CityDemographics.cs — new optional trailing parameter, non-breaking for existing named-argument call sites
public record CityDemographics(
    ...
    IReadOnlyDictionary<string, object>? Attributes = null,
    PostalCodeFormat PostalCodeFormat = PostalCodeFormat.NumericSuffix
)
```

```csharp
// DemographicsDataProvider.cs
public string SampleZipCode(CityDemographics city, Bogus.Randomizer randomizer)
{
    ArgumentNullException.ThrowIfNull(randomizer);

    return city.PostalCodeFormat switch
    {
        PostalCodeFormat.FixedNumeric => city.ZipCodePrefix,
        PostalCodeFormat.DutchAlphaNumeric => $"{city.ZipCodePrefix} {SampleLetters(randomizer, 2)}",
        PostalCodeFormat.UkAlphaNumeric => $"{city.ZipCodePrefix} {randomizer.Int(0, 9)}{SampleLetters(randomizer, 2)}",
        _ => city.ZipCodePrefix + randomizer.Int(0, 99).ToString("D2"),
    };
}

private static string SampleLetters(Bogus.Randomizer randomizer, int count) =>
    new(Enumerable.Range(0, count).Select(_ => (char)randomizer.Int('A', 'Z')).ToArray());
```

- **Pros**: fixes the already-shipping AU/NL bug with the same change that unblocks UK; format is
  explicit, visible, and co-located with the rest of a city's declared shape (`ZipCodePrefix`,
  `AreaCodes`) rather than inferred from `Country` in a second lookup table — matches "make invalid
  state unrepresentable" (the format is part of the data, not derived/guessed); default parameter value
  (`NumericSuffix`) means every existing `AddCity(...)` call in `DemographicsDataProvider.CreateDefault`
  keeps compiling and behaving identically with zero changes required to the 11 existing US cities.
- **Cons**: same as Option 1, a genuinely new format still means editing the switch in
  `DemographicsDataProvider` (not pluggable without a code change) — but this is consistent with how
  `SampleAge`'s age-bucket logic and `SampleGender`'s ratio logic already work on this same class; none
  of the three sibling sampling methods are externally pluggable today either.

## Tradeoffs

| Pros | Cons |
|------|------|
| Fixes a real, already-shipping bug (Melbourne/Sydney/Amsterdam postcodes are wrong shape today), not just a hypothetical UK blocker | UK postcode generation only approximates shape (outward-code + digit + 2 letters); it does not enforce Royal Mail's letter-exclusion rules per position (e.g. certain letters never appear in the first position of certain outward codes) — acceptable for test-data purposes, worth stating explicitly so nobody assumes real-world postcode validity |
| `PostalCodeFormat` as city-level data keeps the fix consistent with `SampleAge`/`SampleGender`/`SampleAreaCode`, which are also plain data-driven, not strategy objects | A truly new/exotic format (e.g. Canadian `A1A 1A1`, Japanese 3+4 digit) still requires a code change to `DemographicsDataProvider`, same as today |
| Default enum value preserves current behavior for all 11 existing US cities with zero call-site changes | Doesn't fix the separately-discovered `Country: "USA"` hardcoding bug in `OrganizationState` — needs its own fix |

## Alignment

- [x] Follows architectural layering rules — stays entirely inside `Ignixa.FhirFakes` (Core), no new external dependency
- [x] Developer Experience (works with minimal setup) — existing `KnownCities.*` call sites need no changes; new cities just declare their format like they already declare `AreaCodes`
- [x] Specification compliance (if applicable) — postal code *shape* only; not attempting full national postal-authority validation rules (documented above as an explicit non-goal)
- [x] Consistent with existing patterns — matches the plain-data style of `SampleAge`/`SampleGender`/`SampleAreaCode` on `DemographicsDataProvider`, and the "default parameter preserves compatibility" convention already used for `CityDemographics.Attributes`

## Evidence

- Confirmed via direct read of `DemographicsDataProvider.cs:466-472` that `SampleZipCode` has no
  country branch and unconditionally appends a `D2` numeric suffix.
- Confirmed both call sites (`PatientBuilder.cs:726`, `OrganizationState.cs:349`) invoke
  `SampleZipCode` the same unconditional way — the fix needs to live in `DemographicsDataProvider`,
  not in either caller.
- Manually traced today's output for Melbourne (`"3000"` + 2-digit suffix → 6-digit string) and
  Amsterdam (`"1011"` + 2-digit suffix → 6-digit numeric string, when a real Dutch postcode is 4
  digits + space + 2 letters) against the actual `CreateDefault()` data
  (`DemographicsDataProvider.cs:339-394`) — confirms this is a live bug in cities already shipping
  today, not a hypothetical.
- Read `OrganizationState.cs:346-364` — confirms the `Country: "USA"` hardcoding as a related but
  distinct defect, noted for follow-up rather than folded into this fix.
- Cross-referenced against `CityDemographics.cs` record shape — adding a trailing optional parameter
  with a default is the same non-breaking-change pattern already used when `Attributes` was added
  (`Attributes: IReadOnlyDictionary<string, object>? = null`).

## Verdict

**Implemented: Option 3** (`PostalCodeFormat` enum on `CityDemographics`). Shipped in PR #300: fixes
the previously-shipping AU/NL bug, unblocks UK cities (London), and stays consistent with how its three
sibling sampling methods on `DemographicsDataProvider` already work — data on the record, not a new
strategy/registry layer. The separately-discovered `OrganizationState` country-hardcoding bug was fixed
in the same change (`Country: city.Country` instead of a hardcoded `"USA"` literal).

Post-merge review hardened the implementation further: `SampleZipCode`'s switch now has an explicit
`NumericSuffix` arm plus a throwing default arm (instead of a catch-all `_ =>` that would have silently
produced US-shaped output for any future `PostalCodeFormat` value with no compiler warning), and
`PostalCodeFormat.UkAlphaNumeric` was renamed to `UKAlphaNumeric` for consistency with
`UKCorePatientProfile`'s capitalization (two-letter acronyms stay fully capitalized per .NET naming
guidelines).
