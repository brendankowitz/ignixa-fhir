// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology;
using Ignixa.CliOnFhir.Terminology.Tools;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.TerminologyFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// Which loaded version answers a request without a version, and which copy a conflict keeps (Task 7 fix round 2, F2):
/// the folder, then package authority (hl7.terminology.* for terminology.hl7.org canonicals; core always last), then the
/// highest version within one package id, then load order. Each test says whether it is a regression (fails at 4073a28,
/// verified in a worktree at that commit, where SemVer was compared across packages) or a guard.
/// </summary>
public class TerminologyPrecedenceTests
{
    private const string System = "http://example.org/fhir/CodeSystem/shared";
    private const string Tho = "http://terminology.hl7.org/CodeSystem/observation-category";
    private const string ThoPackage = "hl7.terminology.r4@7.4.0";
    private const string CorePackage = "hl7.fhir.r4.core@4.0.1";

#pragma warning disable CA2000 // The registry client owns the stub handler for the session's lifetime.
    private static FolderSession Session(TestFolder folder, TestFolder store, IReadOnlyList<(string Id, string Version, string CodeSystemVersion, string Extra)> packages, string[] load, string url = System)
    {
        StubHttpHandler registry = Registry([.. packages.Select(package => (package.Id, package.Version, Archive(PackageFiles(package.Id, package.Version, "4.0.1", CodeSystem(url, package.CodeSystemVersion, package.Extra, $"a:From {package.Id}@{package.Version}"))), (string?)null))]);
        return TerminologyFixture.Open(folder, Online(registry, store, load));
    }
#pragma warning restore CA2000

    private static async Task<JsonNode> Lookup(FolderSession session, string url = System) => Json(await TermLookup.LookupAsync(session, url, "a", null, CancellationToken.None));

    private static string Display(JsonNode result) => (string)result["facts"]!["display"]!;

    private static string OtherVersionsNote(JsonNode result) =>
        (string)result["issues"]!.AsArray().Single(issue => (string)issue!["code"]! == CodeSystemResolution.OtherVersionsCode)!["diagnostics"]!;

    // Regression: at 4073a28 the higher SemVer (1.10.0 from pkg.b) answered in both load orders.
    [Theory]
    [InlineData("pkg.a@1.0.0", "pkg.b@1.0.0", "1.2.0")]
    [InlineData("pkg.b@1.0.0", "pkg.a@1.0.0", "1.10.0")]
    public async Task GivenTwoPackagesOfEqualAuthority_WhenAskedWithoutAVersion_ThenTheFirstLoadedAnswersAndTheOtherIsNamed(string first, string second, string expected)
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        FolderSession session = Session(folder, store, [("pkg.a", "1.0.0", "1.2.0", ""), ("pkg.b", "1.0.0", "1.10.0", "")], [first, second]);

        JsonNode result = await Lookup(session);

        ((string)result["facts"]!["version"]!).ShouldBe(expected);
        string note = OtherVersionsNote(result);
        note.ShouldContain("1.2.0 from pkg.a@1.0.0");
        note.ShouldContain("1.10.0 from pkg.b@1.0.0");
        result["issues"]!.AsArray().Single(issue => (string)issue!["code"]! == CodeSystemResolution.OtherVersionsCode)!["severity"]!.GetValue<string>().ShouldBe("information");
    }

    // Guard: two versions of one package compare by SemVer, as at 4073a28.
    [Theory]
    [InlineData("pkg.a@1.0.0", "pkg.a@2.0.0")]
    [InlineData("pkg.a@2.0.0", "pkg.a@1.0.0")]
    public async Task GivenTwoVersionsOfOnePackageInEitherLoadOrder_WhenAskedWithoutAVersion_ThenTheHighestAnswers(string first, string second)
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        FolderSession session = Session(folder, store, [("pkg.a", "1.0.0", "1.10.0", ""), ("pkg.a", "2.0.0", "1.2.0", "")], [first, second]);

        JsonNode result = await Lookup(session);

        ((string)result["facts"]!["version"]!).ShouldBe("1.10.0");
        Display(result).ShouldBe("From pkg.a@1.0.0");
    }

    // Guard: versions that are not all SemVer were already left to load order at 4073a28.
    [Theory]
    [InlineData("pkg.a@1.0.0", "pkg.a@2.0.0", "2018-08-12")]
    [InlineData("pkg.a@2.0.0", "pkg.a@1.0.0", "3.0.0")]
    public async Task GivenVersionsOfOnePackageThatAreNotAllSemVer_WhenAskedWithoutAVersion_ThenTheFirstLoadedAnswers(string first, string second, string expected)
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        FolderSession session = Session(folder, store, [("pkg.a", "1.0.0", "2018-08-12", ""), ("pkg.a", "2.0.0", "3.0.0", "")], [first, second]);

        JsonNode result = await Lookup(session);

        ((string)result["facts"]!["version"]!).ShouldBe(expected);
        result["issues"]!.AsArray().Select(issue => (string)issue!["code"]!).ShouldContain(CodeSystemResolution.OtherVersionsCode);
    }

    // Guard: a shared versionAlgorithm already ordered versions at 4073a28; now only within one package id.
    [Fact]
    public async Task GivenASharedIntegerVersionAlgorithmWithinOnePackage_WhenAskedWithoutAVersion_ThenItOrdersTheVersions()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        const string Integer = ",\"versionAlgorithmString\":\"integer\"";
        FolderSession session = Session(folder, store, [("pkg.a", "1.0.0", "10", Integer), ("pkg.a", "2.0.0", "9", Integer)], ["pkg.a@2.0.0", "pkg.a@1.0.0"]);

        ((string)(await Lookup(session))["facts"]!["version"]!).ShouldBe("10");
    }

    // Guard: the default core package already ranked below an explicit load at 4073a28.
    [Fact]
    public async Task GivenTheDefaultCorePackageAndALaterExplicitLoad_WhenAskedWithoutAVersion_ThenTheExplicitPackageAnswersEvenWithALowerVersion()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        FolderSession session = Session(folder, store, [("hl7.fhir.r4.core", "4.0.1", "9.0.0", ""), ("pkg.a", "1.0.0", "1.0.0", "")], []);
        session = TerminologyFixture.Open(folder, session.Terminology.Options with { Packages = [] });
        _ = await TermList.ListAsync(session, null, null, null, null, 0, false, CancellationToken.None);
        _ = await TermLoad.LoadAsync(session, "pkg.a@1.0.0", CancellationToken.None);

        JsonNode result = await Lookup(session);

        ((string)result["facts"]!["source"]!["package"]!).ShouldBe("pkg.a@1.0.0");
        ((string)result["facts"]!["version"]!).ShouldBe("1.0.0");
    }

    // Regression: at 4073a28 an explicitly loaded core package ranked with other packages, so its higher version answered.
    [Fact]
    public async Task GivenCoreLoadedExplicitlyBeforeAnotherPackage_WhenAskedWithoutAVersion_ThenTheOtherPackageAnswers()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        FolderSession session = Session(folder, store, [("hl7.fhir.r4.core", "4.0.1", "9.0.0", ""), ("pkg.a", "1.0.0", "1.0.0", "")], [CorePackage, "pkg.a@1.0.0"]);

        JsonNode result = await Lookup(session);

        ((string)result["facts"]!["source"]!["package"]!).ShouldBe("pkg.a@1.0.0");
        OtherVersionsNote(result).ShouldContain("9.0.0 from hl7.fhir.r4.core@4.0.1");
    }

    // Regression (the reviewer's observation-category repro): at 4073a28 core's 4.0.1 copy outranked THO's 2.0.0 by SemVer
    // in both load orders.
    [Theory]
    [InlineData(ThoPackage, CorePackage)]
    [InlineData(CorePackage, ThoPackage)]
    public async Task GivenATerminologyHl7CodeSystemInThoAndCoreInEitherLoadOrder_WhenAskedWithoutAVersion_ThenThoAnswers(string first, string second)
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        FolderSession session = Session(folder, store, [("hl7.terminology.r4", "7.4.0", "2.0.0", ""), ("hl7.fhir.r4.core", "4.0.1", "4.0.1", "")], [first, second], Tho);

        JsonNode result = await Lookup(session, Tho);

        ((string)result["facts"]!["version"]!).ShouldBe("2.0.0");
        ((string)result["facts"]!["source"]!["package"]!).ShouldBe(ThoPackage);
        string note = OtherVersionsNote(result);
        note.ShouldContain("2.0.0 from hl7.terminology.r4@7.4.0");
        note.ShouldContain("4.0.1 from hl7.fhir.r4.core@4.0.1");
    }

    // Regression: at 4073a28 the higher SemVer of the package loaded alongside THO answered.
    [Fact]
    public async Task GivenATerminologyHl7CodeSystemInThoAndAnotherPackageLoadedFirst_WhenAskedWithoutAVersion_ThenThoAnswers()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        FolderSession session = Session(folder, store, [("pkg.ig", "1.0.0", "3.0.0", ""), ("hl7.terminology.r4", "7.4.0", "2.0.0", "")], ["pkg.ig@1.0.0", ThoPackage], Tho);

        ((string)(await Lookup(session, Tho))["facts"]!["source"]!["package"]!).ShouldBe(ThoPackage);
    }

    // Regression: THO's authority is limited to terminology.hl7.org canonicals; elsewhere it is one package among others, so
    // load order decides. At 4073a28 the higher SemVer (THO's 2.0.0) answered.
    [Fact]
    public async Task GivenAnotherCanonicalInThoAndAnotherPackageLoadedFirst_WhenAskedWithoutAVersion_ThenTheFirstLoadedAnswers()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        FolderSession session = Session(folder, store, [("pkg.ig", "1.0.0", "1.0.0", ""), ("hl7.terminology.r4", "7.4.0", "2.0.0", "")], ["pkg.ig@1.0.0", ThoPackage]);

        ((string)(await Lookup(session))["facts"]!["source"]!["package"]!).ShouldBe("pkg.ig@1.0.0");
    }

    // Guard: the folder already came first at 4073a28; authority keeps it first over a higher package version.
    [Fact]
    public async Task GivenTheFolderDefinesALowerVersion_WhenAskedWithoutAVersion_ThenTheFolderAnswers()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        folder.Write("cs.json", CodeSystem(Tho, "0.1.0", "", "a:From the folder"));
        FolderSession session = Session(folder, store, [("hl7.terminology.r4", "7.4.0", "5.0.0", "")], [ThoPackage], Tho);

        JsonNode result = await Lookup(session, Tho);

        Display(result).ShouldBe("From the folder");
        result["facts"]!["source"]!["package"].ShouldBeNull();
    }

    // Guard: the default core package already lost a conflict to an explicit package at 4073a28.
    [Fact]
    public async Task GivenTheDefaultCoreAndAnExplicitPackageDisagreeOnOneVersion_WhenLookedUp_ThenTheExplicitDefinitionIsKept()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        FolderSession session = Session(folder, store, [("hl7.fhir.r4.core", "4.0.1", "1.0.0", ""), ("pkg.a", "1.0.0", "1.0.0", "")], []);
        session = TerminologyFixture.Open(folder, session.Terminology.Options with { Packages = [] });
        _ = await TermList.ListAsync(session, null, null, null, null, 0, false, CancellationToken.None);
        _ = await TermLoad.LoadAsync(session, "pkg.a@1.0.0", CancellationToken.None);

        JsonNode result = await Lookup(session);

        Display(result).ShouldBe("From pkg.a@1.0.0");
        ((string)result["issues"]!.AsArray().Single(issue => (string)issue!["code"]! == CanonicalConflict.Code)!["diagnostics"]!).ShouldContain("Answers use pkg.a@1.0.0");
    }

    // Regression: at 4073a28 a conflict kept the first-loaded copy among explicit packages, here core's.
    [Fact]
    public async Task GivenCoreLoadedFirstAndThoDisagreeOnOneVersion_WhenLookedUp_ThenThoDefinitionIsKept()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        FolderSession session = Session(folder, store, [("hl7.fhir.r4.core", "4.0.1", "2.0.0", ""), ("hl7.terminology.r4", "7.4.0", "2.0.0", "")], [CorePackage, ThoPackage], Tho);

        JsonNode result = await Lookup(session, Tho);

        Display(result).ShouldBe("From hl7.terminology.r4@7.4.0");
        ((string)result["issues"]!.AsArray().Single(issue => (string)issue!["code"]! == CanonicalConflict.Code)!["diagnostics"]!).ShouldContain("Answers use hl7.terminology.r4@7.4.0");
    }
}
