# Country-Aware Postal Code Format Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix `DemographicsDataProvider.SampleZipCode` so it produces correctly-shaped postal codes per
country instead of always appending a US-style 2-digit numeric suffix, and fix
`OrganizationState.GenerateAddress` hardcoding `Country: "USA"` regardless of which city was actually
sampled.

**Architecture:** Add a `PostalCodeFormat` enum as a new optional trailing property on the
`CityDemographics` record (default `NumericSuffix`, preserving current behavior for all 11 existing US
cities with zero call-site changes). `DemographicsDataProvider.SampleZipCode` switches on
`city.PostalCodeFormat` instead of always doing US-shaped concatenation. Existing AU/NL cities get
tagged with their correct format as part of this fix (this is not cosmetic — Melbourne, Sydney, and
Amsterdam produce invalid postal codes today). `OrganizationState.GenerateAddress` starts reading
`city.Country` (already present on every `CityDemographics`) instead of a hardcoded literal, matching
what `PatientBuilder.FromCity` already does.

**Tech Stack:** .NET, C# records/enums, Bogus (`Bogus.Randomizer`), xUnit + Shouldly (existing test
stack in `Ignixa.FhirFakes.Tests`).

## Global Constraints

- Nullable reference types enabled — no new `#nullable disable`.
- One type per file — the new enum gets its own file, not appended to `CityDemographics.cs`.
- No `#region` blocks in new files (existing files that already use them are not touched for that).
- AAA test structure, BDD naming (`GivenX_WhenY_ThenZ`), using `Shouldly` assertions — match
  `test/Ignixa.FhirFakes.Tests/Population/PopulationGeneratorTests.cs` conventions exactly.
- `CancellationToken` rules don't apply here — none of the touched methods are async.
- This plan does **not** add UK cities or a `UKCorePatientProfile` — that's tracked separately in
  `docs/features/fhir-faker/investigations/uk-core-patient-profile.md` and is out of scope here. This
  plan only builds and proves the postal-code mechanism (including its alphanumeric-format arms) and
  fixes the two already-shipping bugs (AU/NL postcodes, Organization country hardcoding).

## Reviewed by Fable (principal-coding-agent), 2026-07-04

Design confirmed sound: `PostalCodeFormat` as plain data on `CityDemographics` (not a 4th
strategy/registry axis alongside `IPatientProfile` and name-locale mapping) is the right call —
`SampleAge`/`SampleGender`/`SampleAreaCode` on the same class are already data-driven, not strategy
objects, and this problem is the same shape. All "current code" quotes verified byte-accurate against
the real files; `SampleZipCode` has exactly two callers (`PatientBuilder.cs:726`,
`OrganizationState.cs:349`), both accounted for. Two things called out but deliberately **not**
changed by this plan:

- After this fix, auto-generated organizations carry ISO alpha-2 country codes (`"US"`, `"AU"`, `"NL"`)
  while `OrganizationAddress`'s record default and `OrganizationBuilder.WithAddress`'s default
  parameter stay `"USA"` (the manual-builder path, unaffected by this fix). This asymmetry is
  intentional — don't "fix" the manual-path default reflexively; it's a different code path with its
  own, valid default for when no city context exists.
- `OrganizationState.GeneratePhoneNumber` draws an *independent* random city from the one
  `GenerateAddress` uses, so a generated organization can carry, e.g., a Melbourne address with a
  Boston-shaped phone number. Pre-existing, out of scope here — worth a follow-up ticket alongside the
  UK Core Patient work.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/Core/Ignixa.FhirFakes/Population/PostalCodeFormat.cs` (new) | The enum: `NumericSuffix`, `FixedNumeric`, `DutchAlphaNumeric`, `UkAlphaNumeric` |
| `src/Core/Ignixa.FhirFakes/Population/CityDemographics.cs` (modify) | Add `PostalCodeFormat PostalCodeFormat = PostalCodeFormat.NumericSuffix` as a trailing optional record parameter |
| `src/Core/Ignixa.FhirFakes/Population/DemographicsDataProvider.cs` (modify) | `SampleZipCode` switches on format, using the existing `Bogus.Randomizer.String2` idiom (already used in `CoverageState.cs`/`OrganizationState.cs`) for letter sampling; tag Melbourne/Sydney as `FixedNumeric` and Amsterdam as `DutchAlphaNumeric` in `CreateDefault()` |
| `src/Core/Ignixa.FhirFakes/Scenarios/States/OrganizationState.cs` (modify) | `GenerateAddress` uses `city.Country` instead of the `"USA"` literal |
| `test/Ignixa.FhirFakes.Tests/Population/CityDemographicsTests.cs` (new) | Proves the new property's default and explicit-value construction |
| `test/Ignixa.FhirFakes.Tests/Population/DemographicsDataProviderTests.cs` (new) | Proves `SampleZipCode` produces correctly-shaped output for all four formats, including alphanumeric ones, and for the real Melbourne/Sydney/Amsterdam cities |
| `test/Ignixa.FhirFakes.Tests/OrganizationStateTests.cs` (modify) | Updates the one existing assertion that hardcodes `"USA"` to reflect the corrected, city-driven country |

---

### Task 1: Add `PostalCodeFormat` enum and wire it into `CityDemographics`

**Files:**
- Create: `src/Core/Ignixa.FhirFakes/Population/PostalCodeFormat.cs`
- Modify: `src/Core/Ignixa.FhirFakes/Population/CityDemographics.cs`
- Test: `test/Ignixa.FhirFakes.Tests/Population/CityDemographicsTests.cs`

**Interfaces:**
- Produces: `enum PostalCodeFormat { NumericSuffix, FixedNumeric, DutchAlphaNumeric, UkAlphaNumeric }` in namespace `Ignixa.FhirFakes.Population`; `CityDemographics.PostalCodeFormat` property (default `PostalCodeFormat.NumericSuffix`).

- [ ] **Step 1: Write the failing test**

Create `test/Ignixa.FhirFakes.Tests/Population/CityDemographicsTests.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.FhirFakes.Population;
using Xunit;

namespace Ignixa.FhirFakes.Tests.Population;

/// <summary>
/// Tests for the CityDemographics record, focused on PostalCodeFormat defaulting behavior.
/// </summary>
public class CityDemographicsTests
{
    [Fact]
    public void GivenCityDemographics_WhenPostalCodeFormatNotSpecified_ThenDefaultsToNumericSuffix()
    {
        // Arrange & Act
        var city = new CityDemographics(
            Name: "Testville",
            State: "Test State",
            Country: "US",
            Population: 1000,
            AgeGroupDistribution: new() { ["0-17"] = 1.0 },
            MaleRatio: 0.5,
            ZipCodePrefix: "000",
            AreaCodes: ["000"]);

        // Assert
        city.PostalCodeFormat.ShouldBe(PostalCodeFormat.NumericSuffix);
    }

    [Theory]
    [InlineData(PostalCodeFormat.NumericSuffix)]
    [InlineData(PostalCodeFormat.FixedNumeric)]
    [InlineData(PostalCodeFormat.DutchAlphaNumeric)]
    [InlineData(PostalCodeFormat.UkAlphaNumeric)]
    public void GivenCityDemographics_WhenPostalCodeFormatSpecified_ThenUsesThatValue(PostalCodeFormat format)
    {
        // Arrange & Act
        var city = new CityDemographics(
            Name: "Testville",
            State: "Test State",
            Country: "US",
            Population: 1000,
            AgeGroupDistribution: new() { ["0-17"] = 1.0 },
            MaleRatio: 0.5,
            ZipCodePrefix: "000",
            AreaCodes: ["000"],
            PostalCodeFormat: format);

        // Assert
        city.PostalCodeFormat.ShouldBe(format);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.FhirFakes.Tests/Ignixa.FhirFakes.Tests.csproj --filter "FullyQualifiedName~CityDemographicsTests"`
Expected: FAIL — build error, `CityDemographics` has no `PostalCodeFormat` parameter and `Ignixa.FhirFakes.Population.PostalCodeFormat` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/Core/Ignixa.FhirFakes/Population/PostalCodeFormat.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Population;

/// <summary>
/// Describes the shape a city's postal code should be sampled in, so <see cref="DemographicsDataProvider.SampleZipCode"/>
/// can generate a realistically-shaped value instead of assuming a single (US) format for every country.
/// </summary>
public enum PostalCodeFormat
{
    /// <summary>US-style: append a 2-digit numeric suffix to <see cref="CityDemographics.ZipCodePrefix"/> (e.g. "021" -&gt; "02105").</summary>
    NumericSuffix,

    /// <summary>Fixed code, no suffix — the prefix already is the full code (e.g. Australian postcodes: "3000").</summary>
    FixedNumeric,

    /// <summary>Dutch-style: the prefix, a space, then 2 random uppercase letters (e.g. "1011" -&gt; "1011 AB").</summary>
    DutchAlphaNumeric,

    /// <summary>UK-style: the prefix (an alphanumeric outward code), a space, then a digit and 2 random uppercase letters (e.g. "SW1A" -&gt; "SW1A 1AA").</summary>
    UkAlphaNumeric
}
```

Modify `src/Core/Ignixa.FhirFakes/Population/CityDemographics.cs` — the record parameter list currently
ends with:

```csharp
public record CityDemographics(
    string Name,
    string State,
    string Country,
    int Population,
    Dictionary<string, double> AgeGroupDistribution,
    double MaleRatio,
    string ZipCodePrefix,
    IReadOnlyList<string> AreaCodes,
    IReadOnlyDictionary<string, object>? Attributes = null
)
```

Change it to:

```csharp
public record CityDemographics(
    string Name,
    string State,
    string Country,
    int Population,
    Dictionary<string, double> AgeGroupDistribution,
    double MaleRatio,
    string ZipCodePrefix,
    IReadOnlyList<string> AreaCodes,
    IReadOnlyDictionary<string, object>? Attributes = null,
    PostalCodeFormat PostalCodeFormat = PostalCodeFormat.NumericSuffix
)
```

Also add a `<param>` doc-comment line above the record (next to the existing `<param name="Attributes">`
line) so the public API stays documented:

```csharp
/// <param name="PostalCodeFormat">The postal code shape to sample in (default: US-style numeric suffix). See <see cref="Population.PostalCodeFormat"/>.</param>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Ignixa.FhirFakes.Tests/Ignixa.FhirFakes.Tests.csproj --filter "FullyQualifiedName~CityDemographicsTests"`
Expected: PASS (5 tests: 1 default + 4 theory cases)

- [ ] **Step 5: Commit**

```bash
git add src/Core/Ignixa.FhirFakes/Population/PostalCodeFormat.cs src/Core/Ignixa.FhirFakes/Population/CityDemographics.cs test/Ignixa.FhirFakes.Tests/Population/CityDemographicsTests.cs
git commit -m "feat(fhir-faker): add PostalCodeFormat to CityDemographics"
```

---

### Task 2: Make `SampleZipCode` format-aware

**Files:**
- Modify: `src/Core/Ignixa.FhirFakes/Population/DemographicsDataProvider.cs:458-472`
- Test: `test/Ignixa.FhirFakes.Tests/Population/DemographicsDataProviderTests.cs` (new)

**Interfaces:**
- Consumes: `PostalCodeFormat` enum and `CityDemographics.PostalCodeFormat` from Task 1.
- Produces: `DemographicsDataProvider.SampleZipCode(CityDemographics, Bogus.Randomizer)` now format-aware — same signature, no callers need to change yet (Task 3 changes what data flows through it).

- [ ] **Step 1: Write the failing test**

Create `test/Ignixa.FhirFakes.Tests/Population/DemographicsDataProviderTests.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.FhirFakes.Population;
using Xunit;

namespace Ignixa.FhirFakes.Tests.Population;

/// <summary>
/// Tests for DemographicsDataProvider.SampleZipCode across all PostalCodeFormat shapes.
/// </summary>
public class DemographicsDataProviderTests
{
    private static CityDemographics MakeCity(string zipCodePrefix, PostalCodeFormat format) =>
        new(
            Name: "Testville",
            State: "Test State",
            Country: "XX",
            Population: 1000,
            AgeGroupDistribution: new() { ["0-17"] = 1.0 },
            MaleRatio: 0.5,
            ZipCodePrefix: zipCodePrefix,
            AreaCodes: ["000"],
            PostalCodeFormat: format);

    [Fact]
    public void GivenNumericSuffixFormat_WhenSamplingZipCode_ThenAppendsTwoDigitSuffix()
    {
        // Arrange
        var provider = DemographicsDataProvider.CreateDefault();
        var city = MakeCity("021", PostalCodeFormat.NumericSuffix);
        var randomizer = new Bogus.Randomizer();

        // Act
        var zipCode = provider.SampleZipCode(city, randomizer);

        // Assert
        zipCode.ShouldMatch("^021\\d{2}$");
    }

    [Fact]
    public void GivenFixedNumericFormat_WhenSamplingZipCode_ThenReturnsPrefixUnchanged()
    {
        // Arrange
        var provider = DemographicsDataProvider.CreateDefault();
        var city = MakeCity("3000", PostalCodeFormat.FixedNumeric);
        var randomizer = new Bogus.Randomizer();

        // Act
        var zipCode = provider.SampleZipCode(city, randomizer);

        // Assert
        zipCode.ShouldBe("3000");
    }

    [Fact]
    public void GivenDutchAlphaNumericFormat_WhenSamplingZipCode_ThenReturnsFourDigitsSpaceTwoLetters()
    {
        // Arrange
        var provider = DemographicsDataProvider.CreateDefault();
        var city = MakeCity("1011", PostalCodeFormat.DutchAlphaNumeric);
        var randomizer = new Bogus.Randomizer();

        // Act
        var zipCode = provider.SampleZipCode(city, randomizer);

        // Assert
        zipCode.ShouldMatch("^1011 [A-Z]{2}$");
    }

    [Fact]
    public void GivenUkAlphaNumericFormat_WhenSamplingZipCode_ThenReturnsOutwardCodeSpaceDigitTwoLetters()
    {
        // Arrange
        var provider = DemographicsDataProvider.CreateDefault();
        var city = MakeCity("SW1A", PostalCodeFormat.UkAlphaNumeric);
        var randomizer = new Bogus.Randomizer();

        // Act
        var zipCode = provider.SampleZipCode(city, randomizer);

        // Assert
        zipCode.ShouldMatch("^SW1A \\d[A-Z]{2}$");
    }

    [Fact]
    public void GivenSameSeed_WhenSamplingZipCodeTwice_ThenReturnsSameValue()
    {
        // Arrange
        var provider = DemographicsDataProvider.CreateDefault();
        var city = MakeCity("SW1A", PostalCodeFormat.UkAlphaNumeric);

        // Act
        var first = provider.SampleZipCode(city, new Bogus.Randomizer(42));
        var second = provider.SampleZipCode(city, new Bogus.Randomizer(42));

        // Assert
        first.ShouldBe(second);
    }
}
```

Note: `ShouldMatch` is an ordinary Shouldly string-assertion extension (same package as every other
`.ShouldBe`/`.ShouldNotBeNullOrEmpty` call in the test suite) — it takes a regex pattern string
directly, so no `System.Text.RegularExpressions` using is needed.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.FhirFakes.Tests/Ignixa.FhirFakes.Tests.csproj --filter "FullyQualifiedName~DemographicsDataProviderTests"`
Expected: FAIL — `GivenFixedNumericFormat...` gets `"300047"` instead of `"3000"`; `GivenDutchAlphaNumericFormat...`
and `GivenUkAlphaNumericFormat...` get a numeric-only suffix instead of the letter pattern.
`GivenNumericSuffixFormat...` and the determinism test should already PASS (current behavior).

- [ ] **Step 3: Write minimal implementation**

Modify `src/Core/Ignixa.FhirFakes/Population/DemographicsDataProvider.cs`, replacing the existing
`SampleZipCode` method **including its XML doc comment** (lines 458-472, from the `/// <summary>` line
through the method's closing brace):

```csharp
    /// <summary>
    /// Samples a postal code from the city's postal code range, shaped according to
    /// <see cref="CityDemographics.PostalCodeFormat"/>.
    /// </summary>
    /// <param name="city">City demographics.</param>
    /// <param name="randomizer">The seeded randomizer used for postal code sampling.</param>
    /// <example>
    /// Boston (prefix "021", NumericSuffix) → "02101", "02142", "02298", etc.
    /// Melbourne (prefix "3000", FixedNumeric) → "3000".
    /// Amsterdam (prefix "1011", DutchAlphaNumeric) → "1011 AB", etc.
    /// London (prefix "SW1A", UkAlphaNumeric) → "SW1A 1AA", etc.
    /// </example>
    public string SampleZipCode(CityDemographics city, Bogus.Randomizer randomizer)
    {
        ArgumentNullException.ThrowIfNull(randomizer);

        const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        return city.PostalCodeFormat switch
        {
            PostalCodeFormat.FixedNumeric => city.ZipCodePrefix,
            PostalCodeFormat.DutchAlphaNumeric => $"{city.ZipCodePrefix} {randomizer.String2(2, letters)}",
            PostalCodeFormat.UkAlphaNumeric => $"{city.ZipCodePrefix} {randomizer.Int(0, 9)}{randomizer.String2(2, letters)}",
            _ => city.ZipCodePrefix + randomizer.Int(0, 99).ToString("D2"),
        };
    }
```

`randomizer.String2(length, chars)` is the existing codebase idiom for sampling a random string from a
character set — already used identically in `CoverageState.cs:274`
(`_faker.Random.String2(3, "ABCDEFGHIJKLMNOPQRSTUVWXYZ")`) and `OrganizationState.cs:190/206`. No new
private helper method needed.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Ignixa.FhirFakes.Tests/Ignixa.FhirFakes.Tests.csproj --filter "FullyQualifiedName~DemographicsDataProviderTests"`
Expected: PASS (5 tests)

Also re-run the pre-existing zip/area code tests to confirm no regression:

Run: `dotnet test test/Ignixa.FhirFakes.Tests/Ignixa.FhirFakes.Tests.csproj --filter "FullyQualifiedName~PopulationGeneratorTests"`
Expected: PASS (unchanged — Boston/Massachusetts cities still default to `NumericSuffix`)

- [ ] **Step 5: Commit**

```bash
git add src/Core/Ignixa.FhirFakes/Population/DemographicsDataProvider.cs test/Ignixa.FhirFakes.Tests/Population/DemographicsDataProviderTests.cs
git commit -m "fix(fhir-faker): make SampleZipCode format-aware instead of always US-shaped"
```

---

### Task 3: Tag Melbourne, Sydney, and Amsterdam with their correct format

**Files:**
- Modify: `src/Core/Ignixa.FhirFakes/Population/DemographicsDataProvider.cs:339-394` (the `CreateDefault()` `AddCity` calls for Melbourne, Sydney, Amsterdam)
- Test: extend `test/Ignixa.FhirFakes.Tests/Population/DemographicsDataProviderTests.cs`

**Interfaces:**
- Consumes: `PostalCodeFormat` (Task 1), format-aware `SampleZipCode` (Task 2), `KnownCities.Melbourne` / `KnownCities.Sydney` / `KnownCities.Amsterdam` (existing, `src/Core/Ignixa.FhirFakes/Population/KnownCities.cs`).
- Produces: nothing new for later tasks — this is a leaf data fix.

- [ ] **Step 1: Write the failing test**

Add to `test/Ignixa.FhirFakes.Tests/Population/DemographicsDataProviderTests.cs`:

```csharp
    [Theory]
    [InlineData("Melbourne")]
    [InlineData("Sydney")]
    public void GivenAustralianKnownCity_WhenSamplingZipCode_ThenReturnsExactFourDigitPostcode(string cityName)
    {
        // Arrange
        var provider = DemographicsDataProvider.CreateDefault();
        var city = provider.Cities.First(c => c.Name == cityName);
        var randomizer = new Bogus.Randomizer();

        // Act
        var zipCode = provider.SampleZipCode(city, randomizer);

        // Assert
        zipCode.ShouldBe(city.ZipCodePrefix);
        zipCode.Length.ShouldBe(4);
    }

    [Fact]
    public void GivenAmsterdam_WhenSamplingZipCode_ThenReturnsFourDigitsSpaceTwoLetters()
    {
        // Arrange
        var provider = DemographicsDataProvider.CreateDefault();
        var city = provider.Cities.First(c => c.Name == "Amsterdam");
        var randomizer = new Bogus.Randomizer();

        // Act
        var zipCode = provider.SampleZipCode(city, randomizer);

        // Assert
        zipCode.ShouldMatch("^1011 [A-Z]{2}$");
    }
```

This requires `using System.Linq;` — add it to the file's usings if not already present via implicit
usings (this project has `<ImplicitUsings>enable</ImplicitUsings>`, so `System.Linq` is already
available without an explicit `using`; verify by checking the `.csproj` if the build fails on `.First`).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.FhirFakes.Tests/Ignixa.FhirFakes.Tests.csproj --filter "FullyQualifiedName~DemographicsDataProviderTests"`
Expected: FAIL — Melbourne/Sydney get 6-digit output (`PostalCodeFormat` still defaults to
`NumericSuffix` for these cities); Amsterdam gets a numeric-only 6-digit string instead of the
letter-suffixed pattern.

- [ ] **Step 3: Write minimal implementation**

In `src/Core/Ignixa.FhirFakes/Population/DemographicsDataProvider.cs`, the Melbourne `AddCity` call
currently ends with:

```csharp
        provider.AddCity(new CityDemographics(
            Name: "Melbourne",
            State: "Victoria",
            Country: "AU",
            Population: 5_078_000,
            AgeGroupDistribution: new() {
                ["0-17"] = 0.195,
                ["18-44"] = 0.445,
                ["45-64"] = 0.265,
                ["65+"] = 0.095
            },
            MaleRatio: 0.495,
            ZipCodePrefix: "3000",
            AreaCodes: ["03"],
            Attributes: new Dictionary<string, object>
            {
                [AUBasePatientProfile.IndigenousStatusDistributionKey] = australianIndigenousDistribution
            }
        ));
```

Add `PostalCodeFormat: PostalCodeFormat.FixedNumeric` after the `Attributes` argument:

```csharp
        provider.AddCity(new CityDemographics(
            Name: "Melbourne",
            State: "Victoria",
            Country: "AU",
            Population: 5_078_000,
            AgeGroupDistribution: new() {
                ["0-17"] = 0.195,
                ["18-44"] = 0.445,
                ["45-64"] = 0.265,
                ["65+"] = 0.095
            },
            MaleRatio: 0.495,
            ZipCodePrefix: "3000",
            AreaCodes: ["03"],
            Attributes: new Dictionary<string, object>
            {
                [AUBasePatientProfile.IndigenousStatusDistributionKey] = australianIndigenousDistribution
            },
            PostalCodeFormat: PostalCodeFormat.FixedNumeric
        ));
```

Do the same for the Sydney `AddCity` call (same shape, `ZipCodePrefix: "2000"`) — add
`PostalCodeFormat: PostalCodeFormat.FixedNumeric` as the last named argument.

The Amsterdam `AddCity` call currently ends with:

```csharp
        provider.AddCity(new CityDemographics(
            Name: "Amsterdam",
            State: "North Holland",
            Country: "NL",
            Population: 872_680,
            AgeGroupDistribution: new() {
                ["0-17"] = 0.165,
                ["18-44"] = 0.475,
                ["45-64"] = 0.245,
                ["65+"] = 0.115
            },
            MaleRatio: 0.498,
            ZipCodePrefix: "1011",
            AreaCodes: ["020"]
            // No profile-specific attributes for Amsterdam
        ));
```

Change it to:

```csharp
        provider.AddCity(new CityDemographics(
            Name: "Amsterdam",
            State: "North Holland",
            Country: "NL",
            Population: 872_680,
            AgeGroupDistribution: new() {
                ["0-17"] = 0.165,
                ["18-44"] = 0.475,
                ["45-64"] = 0.245,
                ["65+"] = 0.115
            },
            MaleRatio: 0.498,
            ZipCodePrefix: "1011",
            AreaCodes: ["020"],
            PostalCodeFormat: PostalCodeFormat.DutchAlphaNumeric
        ));
```

(The pre-existing `// No profile-specific attributes for Amsterdam` comment is dropped rather than
re-homed — it stated the obvious once `Attributes` is simply absent from the call, and CLAUDE.md's
comment rule is why-not-what.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Ignixa.FhirFakes.Tests/Ignixa.FhirFakes.Tests.csproj --filter "FullyQualifiedName~DemographicsDataProviderTests"`
Expected: PASS (8 tests total in this file)

Run the full FhirFakes test suite to confirm nothing else regressed:

Run: `dotnet test test/Ignixa.FhirFakes.Tests/Ignixa.FhirFakes.Tests.csproj`
Expected: PASS, 0 failures

- [ ] **Step 5: Commit**

```bash
git add src/Core/Ignixa.FhirFakes/Population/DemographicsDataProvider.cs test/Ignixa.FhirFakes.Tests/Population/DemographicsDataProviderTests.cs
git commit -m "fix(fhir-faker): correct Melbourne/Sydney/Amsterdam postal code formats"
```

---

### Task 4: Fix `OrganizationState` hardcoded `Country: "USA"`

**Files:**
- Modify: `src/Core/Ignixa.FhirFakes/Scenarios/States/OrganizationState.cs:345-364`
- Modify: `test/Ignixa.FhirFakes.Tests/OrganizationStateTests.cs:449-468`

**Interfaces:**
- Consumes: `CityDemographics.Country` (existing property, unchanged).
- Produces: nothing new for later tasks — this is a leaf fix, independent of Tasks 1-3 (it does not
  touch `PostalCodeFormat` at all, only the `Country` field of the same already-in-scope `city`
  variable in `GenerateAddress`).

- [ ] **Step 1: Write the failing test**

Replace the existing test in `test/Ignixa.FhirFakes.Tests/OrganizationStateTests.cs` (currently at
lines 449-468):

```csharp
    [Fact]
    public void GivenOrganization_WhenGenerated_ThenHasAddress()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddHospital()
            .Build();

        // Assert
        var organization = scenario.Organizations[0];
        var addresses = organization.MutableNode["address"];
        addresses.ShouldNotBeNull();

        var address = addresses![0];
        address!["city"]?.GetValue<string>().ShouldNotBeNullOrEmpty();
        address["state"]?.GetValue<string>().ShouldNotBeNullOrEmpty();
        address["postalCode"]?.GetValue<string>().ShouldNotBeNullOrEmpty();
        address["country"]?.GetValue<string>().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void GivenOrganization_WhenGeneratedFromKnownCity_ThenCountryMatchesThatCitysCountry()
    {
        // Arrange
        var demographics = DemographicsDataProvider.CreateDefault();

        // Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddHospital()
            .Build();

        // Assert
        var organization = scenario.Organizations[0];
        var address = organization.MutableNode["address"]![0];
        var cityName = address!["city"]?.GetValue<string>();
        var country = address["country"]?.GetValue<string>();

        cityName.ShouldNotBeNullOrEmpty();
        var matchingCity = demographics.Cities.First(c => c.Name == cityName);
        country.ShouldBe(matchingCity.Country);
    }
```

This requires `using Ignixa.FhirFakes.Population;` and `using System.Linq;` in
`OrganizationStateTests.cs` — add `using Ignixa.FhirFakes.Population;` to the file's using block
(`System.Linq` is available via implicit usings, same as Task 3).

This test is deterministic in both directions, not probabilistic: `GenerateAddress` always returns the
literal `"USA"` today, and every `CityDemographics.Country` in `CreateDefault()` is an ISO alpha-2 code
(`"US"`, `"AU"`, `"NL"`) — `"USA"` never equals any of them, so the assertion fails on every run
pre-fix regardless of which city gets randomly sampled, not just when an AU/NL city happens to be
picked. No seeding or loop is needed to make the failure reliable — a straight string-equality check
already is.

Also update the other pre-existing literal in the same file (line 479,
`GivenOrganizationWithCustomAddress_WhenGenerated_ThenUsesCustomAddress`) — **no change needed there**:
that test constructs an explicit `OrganizationAddress` with `Country: "USA"` directly (the manual
builder path), which is unaffected by this fix (see Step 3 — only the auto-generated path in
`GenerateAddress` changes).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.FhirFakes.Tests/Ignixa.FhirFakes.Tests.csproj --filter "FullyQualifiedName~GivenOrganization_WhenGeneratedFromKnownCity_ThenCountryMatchesThatCitysCountry"`
Expected: FAIL, every run — `country` is `"USA"`, `matchingCity.Country` is `"US"`/`"AU"`/`"NL"`
depending on which city was sampled; `ShouldBe` reports the mismatch regardless of which city that was.

- [ ] **Step 3: Write minimal implementation**

In `src/Core/Ignixa.FhirFakes/Scenarios/States/OrganizationState.cs`, the `GenerateAddress` method
currently is:

```csharp
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    private OrganizationAddress GenerateAddress()
    {
        var city = _demographics.Cities[_faker.Random.Int(0, _demographics.Cities.Count - 1)];
        var zipCode = _demographics.SampleZipCode(city, _faker.Random);
        var streetNumber = _faker.Random.Int(100, 9999);
        var streetName = _faker.Address.StreetName();
        var streetSuffix = _faker.Random.Bool() ? "Suite " + _faker.Random.Int(100, 999) : null;
        var line = streetSuffix is not null
            ? $"{streetNumber} {streetName}, {streetSuffix}"
            : $"{streetNumber} {streetName}";

        return new OrganizationAddress(
            Line: line,
            City: city.Name,
            State: city.State,
            PostalCode: zipCode,
            Country: "USA"
        );
    }
```

Change the `Country:` argument to read from the sampled city:

```csharp
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    private OrganizationAddress GenerateAddress()
    {
        var city = _demographics.Cities[_faker.Random.Int(0, _demographics.Cities.Count - 1)];
        var zipCode = _demographics.SampleZipCode(city, _faker.Random);
        var streetNumber = _faker.Random.Int(100, 9999);
        var streetName = _faker.Address.StreetName();
        var streetSuffix = _faker.Random.Bool() ? "Suite " + _faker.Random.Int(100, 999) : null;
        var line = streetSuffix is not null
            ? $"{streetNumber} {streetName}, {streetSuffix}"
            : $"{streetNumber} {streetName}";

        return new OrganizationAddress(
            Line: line,
            City: city.Name,
            State: city.State,
            PostalCode: zipCode,
            Country: city.Country
        );
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Ignixa.FhirFakes.Tests/Ignixa.FhirFakes.Tests.csproj --filter "FullyQualifiedName~OrganizationStateTests"`
Expected: PASS, all tests in the file green (including the updated
`GivenOrganization_WhenGenerated_ThenHasAddress` and the new
`GivenOrganization_WhenGeneratedFromKnownCity_ThenCountryMatchesThatCitysCountry`) — deterministically,
regardless of which city the random draw picks, since the assertion now compares against that same
city's own `Country` rather than a fixed set.

- [ ] **Step 5: Commit**

```bash
git add src/Core/Ignixa.FhirFakes/Scenarios/States/OrganizationState.cs test/Ignixa.FhirFakes.Tests/OrganizationStateTests.cs
git commit -m "fix(fhir-faker): Organization address country now reflects the sampled city, not a hardcoded USA"
```

---

## Final Verification

- [ ] Run the full solution build: `dotnet build All.sln` — expect 0 warnings, 0 errors.
- [ ] Run the full FhirFakes test project: `dotnet test test/Ignixa.FhirFakes.Tests/Ignixa.FhirFakes.Tests.csproj` — expect 0 failures.
- [ ] Confirm no other project references `CityDemographics`'s positional constructor in a way that
  would break from the new trailing parameter (it's optional, so this should be a non-issue, but worth
  a final `dotnet build All.sln` pass across the whole solution, not just this test project).
