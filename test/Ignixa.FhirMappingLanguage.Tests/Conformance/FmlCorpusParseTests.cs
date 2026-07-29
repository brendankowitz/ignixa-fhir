/*
 * Copyright (c) 2025, Ignixa Contributors
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ignixa.FhirMappingLanguage.Parser;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Ignixa.FhirMappingLanguage.Tests.Conformance;

/// <summary>
/// Parses every .map in the official HL7 structure-mapping corpus and asserts both the
/// total file count (guarding against a silent missing corpus) and the exact set of
/// failing file names (guarding against silent regressions and silent fixes cancelling out).
///
/// The expected-failure set is a ratchet: removing an entry is a feature, adding one is a
/// regression. The count assertion catches the "vacuous pass" failure mode where the glob
/// finds zero files and zero failures look like success.
/// </summary>
public class FmlCorpusParseTests(ITestOutputHelper output)
{
    /// <summary>
    /// Theory data: version, expected total .map files, expected failing file names (sorted).
    /// Keep entries sorted by file name (StringComparer.Ordinal) and document each with a reason.
    ///
    /// Failure classifications:
    ///   (a) real Part A gap — a construct Tasks 1–5 were supposed to handle but did not
    ///   (b) explicitly deferred — known out-of-scope construct
    ///   (c) other — new/unknown failure mode
    ///
    /// All current failures are classification (a): the parser does not handle parenthesized
    /// expressions `(expr)` when they appear as target-value assignments (e.g. `tgt.x = (expr)`)
    /// or as arguments to transform function calls (e.g. `evaluate(src, iif(...))`).
    /// This is a single grammar gap not addressed by any of the five Part A fixes.
    /// Root cause: "unexpected leftparen `(`, expected …" in target-value or transform position.
    /// </summary>
    public static TheoryData<string, int, string[]> CorpusTheoryData() => new()
    {
        // r5: 15 .map files measured in vendored release 1.7.46.
        // parsed: 9/15
        // Failures — all (a) parenthesized-expression gap:
        //   qr2cda-eval.map        line 7  col 50  title.data= evaluate(src, iif(src.is(QuestionnaireResponse),"Hello CDA","badbadbad")) "eval"
        //   qr2pat-gender.map      line 11 col 74  tgt.gender = (item.answer.valueString)
        //   qr2pat-humannameshared.map line 14 col 71  tgt.gender = (item.answer.valueString)
        //   qr2pat-humannametwice.map  line 14 col 71  tgt.gender = (item.answer.valueString)
        //   qr2patfordates.map     line 9  col 42  tgt.birthDate = (%value + 5 days) "plus"
        //   syntax.map             line 17 col 45  ext.system = ('urn:uuid:' + r.lower()) "rootuuid"
        {
            "r5", 15,
            [
                "qr2cda-eval.map",
                "qr2pat-gender.map",
                "qr2pat-humannameshared.map",
                "qr2pat-humannametwice.map",
                "qr2patfordates.map",
                "syntax.map",
            ]
        },

        // r4b: 12 .map files measured in vendored release 1.7.46.
        // parsed: 7/12
        // Failures — all (a) parenthesized-expression gap:
        //   qr2cda-eval.map        line 7  col 50  title.data= evaluate(src, iif(src.is(QuestionnaireResponse),"Hello CDA","badbadbad")) "eval"
        //   qr2pat-gender.map      line 11 col 74  tgt.gender = (item.answer.valueString)
        //   qr2pat-humannameshared.map line 14 col 71  tgt.gender = (item.answer.valueString)
        //   qr2pat-humannametwice.map  line 14 col 71  tgt.gender = (item.answer.valueString)
        //   syntax.map             line 18 col 45  ext.system = ('urn:uuid:' + r.lower()) "rootuuid"
        {
            "r4b", 12,
            [
                "qr2cda-eval.map",
                "qr2pat-gender.map",
                "qr2pat-humannameshared.map",
                "qr2pat-humannametwice.map",
                "syntax.map",
            ]
        },
    };

    [Theory]
    [MemberData(nameof(CorpusTheoryData))]
    public void GivenTheOfficialCorpus_WhenParsingEveryMap_ThenOnlyExpectedFilesFail(
        string version,
        int expectedTotalFiles,
        string[] expectedFailingFileNames)
    {
        var directory = FmlTestCasesLocator.StructureMappingDirectory(version);
        var files = Directory.GetFiles(directory, "*.map")
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        // Guard against the "vacuous pass" failure mode: a glob that matches nothing
        // produces 0 files, 0 failures, and a spuriously green test.
        files.Count.ShouldBe(
            expectedTotalFiles,
            $"{version}: expected {expectedTotalFiles} .map files in corpus but found {files.Count}");

        var failureNames = new List<string>();
        var failureDetails = new List<string>();
        var parsed = 0;

        foreach (var file in files)
        {
            try
            {
                new MappingParser().Parse(File.ReadAllText(file, Encoding.UTF8));
                parsed++;
            }
            catch (Exception ex)
            {
                var name = Path.GetFileName(file);
                failureNames.Add(name);
                failureDetails.Add($"  FAIL {name}: {ex.Message}");
            }
        }

        var report = new StringBuilder();
        report.AppendLine($"{version}: parsed {parsed}/{files.Count}");
        foreach (var detail in failureDetails)
        {
            report.AppendLine(detail);
        }

        output.WriteLine(report.ToString());

        // Assert the exact set of failing file names (sorted). This detects both new
        // failures and silent swaps where a fixed file cancels out a newly broken one.
        var actualSorted = failureNames.OrderBy(n => n, StringComparer.Ordinal).ToArray();
        var expectedSorted = expectedFailingFileNames.OrderBy(n => n, StringComparer.Ordinal).ToArray();

        actualSorted.ShouldBe(expectedSorted, report.ToString());
    }
}
