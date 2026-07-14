using System.Text.Json.Nodes;
using Ignixa.TestScript.Reporting;

namespace Ignixa.TestScript.Tests.Reporting;

public class TestReportResourceGeneratorTests
{
    [Fact]
    public void GivenPassingReport_WhenGenerating_ThenProducesValidTestReport()
    {
        var report = new TestScriptReport
        {
            TestScriptName = "ReadPatientTest",
            StartTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 1, 1, 0, 0, 1, TimeSpan.Zero),
            TestResults =
            [
                new TestCaseResult("ReadPatient", "Read a patient", [
                    new ActionResult("read", "Read Patient", TestScriptOutcome.Pass),
                    new ActionResult("assert-status", "Check 200", TestScriptOutcome.Pass)
                ], TestScriptOutcome.Pass)
            ]
        };

        var json = TestReportResourceGenerator.Generate(report);

        json.ShouldNotBeNull();
        json["resourceType"]?.GetValue<string>().ShouldBe("TestReport");
        json["result"]?.GetValue<string>().ShouldBe("pass");
        json["name"]?.GetValue<string>().ShouldBe("ReadPatientTest");
        json["test"]?.AsArray().Count.ShouldBe(1);
    }

    [Fact]
    public void GivenNoContext_WhenGenerating_ThenTestScriptFallsBackToScriptName()
    {
        // Arrange: testScript is 1..1 in R4, so it must be present even with nothing supplied.
        var report = new TestScriptReport
        {
            TestScriptName = "ReadPatientTest",
            StartTime = DateTimeOffset.UnixEpoch,
            EndTime = DateTimeOffset.UnixEpoch
        };

        // Act
        var json = TestReportResourceGenerator.Generate(report);

        // Assert
        json["testScript"]!["display"]!.GetValue<string>().ShouldBe("ReadPatientTest");
        json["testScript"]!["reference"].ShouldBeNull();
    }

    [Fact]
    public void GivenContext_WhenGenerating_ThenEmitsTesterParticipantAndTestScriptDisplay()
    {
        // Arrange
        var report = new TestScriptReport
        {
            TestScriptName = "IntervalSearch",
            StartTime = DateTimeOffset.UnixEpoch,
            EndTime = DateTimeOffset.UnixEpoch
        };
        var context = new TestReportContext
        {
            Tester = "my-server",
            ServerUri = new Uri("https://example.org/fhir"),
            TestScriptDisplay = "Search/intervals.json"
        };

        // Act
        var json = TestReportResourceGenerator.Generate(report, context);

        // Assert
        json["tester"]!.GetValue<string>().ShouldBe("my-server");
        json["testScript"]!["display"]!.GetValue<string>().ShouldBe("Search/intervals.json");

        var participants = json["participant"]!.AsArray();
        participants[0]!["type"]!.GetValue<string>().ShouldBe("server");
        participants[0]!["uri"]!.GetValue<string>().ShouldBe("https://example.org/fhir");
        participants[1]!["type"]!.GetValue<string>().ShouldBe("test-engine");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GivenBlankTestScriptDisplay_WhenGenerating_ThenFallsBackToScriptNameRatherThanEmittingEmptyString(string display)
    {
        // Arrange: FHIR forbids empty strings, so a blank display must not reach the resource.
        var report = new TestScriptReport
        {
            TestScriptName = "ReadPatientTest",
            StartTime = DateTimeOffset.UnixEpoch,
            EndTime = DateTimeOffset.UnixEpoch
        };

        // Act
        var json = TestReportResourceGenerator.Generate(report, new TestReportContext { TestScriptDisplay = display });

        // Assert
        json["testScript"]!["display"]!.GetValue<string>().ShouldBe("ReadPatientTest");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GivenBlankTester_WhenGenerating_ThenOmitsTesterAndServerParticipant(string blank)
    {
        // Arrange
        var report = new TestScriptReport
        {
            TestScriptName = "Blank",
            StartTime = DateTimeOffset.UnixEpoch,
            EndTime = DateTimeOffset.UnixEpoch
        };

        // Act
        var json = TestReportResourceGenerator.Generate(report, new TestReportContext { Tester = blank });

        // Assert
        json["tester"].ShouldBeNull();
        json["participant"]!.AsArray().Count.ShouldBe(1);
    }

    [Fact]
    public void GivenContextCopiedWithNewValue_WhenUsingWithExpression_ThenStillNormalizes()
    {
        // Arrange: the init accessors carry the normalization, so a 'with' copy must re-run them
        // rather than smuggling a blank value straight into the backing field.
        var context = new TestReportContext { Tester = "original" };

        // Act
        var copied = context with { Tester = "  spaced  ", TestScriptDisplay = "   " };

        // Assert
        copied.Tester.ShouldBe("spaced");
        copied.TestScriptDisplay.ShouldBeNull();
    }

    [Fact]
    public void GivenContextCopiedWithNewServerUri_WhenUsingWithExpression_ThenNewValueIsUsed()
    {
        // Arrange: ServerUri is a reference type with no normalization, but a 'with' copy must
        // still carry the replaced value through like the string members above.
        var context = new TestReportContext { ServerUri = new Uri("https://a.example.org") };

        // Act
        var copied = context with { ServerUri = new Uri("https://b.example.org") };

        // Assert
        copied.ServerUri.ShouldBe(new Uri("https://b.example.org"));
    }

    [Fact]
    public void GivenContextsDifferingOnlyByBlankRepresentation_WhenComparing_ThenTheyAreEqual()
    {
        // Arrange: "" and null both mean absent, so they must not produce unequal contexts.
        var empty = new TestReportContext { Tester = "", ServerDisplay = "" };
        var nulled = new TestReportContext { Tester = null, ServerDisplay = null };

        // Assert
        empty.ShouldBe(nulled);
    }

    [Fact]
    public void GivenRelativeServerUri_WhenConstructingContext_ThenThrowsArgumentException()
    {
        // Arrange: TestReport.participant.uri must be absolute; a relative Uri is a programmer
        // error at this library boundary and must fail fast rather than emit an invalid resource.
        var relativeUri = new Uri("foo", UriKind.Relative);

        // Act
        var act = () => new TestReportContext { ServerUri = relativeUri };

        // Assert
        Should.Throw<ArgumentException>(act);
    }

    [Fact]
    public void GivenAbsoluteServerUri_WhenGenerating_ThenEmitsUriStringOnServerParticipant()
    {
        // Arrange
        var report = new TestScriptReport
        {
            TestScriptName = "ServerUriTest",
            StartTime = DateTimeOffset.UnixEpoch,
            EndTime = DateTimeOffset.UnixEpoch
        };
        var context = new TestReportContext { ServerUri = new Uri("https://fhir.example.org/r4") };

        // Act
        var json = TestReportResourceGenerator.Generate(report, context);

        // Assert
        var participants = json["participant"]!.AsArray();
        participants[0]!["type"]!.GetValue<string>().ShouldBe("server");
        participants[0]!["uri"]!.GetValue<string>().ShouldBe("https://fhir.example.org/r4");
    }

    [Fact]
    public void GivenServerDisplay_WhenGenerating_ThenServerParticipantCarriesDisplay()
    {
        // Arrange
        var report = new TestScriptReport
        {
            TestScriptName = "ServerDisplayTest",
            StartTime = DateTimeOffset.UnixEpoch,
            EndTime = DateTimeOffset.UnixEpoch
        };
        var context = new TestReportContext
        {
            ServerUri = new Uri("https://fhir.example.org"),
            ServerDisplay = "Reference Server"
        };

        // Act
        var json = TestReportResourceGenerator.Generate(report, context);

        // Assert
        var server = json["participant"]!.AsArray()[0]!.AsObject();
        server["display"]!.GetValue<string>().ShouldBe("Reference Server");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GivenBlankOrNullServerDisplay_WhenGenerating_ThenServerParticipantHasNoDisplayKey(string? blank)
    {
        // Arrange: FHIR JSON permits null only inside arrays; a null object property is invalid,
        // so an absent display must be omitted rather than emitted as null.
        var report = new TestScriptReport
        {
            TestScriptName = "NoServerDisplayTest",
            StartTime = DateTimeOffset.UnixEpoch,
            EndTime = DateTimeOffset.UnixEpoch
        };
        var context = new TestReportContext
        {
            ServerUri = new Uri("https://fhir.example.org"),
            ServerDisplay = blank
        };

        // Act
        var json = TestReportResourceGenerator.Generate(report, context);

        // Assert
        var server = json["participant"]!.AsArray()[0]!.AsObject();
        server.ContainsKey("display").ShouldBeFalse();
    }

    [Fact]
    public void GivenPaddedContextValues_WhenGenerating_ThenTrimsThem()
    {
        // Arrange
        var report = new TestScriptReport
        {
            TestScriptName = "Padded",
            StartTime = DateTimeOffset.UnixEpoch,
            EndTime = DateTimeOffset.UnixEpoch
        };

        // Act
        var json = TestReportResourceGenerator.Generate(report, new TestReportContext { Tester = "  my-server  " });

        // Assert
        json["tester"]!.GetValue<string>().ShouldBe("my-server");
    }

    [Fact]
    public void GivenNoServerUri_WhenGenerating_ThenOmitsServerParticipant()
    {
        // Arrange: participant.uri is 1..1, so a server entry with no URI must not be emitted.
        var report = new TestScriptReport
        {
            TestScriptName = "NoServer",
            StartTime = DateTimeOffset.UnixEpoch,
            EndTime = DateTimeOffset.UnixEpoch
        };

        // Act
        var json = TestReportResourceGenerator.Generate(report);

        // Assert
        var participants = json["participant"]!.AsArray();
        participants.Count.ShouldBe(1);
        participants[0]!["type"]!.GetValue<string>().ShouldBe("test-engine");
    }

    [Fact]
    public void GivenMixedOutcomes_WhenGenerating_ThenScoreExcludesSkippedTestsFromTheDenominator()
    {
        // Arrange: 1 pass + 1 warning (a passing outcome) out of 3 attempted => 67. The skipped
        // test was never run, so scoring it as a miss would understate the server.
        var report = new TestScriptReport
        {
            TestScriptName = "Mixed",
            StartTime = DateTimeOffset.UnixEpoch,
            EndTime = DateTimeOffset.UnixEpoch,
            TestResults =
            [
                new TestCaseResult("A", null, [], TestScriptOutcome.Pass),
                new TestCaseResult("B", null, [], TestScriptOutcome.Warning),
                new TestCaseResult("C", null, [], TestScriptOutcome.Fail),
                new TestCaseResult("D", null, [], TestScriptOutcome.Skip)
            ]
        };

        // Act
        var json = TestReportResourceGenerator.Generate(report);

        // Assert
        json["score"]!.GetValue<double>().ShouldBe(67);
    }

    [Fact]
    public void GivenEveryTestSkipped_WhenGenerating_ThenOmitsScoreRatherThanContradictingResult()
    {
        // Arrange: version- or capability-gated scripts skip every test. OverallOutcome has no Skip
        // branch, so result is "pass" — emitting score 0 alongside it would contradict the resource.
        var report = new TestScriptReport
        {
            TestScriptName = "AllSkipped",
            StartTime = DateTimeOffset.UnixEpoch,
            EndTime = DateTimeOffset.UnixEpoch,
            TestResults =
            [
                new TestCaseResult("A", null, [], TestScriptOutcome.Skip),
                new TestCaseResult("B", null, [], TestScriptOutcome.Skip)
            ]
        };

        // Act
        var json = TestReportResourceGenerator.Generate(report);

        // Assert
        json["result"]!.GetValue<string>().ShouldBe("pass");
        json["score"].ShouldBeNull();
    }

    [Fact]
    public void GivenNoTests_WhenGenerating_ThenOmitsScoreRatherThanDividingByZero()
    {
        // Arrange
        var report = new TestScriptReport
        {
            TestScriptName = "Empty",
            StartTime = DateTimeOffset.UnixEpoch,
            EndTime = DateTimeOffset.UnixEpoch
        };

        // Act
        var json = TestReportResourceGenerator.Generate(report);

        // Assert
        json["score"].ShouldBeNull();
    }

    [Fact]
    public void GivenTestWithoutDescription_WhenGenerating_ThenOmitsItRatherThanEmittingJsonNull()
    {
        // Arrange: FHIR JSON permits null only inside arrays; a null object property is invalid.
        var report = new TestScriptReport
        {
            TestScriptName = "NoDescription",
            StartTime = DateTimeOffset.UnixEpoch,
            EndTime = DateTimeOffset.UnixEpoch,
            TestResults = [new TestCaseResult("A", null, [], TestScriptOutcome.Pass)]
        };

        // Act
        var json = TestReportResourceGenerator.Generate(report);

        // Assert
        var test = json["test"]!.AsArray()[0]!.AsObject();
        test.ContainsKey("description").ShouldBeFalse();
        test["name"]!.GetValue<string>().ShouldBe("A");
    }

    [Fact]
    public void GivenFailingReport_WhenGenerating_ThenResultIsFail()
    {
        var report = new TestScriptReport
        {
            TestScriptName = "FailTest",
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow,
            TestResults =
            [
                new TestCaseResult("FailingTest", null, [
                    new ActionResult(null, null, TestScriptOutcome.Fail, "Expected 200 got 404")
                ], TestScriptOutcome.Fail)
            ]
        };

        var json = TestReportResourceGenerator.Generate(report);

        json["result"]?.GetValue<string>().ShouldBe("fail");
    }

    [Fact]
    public void GivenWarningAction_WhenGenerating_ThenActionResultIsWarning()
    {
        var report = new TestScriptReport
        {
            TestScriptName = "WarnTest",
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow,
            TestResults =
            [
                new TestCaseResult("WarnCase", null, [
                    new ActionResult("a", null, TestScriptOutcome.Warning, "soft fail")
                ], TestScriptOutcome.Warning)
            ]
        };

        var json = TestReportResourceGenerator.Generate(report);

        var actionResult = json["test"]!.AsArray()[0]!["action"]!.AsArray()[0]!["result"]!.GetValue<string>();
        actionResult.ShouldBe("warning");
    }

    [Fact]
    public void GivenWarningOverall_WhenGenerating_ThenReportResultIsPass()
    {
        var report = new TestScriptReport
        {
            TestScriptName = "WarnReport",
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow,
            TeardownResult = new TestPhaseResult([], TestScriptOutcome.Error)
        };

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Warning);

        var json = TestReportResourceGenerator.Generate(report);

        json["result"]?.GetValue<string>().ShouldBe("pass");
    }

    [Fact]
    public void GivenErrorReport_WhenGenerating_ThenReportResultIsFailNotError()
    {
        var report = new TestScriptReport
        {
            TestScriptName = "ErrorReport",
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow,
            TestResults =
            [
                new TestCaseResult("Boom", null, [
                    new ActionResult(null, null, TestScriptOutcome.Error, "engine bug")
                ], TestScriptOutcome.Error)
            ]
        };

        var json = TestReportResourceGenerator.Generate(report);

        json["result"]?.GetValue<string>().ShouldBe("fail");
        var actionResult = json["test"]!.AsArray()[0]!["action"]!.AsArray()[0]!["result"]!.GetValue<string>();
        actionResult.ShouldBe("error");
    }

    [Fact]
    public void GivenSkippedTestAction_WhenGenerating_ThenActionResultIsSkip()
    {
        var report = new TestScriptReport
        {
            TestScriptName = "SkippedReport",
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow,
            SetupResult = new TestPhaseResult([], TestScriptOutcome.Pass),
            TestResults =
            [
                new TestCaseResult("Skipped", null, [
                    new ActionResult(null, null, TestScriptOutcome.Skip, "version mismatch")
                ], TestScriptOutcome.Skip)
            ]
        };

        var json = TestReportResourceGenerator.Generate(report);

        var actionResult = json["test"]!.AsArray()[0]!["action"]!.AsArray()[0]!["result"]!.GetValue<string>();
        actionResult.ShouldBe("skip");
    }

    [Fact]
    public void GivenGroupActionWithMembers_WhenGenerating_ThenMembersRenderAsChildExtensions()
    {
        var report = new TestScriptReport
        {
            TestScriptName = "GroupReport",
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow,
            TestResults =
            [
                new TestCaseResult("DeletedResourceReadback", null, [
                    new ActionResult("grp", "Deleted resource readback", TestScriptOutcome.Pass,
                        "assertionAnyOfGroup 'grp': matched alternative 'Alternative: 404 Not Found'",
                        GroupId: "grp",
                        Members:
                        [
                            new AssertionGroupMemberResult("Preferred: 410 Gone", true, false, "Expected response 'gone' but got status 404"),
                            new AssertionGroupMemberResult("Alternative: 404 Not Found", true, true, null)
                        ])
                ], TestScriptOutcome.Pass)
            ]
        };

        var json = TestReportResourceGenerator.Generate(report);

        var action = json["test"]!.AsArray()[0]!["action"]!.AsArray()[0]!;
        action["result"]!.GetValue<string>().ShouldBe("pass");
        var extensions = action["extension"]!.AsArray();
        extensions.Count.ShouldBe(3);
        extensions[0]!["url"]!.GetValue<string>().ShouldBe("http://ignixa.io/testscript/assertionAnyOfGroup");
        extensions[0]!["valueString"]!.GetValue<string>().ShouldBe("grp");
        extensions[1]!["url"]!.GetValue<string>().ShouldBe("http://ignixa.io/testscript/assertionGroupMember");
        var firstChildren = extensions[1]!["extension"]!.AsArray();
        firstChildren.Any(c => c!["url"]!.GetValue<string>() == "passed" && c["valueBoolean"]!.GetValue<bool>() == false)
            .ShouldBeTrue();
        extensions[2]!["extension"]!.AsArray()
            .Any(c => c!["url"]!.GetValue<string>() == "passed" && c["valueBoolean"]!.GetValue<bool>() == true)
            .ShouldBeTrue();
    }

    [Fact]
    public void GivenActionWithoutMembers_WhenGenerating_ThenNoExtensionEmitted()
    {
        var report = new TestScriptReport
        {
            TestScriptName = "PlainReport",
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow,
            TestResults =
            [
                new TestCaseResult("Plain", null, [
                    new ActionResult("a", null, TestScriptOutcome.Pass)
                ], TestScriptOutcome.Pass)
            ]
        };

        var json = TestReportResourceGenerator.Generate(report);

        var action = json["test"]!.AsArray()[0]!["action"]!.AsArray()[0]!;
        action.AsObject().ContainsKey("extension").ShouldBeFalse();
    }
}
