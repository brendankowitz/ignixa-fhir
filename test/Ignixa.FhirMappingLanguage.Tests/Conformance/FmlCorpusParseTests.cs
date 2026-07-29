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
    /// qr2cda-eval.map is classification (b): it uses nested function calls of the form
    /// evaluate(src, iif(src.is(X),"a","b")) as a transform. The inner iif() is a
    /// TransformArgumentExpression which the greedy FhirPathExpression fallback cannot safely
    /// expand without a parenthesised-only guard — and the RightParen that closes iif() is
    /// not in the fallback terminator set. This construct is explicitly deferred: the file
    /// produces XML-format CDA output and is excluded from the end-to-end oracle.
    /// </summary>
    public static TheoryData<string, int, string[]> CorpusTheoryData() => new()
    {
        // r5: 15 .map files measured in vendored release 1.7.46.
        // parsed: 14/15
        // Remaining failure — classification (b) explicitly deferred:
        //   qr2cda-eval.map: evaluate(src, iif(src.is(QuestionnaireResponse),"Hello CDA","badbadbad"))
        //   nested iif() inside evaluate() — greedy-swallow hazard; CDA output excluded from oracle.
        {
            "r5", 15,
            [
                "qr2cda-eval.map",
            ]
        },

        // r4b: 12 .map files measured in vendored release 1.7.46.
        // parsed: 11/12
        // Remaining failure — classification (b) explicitly deferred:
        //   qr2cda-eval.map: same nested evaluate/iif as r5 variant.
        {
            "r4b", 12,
            [
                "qr2cda-eval.map",
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
