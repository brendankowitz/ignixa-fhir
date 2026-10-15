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
/// Which loaded version answers a request without a version (Task 7 review ruling 3). Regressions: at 96d6150 the first
/// loaded source always answered, silently, so these tests fail there (verified in a worktree at that commit).
/// </summary>
public class TerminologyPrecedenceTests
{
    private const string System = "http://example.org/fhir/CodeSystem/shared";

#pragma warning disable CA2000 // The registry client owns the stub handler for the session's lifetime.
    private static FolderSession Session(TestFolder folder, TestFolder store, IReadOnlyList<(string Id, string Version, string CodeSystemVersion, string Extra)> packages, string[] load)
    {
        StubHttpHandler registry = Registry([.. packages.Select(package => (package.Id, package.Version, Archive(PackageFiles(package.Id, package.Version, "4.0.1", CodeSystem(System, package.CodeSystemVersion, package.Extra, $"a:From {package.Id}"))), (string?)null))]);
        return TerminologyFixture.Open(folder, Online(registry, store, load));
    }
#pragma warning restore CA2000

    private static async Task<JsonNode> Lookup(FolderSession session) => Json(await TermLookup.LookupAsync(session, System, "a", null, CancellationToken.None));

    [Theory]
    [InlineData("pkg.a@1.0.0", "pkg.b@1.0.0")]
    [InlineData("pkg.b@1.0.0", "pkg.a@1.0.0")]
    public async Task GivenTwoSemVerVersionsInEitherLoadOrder_WhenAskedWithoutAVersion_ThenTheHighestAnswersAndTheOtherIsNamed(string first, string second)
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        FolderSession session = Session(folder, store, [("pkg.a", "1.0.0", "1.2.0", ""), ("pkg.b", "1.0.0", "1.10.0", "")], [first, second]);

        JsonNode result = await Lookup(session);

        ((string)result["facts"]!["version"]!).ShouldBe("1.10.0");
        ((string)result["facts"]!["display"]!).ShouldBe("From pkg.b");
        JsonNode note = result["issues"]!.AsArray().Single(issue => (string)issue!["code"]! == CodeSystemResolution.OtherVersionsCode)!;
        ((string)note["severity"]!).ShouldBe("information");
        ((string)note["diagnostics"]!).ShouldContain("1.2.0 from pkg.a@1.0.0");
        ((string)note["diagnostics"]!).ShouldContain("1.10.0 from pkg.b@1.0.0");
    }

    [Theory]
    [InlineData("pkg.a@1.0.0", "pkg.b@1.0.0", "2018-08-12")]
    [InlineData("pkg.b@1.0.0", "pkg.a@1.0.0", "3.0.0")]
    public async Task GivenVersionsThatAreNotAllSemVer_WhenAskedWithoutAVersion_ThenTheFirstLoadedAnswers(string first, string second, string expected)
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        FolderSession session = Session(folder, store, [("pkg.a", "1.0.0", "2018-08-12", ""), ("pkg.b", "1.0.0", "3.0.0", "")], [first, second]);

        JsonNode result = await Lookup(session);

        ((string)result["facts"]!["version"]!).ShouldBe(expected);
        result["issues"]!.AsArray().Select(issue => (string)issue!["code"]!).ShouldContain(CodeSystemResolution.OtherVersionsCode);
    }

    [Fact]
    public async Task GivenASharedIntegerVersionAlgorithm_WhenAskedWithoutAVersion_ThenItOrdersTheVersions()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        const string Integer = ",\"versionAlgorithmString\":\"integer\"";
        FolderSession session = Session(folder, store, [("pkg.a", "1.0.0", "10", Integer), ("pkg.b", "1.0.0", "9", Integer)], ["pkg.b@1.0.0", "pkg.a@1.0.0"]);

        ((string)(await Lookup(session))["facts"]!["version"]!).ShouldBe("10");
    }

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

    // Guard: the folder already came first at 96d6150; the ruling keeps it first over a higher package version.
    [Fact]
    public async Task GivenTheFolderDefinesALowerVersion_WhenAskedWithoutAVersion_ThenTheFolderAnswers()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        folder.Write("cs.json", CodeSystem(System, "0.1.0", "", "a:From the folder"));
        FolderSession session = Session(folder, store, [("pkg.a", "1.0.0", "5.0.0", "")], ["pkg.a@1.0.0"]);

        JsonNode result = await Lookup(session);

        ((string)result["facts"]!["display"]!).ShouldBe("From the folder");
        result["facts"]!["source"]!["package"].ShouldBeNull();
    }

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

        ((string)result["facts"]!["display"]!).ShouldBe("From pkg.a");
        ((string)result["issues"]!.AsArray().Single(issue => (string)issue!["code"]! == CanonicalConflict.Code)!["diagnostics"]!).ShouldContain("Answers use pkg.a@1.0.0");
    }
}
