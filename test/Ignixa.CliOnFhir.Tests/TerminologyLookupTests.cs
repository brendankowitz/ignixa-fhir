// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology.Tools;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.TerminologyFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// terminology_lookup over a package. New behavior (Task 7): 24779ff has no terminology store, so none of these tests
/// can exist there (verified: the file does not compile at that commit).
/// </summary>
public class TerminologyLookupTests
{
    [Fact]
    public async Task GivenALoadedCodeSystem_WhenACodeIsLookedUp_ThenItsDisplayDefinitionParentsAndSourceAreReturned()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        FolderSession session = OpenWithPackage(folder, store);

        JsonNode result = Json(await TermLookup.LookupAsync(session, Body, "heart", null, CancellationToken.None));

        JsonNode facts = result["facts"]!;
        ((string)facts["outcome"]!).ShouldBe("found");
        ((string)facts["version"]!).ShouldBe("1.0.0");
        ((string)facts["display"]!).ShouldBe("Heart disorder");
        ((string)facts["designations"]![0]!["value"]!).ShouldBe("Herzkrankheit");
        ((string)facts["parents"]![0]!["code"]!).ShouldBe("disorder");
        ((int)facts["childCount"]!).ShouldBe(1);
        ((bool)facts["inactive"]!).ShouldBeFalse();
        ((string)facts["source"]!["kind"]!).ShouldBe("local");
        ((string)facts["source"]!["package"]!).ShouldBe($"{PackageId}@{PackageVersion}");
        EvidenceFile(result, 0).ShouldBe($"{PackageId}#{PackageVersion}/package/CodeSystem-body.json");
        ((string)result["next"]![0]!["tool"]!).ShouldBe("terminology_navigate");
    }

    [Fact]
    public async Task GivenARetiredConcept_WhenLookedUp_ThenItIsReportedInactiveWithItsStatus()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode result = Json(await TermLookup.LookupAsync(OpenWithPackage(folder, store), Body, "old", null, CancellationToken.None));

        ((bool)result["facts"]!["inactive"]!).ShouldBeTrue();
        ((string)result["facts"]!["status"]!).ShouldBe("retired");
        result["issues"]!.AsArray().Select(issue => (string)issue!["code"]!).ShouldContain("inactive-concept");
    }

    [Fact]
    public async Task GivenACompleteCodeSystem_WhenTheCodeIsNotInIt_ThenTheOutcomeIsCodeNotInVersionNotSystemNotLoaded()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode result = Json(await TermLookup.LookupAsync(OpenWithPackage(folder, store), Body, "nope", null, CancellationToken.None));

        ((string)result["facts"]!["outcome"]!).ShouldBe("code-not-in-version");
        ((string)result["facts"]!["version"]!).ShouldBe("1.0.0");
        ((string)result["absent"]![0]!["what"]!).ShouldBe($"{Body}|nope");
        ((string)result["absent"]![0]!["checked"]!).ShouldContain("content 'complete'");
    }

    [Fact]
    public async Task GivenASystemNoSourceDefines_WhenLookedUp_ThenTheOutcomeIsSystemNotLoaded()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode result = Json(await TermLookup.LookupAsync(OpenWithPackage(folder, store), "http://loinc.org", "8867-4", null, CancellationToken.None));

        ((string)result["facts"]!["outcome"]!).ShouldBe("system-not-loaded");
        result["facts"]!["source"].ShouldBeNull();
        ((string)result["absent"]![0]!["what"]!).ShouldBe("CodeSystem http://loinc.org");
    }

    [Fact]
    public async Task GivenAContentNotPresentStub_WhenLookedUp_ThenTheSystemIsNotLoadedAndTheStubIsCited()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode result = Json(await TermLookup.LookupAsync(OpenWithPackage(folder, store), Snomed, "22298006", null, CancellationToken.None));

        ((string)result["facts"]!["outcome"]!).ShouldBe("system-not-loaded");
        ((string)result["absent"]![0]!["checked"]!).ShouldContain("content 'not-present'");
        EvidenceFile(result, 0).ShouldEndWith("package/CodeSystem-snomed.json");
    }

    [Fact]
    public async Task GivenAFragment_WhenTheCodeIsMissing_ThenAbsenceIsNotAuthoritative()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode result = Json(await TermLookup.LookupAsync(OpenWithPackage(folder, store), Partial, "z", null, CancellationToken.None));

        ((string)result["facts"]!["outcome"]!).ShouldBe("code-not-in-loaded-content");
        ((string)result["absent"]![0]!["checked"]!).ShouldContain("may still exist");
    }

    [Fact]
    public async Task GivenAVersionThatIsNotLoaded_WhenLookedUp_ThenTheLoadedVersionsAreNamed()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode result = Json(await TermLookup.LookupAsync(OpenWithPackage(folder, store), Body, "heart", "9.9.9", CancellationToken.None));

        ((string)result["facts"]!["outcome"]!).ShouldBe("version-not-loaded");
        result["facts"]!["loadedVersions"]!.AsArray().Select(version => (string)version!).ShouldBe(["1.0.0"]);
    }

    [Fact]
    public async Task GivenMissingArguments_WhenLookedUp_ThenTheCallFailsWithInvalidParameter()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode result = Json(await TermLookup.LookupAsync(OpenWithPackage(folder, store), Body, " ", null, CancellationToken.None));

        result["facts"].ShouldBeNull();
        ((string)result["issues"]![0]!["code"]!).ShouldBe("invalid-parameter");
    }
}
