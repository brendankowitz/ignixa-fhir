# Investigation: UK Core Patient Profile

**Feature**: fhir-faker
**Status**: Implemented
**Created**: 2026-07-04
**Implemented**: 2026-07-04 (PR #300)

## Approach

Add a fourth national `IPatientProfile` implementation — `UKCorePatientProfile` — following the exact
pattern already established by `USCorePatientProfile` and `AUBasePatientProfile`, plus a small set of
UK cities in `DemographicsDataProvider`. Two smaller, independent fixes fall out of the same research
and could ship on their own regardless of whether the full profile is built.

### 1. UKCorePatientProfile (new)

Modeled on the HL7 UK Core FHIR Implementation Guide (`fhir.hl7.org.uk`):

- `ProfileUrl`: `https://fhir.hl7.org.uk/StructureDefinition/UKCore-Patient`
- `CountryCode`: `"GB"`
- `NameGenerationStrategy`: new `UKCoreNameGenerationStrategy` using Bogus locale `en_GB` (see finding
  below — this locale exists but is currently unused).
- `RequiredAttributes`: `["ethnicCategory"]`
- `BuildExtensions`: UK Core Ethnic Category extension
  (`https://fhir.hl7.org.uk/StructureDefinition/Extension-UKCore-EthnicCategory`), coded from the ONS
  2011 census ethnic category CodeSystem (`https://fhir.hl7.org.uk/CodeSystem/UKCore-EthnicCategory`),
  plus the existing BMI extension pattern shared by all profiles.
- `BuildIdentifiers`: NHS Number identifier (`system: https://fhir.nhs.uk/Id/nhs-number`), with the
  `NHSNumberVerificationStatus` extension on the identifier (codes `01`–`06`, e.g. `01` = "Number
  present and verified"). This is the UK analogue of what AU Base leaves as "not implemented" for
  Medicare/IHI numbers — NHS Number is the one identifier every UK Core `Patient` example in the IG
  carries, so it's worth implementing rather than deferring.
- `SampleProfileAttributes`: samples an ethnic category code from a per-city distribution key
  (`ethnicCategoryDistribution`), same shape as `EthnicityDistributionKey` (US) and
  `IndigenousStatusDistributionKey` (AU).

ONS 2011 ethnic category codes (18 total, grouped): White (`A` British, `B` Irish, `C` Other White),
Mixed (`D` White+Black Caribbean, `E` White+Black African, `F` White+Asian, `G` Other Mixed), Asian
(`H` Indian, `J` Pakistani, `K` Bangladeshi, `L` Other Asian), Black (`M` Caribbean, `N` African, `P`
Other Black), Other (`R` Chinese, `S` Other ethnic group), plus `Z` Not stated. This is structurally
identical to `USCorePatientProfile.Race` — a nested static class of string constants would follow the
same convention.

### 2. UK/Ireland name locale fix (independent, no profile needed)

`DefaultNameGenerationStrategy.CountryToLocaleMapping` currently maps `GB`, `UK`, and `IE` all to the
generic `"en"` Bogus locale. Verified against the installed `Bogus 35.6.5` package
(`~/.nuget/packages/bogus/35.6.5`) that `en_GB` and `en_IE` are real, working locales — not just
theoretical Bogus features:

```
en_GB: OK first=Leland last=Rutherford
en_IE: OK first=Rachel last=Hilpert
```

This is a one-line-per-entry change to the mapping table regardless of whether `UKCorePatientProfile`
ships, and would also fix the same gap for Ireland.

### 3. UK cities + postcode format gap

Candidate cities for `DemographicsDataProvider.CreateDefault()`, by population, with dialing code and
postcode outward-code prefix:

| City | Dialing code | Postcode outward prefix | Nation |
|---|---|---|---|
| London | 020 | SW1 (illustrative — London spans dozens of outward codes) | England |
| Birmingham | 0121 | B1 | England |
| Manchester | 0161 | M1 | England |
| Glasgow | 0141 | G1 | Scotland |
| Edinburgh | 0131 | EH1 | Scotland |
| Leeds | 0113 | LS1 | England |
| Liverpool | 0151 | L1 | England |
| Bristol | 0117 | BS1 | England |
| Cardiff | 029 | CF10 | Wales |
| Belfast | 028 | BT1 | Northern Ireland |

**Real blocker, not just missing data — and it turns out to already be live, not just a UK
problem**: `DemographicsDataProvider.SampleZipCode` unconditionally appends a 2-digit numeric suffix
(`randomizer.Int(0, 99).ToString("D2")`) to `ZipCodePrefix`. That's correct for a US ZIP (`021` + `05`
→ `02105`), but it's *already wrong* for the two international cities shipping today — Melbourne
(`3000` + `05` → `300005`, a 6-digit string, when a real AU postcode is 4 digits) and Amsterdam
(`1011` + `23` → `101123`, when a real Dutch postcode is `1011 AB`-shaped). A UK postcode would fail
the same way (`SW1A` + `05` → `SW1A05`, when a real one looks like `SW1A 1AA`). This is spun out into
its own investigation — see [country-aware-postal-codes](country-aware-postal-codes.md) — since fixing
it benefits Melbourne/Sydney/Amsterdam regardless of whether UK ships.

## Tradeoffs

| Pros | Cons |
|------|------|
| Follows an established, well-tested pattern (`IPatientProfile` + `INameGenerationStrategy` + registry) almost exactly — low design risk | UK Core ethnic category (18 ONS codes) is a different shape than US Core race (free-text) or AU indigenous status (5 codes) — needs its own CodeSystem constant, not reuse |
| NHS Number + verification status is a real, spec-backed identifier every UK Core Patient example uses — good test-data fidelity | Realistic per-city ethnic category *distributions* need real ONS 2021 census data — London's diversity is far above the national average; guessing risks quietly-wrong demographics that look plausible in a demo |
| en_GB/en_IE locale fix is decoupled — ships independently of everything else, zero risk | UK postcode format doesn't fit the existing numeric-suffix sampler — either ship approximate postcodes now or take on a bigger `DemographicsDataProvider` refactor |
| Extends country coverage to 4 profiles (US/AU/UK/default-NL), improving international test-data credibility | Adds another `IPatientProfile` singleton + strategy pair to maintain and version alongside FHIR schema/version changes |

## Alignment

- [x] Follows architectural layering rules — profile stays entirely inside `Ignixa.FhirFakes` (Core), no `Hl7.Fhir.*` dependency, matches existing US/AU profiles' file-per-type layout
- [x] Developer Experience (works with minimal setup) — `PatientProfileFactory.GetProfile("GB")` and `KnownCities.London` would work with no new setup, same as `KnownCities.Boston` today
- [x] Specification compliance (if applicable) — profile URL, NHS Number identifier system, and ethnic category CodeSystem/extension URLs are taken directly from the published UK Core IG rather than invented
- [x] Consistent with existing patterns — the postcode-format gap (see above) was resolved by the `PostalCodeFormat` enum in [country-aware-postal-codes](country-aware-postal-codes.md), so London (and Melbourne/Sydney/Amsterdam) now sample correctly-shaped postal codes through the same `SampleZipCode` method every city uses

## Evidence

- Read `src/Core/Ignixa.FhirFakes/Builders/Profiles/{IPatientProfile,PatientProfileFactory,USCorePatientProfile,AUBasePatientProfile,AUBaseNameGenerationStrategy,DefaultPatientProfile,DefaultNameGenerationStrategy,INameGenerationStrategy}.cs` — confirmed the profile/strategy contract and registry shape.
- Read `src/Core/Ignixa.FhirFakes/Population/{DemographicsDataProvider,CityDemographics,KnownCities,LocalBasedNameGenerator}.cs` — confirmed city data lives inline in `DemographicsDataProvider.CreateDefault()`, `CityDemographics.GetProfile()` dispatches via `PatientProfileFactory`, and `SampleZipCode`'s numeric-suffix logic is the format blocker.
- Ran a throwaway console app against the restored `Bogus 35.6.5` NuGet package (`~/.nuget/packages/bogus/35.6.5`) to confirm `en_GB` and `en_IE` are real, loadable Bogus locales, not just documented-but-absent ones.
- Grepped the repo (`UKCore`, `UK Core`, `en_GB`, `NHS number`, `nhs-number`) — no prior UK work exists; only unrelated matches in generated FHIR ValueSet resx files.
- Checked `docs/adr/` and `docs/site/docs/core-sdk/fhir-fakes.md` — no ADR or documented plan for UK support; NL (Amsterdam) is the only non-US/AU city today and it deliberately ships with `DefaultPatientProfile` (no country-specific extensions), which is the fallback this investigation would improve on for GB specifically.
- UK Core FHIR IG structure (profile URL, NHS Number system/verification-status codes, Ethnic Category extension URL and ONS 2011 code list) is from HL7 UK Core (`fhir.hl7.org.uk`). **Cross-checked against the live IG at implementation time** via web search (`build.fhir.org/ig/HL7-UK/UK-Core-Access`, `fhir.hl7.org.uk` CodeSystem/StructureDefinition pages, NHS Data Dictionary's "ETHNIC CATEGORY" data element) — the profile URL, NHS Number identifier system, both extension URLs, and the full ethnic category code list were confirmed to match the values used in `UKCorePatientProfile.cs` before merge, not just carried over from this doc's original estimate.

## Verdict

**Implemented** — all three decisions below were carried out in PR #300:

1. Shipped the `en_GB`/`en_IE` locale fix in `DefaultNameGenerationStrategy` (also later extended to the informal `UK` alias, mapped to `en_GB`, following review feedback).
2. Built `UKCorePatientProfile` following the US/AU pattern, with canonical URLs verified against the live UK Core IG (see updated Evidence above) and a London ethnic-category distribution derived from 2021 Census data.
3. Shipped the postal code fix — see [country-aware-postal-codes](country-aware-postal-codes.md) — as a `PostalCodeFormat` enum on `CityDemographics`, fixing the already-live Melbourne/Sydney/Amsterdam bug in the same change.

Post-merge review (5 specialized review agents + the `ocr` CLI + Gemini Code Assist) surfaced one real correctness bug not caught before merge: `UKCorePatientProfile.GenerateNhsNumber` used `Random.Shared` instead of the seeded `Bogus.Randomizer`, silently breaking this library's seeded-reproducibility contract for UK Core patients specifically (every other profile's sampling was already seed-aware). Fixed as a follow-up commit on the same PR by threading `Bogus.Randomizer` through `IPatientProfile.BuildIdentifiers`.

## Alternatives noted for future investigation

- **Country-aware postal code sampling in `DemographicsDataProvider`** — spun out into its own
  investigation: [country-aware-postal-codes](country-aware-postal-codes.md). Turned out to be more
  than a UK blocker: `SampleZipCode`'s numeric-suffix logic already produces invalid postcodes for
  Melbourne, Sydney, and Amsterdam today.
- **NL (Dutch) Core Patient profile** — Amsterdam already exists as a city but sits on
  `DefaultPatientProfile` with no country-specific extensions; a `NLCorePatientProfile` would be the
  same shape of work as this UK investigation, for a country that already has partial data in the
  system.
- **Real census/ONS data ingestion instead of hand-maintained per-city dictionaries** — the doc
  comment on `DemographicsDataProvider` already flags "Future enhancement: Load from demographics.csv
  for 29,000+ cities"; a UK profile built on hand-estimated ethnicity percentages is exactly the kind
  of data this future enhancement would replace.
