// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.DeId;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.DeIdFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>A configuration is checked before anything is read, and no value of it is ever repeated.</summary>
public class DeIdConfigurationTests
{
    private const string Secret = "zq-secret-7731-value";

    public static TheoryData<string, string, string> Refused => new()
    {
        { $$$"""{"fhirPathRules":[{"path":"Patient.name","method":"{{{Secret}}}"}]}""", "deid-config-invalid", "names a method Ignixa.DeId does not have" },
        { """{"fhirPathRules":[{"method":"redact"}]}""", "deid-config-invalid", "fhirPathRules[0] must be an object with a text 'path'" },
        { """{"fhirPathRules":[{"path":"Patient.name","method":"redact"},{"path":"","method":"redact"}]}""", "deid-config-invalid", "fhirPathRules[1]" },
        { """{"fhirPathRules":[]}""", "deid-config-invalid", "no 'fhirPathRules'" },
        { """{"fhirPathRules":[{"path":"Patient.id","method":"cryptoHash"}]}""", "deid-config-key-missing", "parameters.cryptoHashKey" },
        { """{"fhirPathRules":[{"path":"Patient.birthDate","method":"dateShift"}],"parameters":{"dateShiftKey":"  "}}""", "deid-config-key-missing", "parameters.dateShiftKey" },
        { """{"fhirPathRules":[{"path":"Patient.gender","method":"encrypt"}]}""", "deid-config-key-missing", "parameters.encryptKey" },
        { $$$"""{"fhirPathRules":[{"path":"Patient.gender","method":"encrypt"}],"parameters":{"encryptKey":"{{{Secret}}}"}}""", "deid-config-invalid", "16, 24 or 32 bytes" },
        { $$$"""{"fhirPathRules":[{"path":"Patient.name.where({{{Secret}}} = ","method":"redact"}]}""", "deid-config-invalid", "is not valid FHIRPath" },
        { """{"fhirPathRules":[{"path":"Patient.gender","method":"generalize"}]}""", "deid-config-invalid", "needs 'cases'" },
        { $$$"""{"fhirPathRules":[{"path":"Patient.name","method":"redact"}],"parameters":{"cryptoHashKey":"{{{Secret}}}"} x""", "deid-config-invalid", "not valid JSON at line 1" },
        { $$$$"""{"fhirPathRules":[{"path":"Patient.name","method":"redact"}],"parameters":{"cryptoHashKey":{"k":"{{{{Secret}}}}"}}}""", "deid-config-invalid", "could not read the configuration" },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task GivenAConfigurationThatCannotBeApplied_WhenRun_ThenItIsRefusedWithoutRepeatingIt(string config, string code, string says)
    {
        using TestFolder root = Root();

        ToolEnvelope<DeIdFacts?> envelope = await RunAsync(Data(root), new DeIdRequest(config, Out(root)));

        IssueItem error = Error(envelope);
        error.Code.ShouldBe(code);
        error.Diagnostics.ShouldContain(says);
        Json(envelope).ShouldNotContain(Secret);
        Directory.Exists(Out(root)).ShouldBeFalse();
    }

    // A stack overflow cannot be caught: a path nested deeper than any rule needs is refused before the FHIRPath parser sees it.
    [Theory]
    [InlineData("path")]
    [InlineData("case")]
    public async Task GivenAnExpressionNestedThousandsDeep_WhenRun_ThenItIsRefusedBeforeItIsParsed(string where)
    {
        using TestFolder root = Root();
        string deep = new string('(', 5000) + "true" + new string(')', 5000);
        string config = where == "path"
            ? $$$"""{"fhirPathRules":[{"path":"Patient.name.where({{{deep}}})","method":"redact"}]}"""
            : $$$"""{"fhirPathRules":[{"path":"Patient.gender","method":"generalize","cases":{"{{{deep}}}":"'x'"}}]}""";

        IssueItem error = Error(await RunAsync(Data(root), new DeIdRequest(config, Out(root))));

        error.Code.ShouldBe("deid-config-invalid");
        error.Diagnostics.ShouldContain("longer than 4096 characters");
    }

    [Fact]
    public async Task GivenAnExpressionNestedMoreThan64Deep_WhenRun_ThenItIsRefused()
    {
        using TestFolder root = Root();
        string deep = new string('(', 65) + "true" + new string(')', 65);

        IssueItem error = Error(await RunAsync(Data(root), new DeIdRequest($$$"""{"fhirPathRules":[{"path":"Patient.name.where({{{deep}}})","method":"redact"}]}""", Out(root))));

        error.Code.ShouldBe("deid-config-invalid");
        error.Diagnostics.ShouldContain("nested more than 64 levels deep");
    }

    [Fact]
    public async Task GivenAConfigurationForAnotherVersion_WhenRun_ThenAWarningSaysSo()
    {
        using TestFolder root = Root();

        ToolEnvelope<DeIdFacts?> envelope = await RunAsync(Data(root), new DeIdRequest("""{"fhirVersion":"R5","fhirPathRules":[{"path":"Patient.name","method":"redact"}]}""", Out(root)));

        Facts(envelope);
        envelope.Issues.ShouldContain(issue => issue.Code == "deid-config-version" && issue.Severity == "warning");
    }

    [Fact]
    public async Task GivenPerturb_WhenRun_ThenANoteSaysTheOutputDiffersOnEveryRun()
    {
        using TestFolder root = Root();

        ToolEnvelope<DeIdFacts?> envelope = await RunAsync(Data(root), new DeIdRequest("""{"fhirPathRules":[{"path":"Observation.value","method":"perturb","span":1}]}""", Out(root)));

        Facts(envelope);
        envelope.Issues.ShouldContain(issue => issue.Code == "deid-not-reproducible");
    }

    [Fact]
    public async Task GivenAMissingConfigurationFile_WhenRun_ThenItIsUnreadable()
    {
        using TestFolder root = Root();

        Error(await RunAsync(Data(root), new DeIdRequest(Path.Combine(root.Path, "missing.json"), Out(root)))).Code.ShouldBe("deid-config-unreadable");
    }
}
