// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Indexing;
using Ignixa.Search.InMemory;
using Ignixa.Search.Models;

namespace Ignixa.Search.Tests.InMemory;

/// <summary>
/// Chains, <c>_has</c>, <c>_sort</c> and includes over an <see cref="IInMemorySearchSource"/>. Several cases are ported
/// from fhir-candle's <c>SearchExecutorTests</c> (src/FhirCandle.Engine.Tests/SearchExecutorTests.cs at 6d0af1e), the
/// behavior these features were specified from.
/// </summary>
public class InMemorySearchEngineTests
{
    private const string Acme = """{"resourceType":"Organization","id":"org-1","name":"Acme Health"}""";

    private const string Chalmers = """{"resourceType":"Patient","id":"pat-1","gender":"male","name":[{"family":"Chalmers"}],"birthDate":"1974-12-25","managingOrganization":{"reference":"Organization/org-1"}}""";

    private const string Ng = """{"resourceType":"Patient","id":"pat-2","gender":"female","name":[{"family":"Ng"}],"birthDate":"1990-06-15"}""";

    private static string Observation(string id, string subject, string code = "8480-6", string? performer = null) =>
        "{\"resourceType\":\"Observation\",\"id\":\"" + id + "\",\"status\":\"final\",\"code\":{\"coding\":[{\"system\":\"http://loinc.org\",\"code\":\"" + code + "\"}]},"
        + "\"subject\":{\"reference\":\"" + subject + "\"}"
        + (performer is null ? string.Empty : ",\"performer\":[{\"reference\":\"" + performer + "\"}]")
        + "}";

    private static IndexedTestSource Clinic() => new(
        Acme,
        Chalmers,
        Ng,
        """{"resourceType":"Practitioner","id":"dr-1","name":[{"family":"House"}]}""",
        Observation("obs-1", "Patient/pat-1", performer: "Practitioner/dr-1"),
        Observation("obs-2", "Patient/pat-2", code: "8867-4"),
        Observation("obs-3", "Patient/missing"),
        Observation("obs-4", "http://elsewhere.example.org/fhir/Patient/pat-1"));

    // Ported: SearchExecutorTests.EvaluateChained_SubjectNameChalmers_MatchesOnlyOnCorrectValue.
    [Theory]
    [InlineData("subject:Patient.name=Chalmers", "obs-1")]
    [InlineData("subject:Patient.name=Nomatch", "")]
    [InlineData("subject.gender=female", "obs-2")]
    [InlineData("subject:Patient.organization.name=Acme", "obs-1")]
    [InlineData("subject:Patient.organization.name=Other", "")]
    [InlineData("subject:Patient.gender=male&code=http://loinc.org|8480-6", "obs-1")]
    public void GivenForwardChain_WhenSearched_ThenTheReferencedResourceMustMatch(string query, string expected)
    {
        Clinic().Search("Observation", query).ShouldBe(Ids(expected));
    }

    [Fact]
    public void GivenReferencesToMissingAndExternalResources_WhenChained_ThenTheyNeverMatch()
    {
        // obs-3 references a patient the source does not hold and obs-4 one on another server with the same id.
        Clinic().Search("Observation", "subject:Patient.gender=male").ShouldBe(["obs-1"]);
    }

    // Ported: SearchExecutorTests.EvaluateChained_HasObservationSubjectCode_MatchesReferencedPatient.
    [Theory]
    [InlineData("_has:Observation:subject:code=http://loinc.org|8480-6", "pat-1")]
    [InlineData("_has:Observation:subject:code=http://loinc.org|1234-5", "")]
    [InlineData("_has:Observation:subject:code=8867-4", "pat-2")]
    [InlineData("_has:Observation:subject:performer:Practitioner.name=House", "pat-1")]
    [InlineData("_has:Observation:subject:code=8480-6&gender=female", "")]
    public void GivenReverseChain_WhenSearched_ThenAReferencingResourceMustMatch(string query, string expected)
    {
        Clinic().Search("Patient", query).ShouldBe(Ids(expected));
    }

    [Fact]
    public void GivenAChain_WhenSearched_ThenTheTargetTypeIsReadOnce()
    {
        IndexedTestSource source = Clinic();

        source.Search("Observation", "subject:Patient.gender=male");

        source.Enumerations["Patient"].ShouldBe(1);
    }

    [Theory]
    [InlineData("subject:Patient.name=Chalmers", "Observation")]
    [InlineData("_has:Observation:subject:code=8480-6", "Patient")]
    public void GivenChainFeaturesNotEnabled_WhenSearched_ThenTheSearchFailsInsteadOfIgnoringTheChain(string query, string resourceType)
    {
        IndexedTestSource source = Clinic();
        SearchOptions options = SearchCorpus.Options(resourceType, query);

        Should.Throw<SearchOperationNotSupportedException>(() => new InMemorySearchEngine(source, InMemorySearchFeatures.All & ~InMemorySearchFeatures.Chains & ~InMemorySearchFeatures.ReverseChains).Search(options));
        Should.Throw<SearchOperationNotSupportedException>(() => options.Expression.AcceptVisitor(new SearchQueryInterpreter(), default)(source.Enumerate(resourceType)).ToList());
    }

    // Ported: SearchExecutorTests.FhirSortComparer_SortsByBirthdate_AscendingAndDescending.
    [Theory]
    [InlineData("_sort=birthdate", "p-1970,p-1990,p-none")]
    [InlineData("_sort=-birthdate", "p-1990,p-1970,p-none")]
    [InlineData("_sort=family", "p-1990,p-1970,p-none")]
    [InlineData("_sort=-family", "p-1970,p-1990,p-none")]
    [InlineData("_sort=gender,-birthdate", "p-1990,p-1970,p-none")]
    [InlineData("_sort=_id", "p-1970,p-1990,p-none")]
    [InlineData("_sort=-_id", "p-none,p-1990,p-1970")]
    public void GivenSort_WhenSearched_ThenMatchesAreOrderedAndMissingValuesComeLast(string query, string expected)
    {
        var source = new IndexedTestSource(
            """{"resourceType":"Patient","id":"p-none","gender":"other"}""",
            """{"resourceType":"Patient","id":"p-1990","gender":"female","birthDate":"1990-06-15","name":[{"family":"Álvarez"}]}""",
            """{"resourceType":"Patient","id":"p-1970","gender":"female","birthDate":"1970-01-01","name":[{"family":"Brown"}]}""");

        source.Search("Patient", query).ShouldBe(Ids(expected));
    }

    [Fact]
    public void GivenSeveralValuesPerResource_WhenSorted_ThenAscendingUsesTheLowestAndDescendingTheHighest()
    {
        var source = new IndexedTestSource(
            """{"resourceType":"Patient","id":"wide","name":[{"family":"Adams"},{"family":"Young"}]}""",
            """{"resourceType":"Patient","id":"mid","name":[{"family":"Miller"}]}""");

        source.Search("Patient", "_sort=family").ShouldBe(["wide", "mid"]);
        source.Search("Patient", "_sort=-family").ShouldBe(["wide", "mid"]);
    }

    [Fact]
    public void GivenTiesOnEverySortKey_WhenSorted_ThenLastUpdatedThenIdBreakTheTie()
    {
        var source = new IndexedTestSource(
            """{"resourceType":"Patient","id":"c","gender":"male"}""",
            """{"resourceType":"Patient","id":"b","gender":"male","meta":{"lastUpdated":"2021-01-01T00:00:00Z"}}""",
            """{"resourceType":"Patient","id":"a","gender":"male","meta":{"lastUpdated":"2022-01-01T00:00:00Z"}}""",
            """{"resourceType":"Patient","id":"d","gender":"male"}""");

        source.Search("Patient", "_sort=gender").ShouldBe(["b", "a", "c", "d"]);
        source.Search("Patient", "_sort=-gender").ShouldBe(["b", "a", "c", "d"]);
    }

    [Fact]
    public void GivenSortOnDatesWithPeriods_WhenSortedDescending_ThenTheEndOfEachRangeIsCompared()
    {
        var source = new IndexedTestSource(
            """{"resourceType":"Encounter","id":"long","status":"finished","class":{"code":"AMB"},"period":{"start":"2020-01-01","end":"2020-12-31"}}""",
            """{"resourceType":"Encounter","id":"late","status":"finished","class":{"code":"AMB"},"period":{"start":"2020-06-01","end":"2020-06-02"}}""");

        source.Search("Encounter", "_sort=date").ShouldBe(["long", "late"]);
        source.Search("Encounter", "_sort=-date").ShouldBe(["long", "late"]);
    }

    [Fact]
    public void GivenSortNotEnabled_WhenSearched_ThenTheSearchFails()
    {
        SearchOptions options = SearchCorpus.Options("Patient", "_sort=birthdate");

        Should.Throw<SearchOperationNotSupportedException>(() => new InMemorySearchEngine(Clinic(), InMemorySearchFeatures.All & ~InMemorySearchFeatures.Sort).Search(options));
    }

    // Ported: SearchExecutorTests.ResolveIncludes_ObservationSubject_YieldsReferencedPatient.
    [Theory]
    [InlineData("_include=Observation:subject", "Patient/pat-1,Patient/pat-2")]
    [InlineData("code=8480-6&_include=Observation:subject", "Patient/pat-1")]
    [InlineData("_include=Observation:subject:Group", "")]
    [InlineData("_include=Observation:*", "Practitioner/dr-1,Patient/pat-1,Patient/pat-2")]
    [InlineData("_include=Observation:performer&_include=Observation:subject", "Practitioner/dr-1,Patient/pat-1,Patient/pat-2")]
    [InlineData("_include=Observation:subject&_include=Observation:performer", "Patient/pat-1,Practitioner/dr-1,Patient/pat-2")]
    [InlineData("_include=Patient:organization", "")]
    public void GivenInclude_WhenSearched_ThenReferencedResourcesTheSourceHoldsAreIncludedOnce(string query, string expected)
    {
        Clinic().Included("Observation", query).ShouldBe(Ids(expected));
    }

    // Ported: SearchExecutorTests.ResolveRevIncludes_ObservationSubject_YieldsReferencingObservation.
    [Theory]
    [InlineData("_id=pat-1&_revinclude=Observation:subject", "Observation/obs-1")]
    [InlineData("_revinclude=Observation:subject", "Observation/obs-1,Observation/obs-2")]
    [InlineData("_id=pat-1&_revinclude=Observation:performer", "")]
    public void GivenRevInclude_WhenSearched_ThenResourcesReferencingTheMatchesAreIncluded(string query, string expected)
    {
        Clinic().Included("Patient", query).ShouldBe(Ids(expected));
    }

    [Theory]
    [InlineData("_id=obs-1&_include=Observation:subject&_include=Patient:organization", "Patient/pat-1")]
    [InlineData("_id=obs-1&_include=Observation:subject&_include:iterate=Patient:organization", "Patient/pat-1,Organization/org-1")]
    public void GivenIterateInclude_WhenSearched_ThenItAppliesToIncludedResourcesToo(string query, string expected)
    {
        Clinic().Included("Observation", query).ShouldBe(Ids(expected));
    }

    [Fact]
    public void GivenIterateRevIncludeThatCycles_WhenSearched_ThenEachResourceIsIncludedOnce()
    {
        Clinic().Included("Patient", "_id=pat-1&_revinclude:iterate=Observation:subject&_include:iterate=Observation:subject")
            .ShouldBe(["Observation/obs-1"]);
    }

    [Fact]
    public void GivenIncludedResourceThatIsAlsoAMatch_WhenSearched_ThenItIsNotRepeated()
    {
        var source = new IndexedTestSource(
            """{"resourceType":"Patient","id":"a","link":[{"other":{"reference":"Patient/b"},"type":"seealso"}]}""",
            """{"resourceType":"Patient","id":"b","link":[{"other":{"reference":"Patient/a"},"type":"seealso"}]}""");

        source.Included("Patient", "_include=Patient:link").ShouldBeEmpty();
        source.Included("Patient", "_id=a&_include=Patient:link").ShouldBe(["Patient/b"]);
    }

    [Theory]
    [InlineData(InMemorySearchFeatures.Include, "_revinclude=Observation:subject")]
    [InlineData(InMemorySearchFeatures.RevInclude, "_include=Patient:organization")]
    public void GivenIncludeKindNotEnabled_WhenSearchedOrIncluded_ThenTheCallFails(InMemorySearchFeatures enabled, string query)
    {
        var engine = new InMemorySearchEngine(Clinic(), enabled);
        SearchOptions options = SearchCorpus.Options("Patient", query);

        Should.Throw<SearchOperationNotSupportedException>(() => engine.Search(options));
        Should.Throw<SearchOperationNotSupportedException>(() => engine.Include([], options, int.MaxValue));
    }

    [Fact]
    public void GivenNoExpression_WhenSearched_ThenEveryResourceOfTheTypeIsReturnedInSourceOrder()
    {
        new InMemorySearchEngine(Clinic(), InMemorySearchFeatures.None).Search(new SearchOptions { ResourceType = "Patient" })
            .Select(match => match.Location.Id)
            .ShouldBe(["pat-1", "pat-2"]);
    }

    [Theory]
    [InlineData("Patient", "family=Chalmers&gender=male", "pat-1")]
    [InlineData("Patient", "gender=male,female&_id:not=pat-1", "pat-2")]
    [InlineData("Patient", "_has:Observation:subject:code=8480-6&gender=male", "pat-1")]
    [InlineData("Observation", "subject:Patient.gender=male&code=8480-6", "obs-1")]
    [InlineData("Patient", "family=Chalmers,Ng&_sort=-birthdate", "pat-2,pat-1")]
    public void GivenASourceThatReturnsFreshIndexCollections_WhenSearched_ThenResultsAreTheSameAsFromAStableSource(string type, string query, string expected)
    {
        IndexedTestSource stable = Clinic();
        IndexedTestSource fresh = new(
            Acme,
            Chalmers,
            Ng,
            """{"resourceType":"Practitioner","id":"dr-1","name":[{"family":"House"}]}""",
            Observation("obs-1", "Patient/pat-1", performer: "Practitioner/dr-1"),
            Observation("obs-2", "Patient/pat-2", code: "8867-4"))
        {
            FreshCollections = true,
        };

        fresh.Search(type, query).ShouldBe(Ids(expected));
        stable.Search(type, query).ShouldBe(Ids(expected));
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "Observation/obs-1")]
    [InlineData(2, "Observation/obs-1,Observation/obs-2")]
    [InlineData(3, "Observation/obs-1,Observation/obs-2,Practitioner/dr-1")]
    [InlineData(10, "Observation/obs-1,Observation/obs-2,Practitioner/dr-1")]
    public void GivenAMaximum_WhenIncludesAreResolved_ThenResolutionStopsThere(int maximum, string expected)
    {
        IndexedTestSource source = Clinic();
        var engine = new InMemorySearchEngine(source, InMemorySearchFeatures.All);
        SearchOptions options = SearchCorpus.Options("Patient", "_revinclude:iterate=Observation:subject&_include:iterate=Observation:performer");

        engine.Include(engine.Search(options), options, maximum).Select(resource => resource.Location.ToString()).ShouldBe(Ids(expected));
    }

    private static string[] Ids(string expected) => expected.Length == 0 ? [] : expected.Split(',');
}
