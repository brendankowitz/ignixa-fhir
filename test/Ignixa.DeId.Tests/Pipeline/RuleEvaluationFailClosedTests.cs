// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Immutable;
using Ignixa.DeId.Configuration;
using Ignixa.DeId.Models;
using Ignixa.DeId.Tests.Utilities;

namespace Ignixa.DeId.Tests.Pipeline;

/// <summary>
/// De-identification must fail closed: a rule that cannot be evaluated may never be skipped, because
/// the elements it targets would then be returned in clear as a success (GitHub #534).
/// </summary>
public class RuleEvaluationFailClosedTests
{
    private const string TwoGivenNamesPatient =
        """{"resourceType":"Patient","id":"p1","name":[{"family":"Doe","given":["D","Do"]}]}""";

    private const string OneGivenNamePatient =
        """{"resourceType":"Patient","id":"p2","name":[{"family":"Doe","given":["D"]}]}""";

    private const string DataDependentPath = "Patient.name.where(family.startsWith(%resource.name.given))";

    [Fact]
    public async Task GivenRegexRuleWithInvalidPattern_WhenDeidentifying_ThenOperationFailsAndNothingIsReturnedInClear()
    {
        // Regression: the swallowed exception left the name in clear with IsSuccess == true.
        var engine = DeIdTestHelpers.CreateEngine(RuleOptions("Patient.name.where(family.matches('['))"));

        var result = await engine.DeidentifyAsync(OneGivenNamePatient);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("RULE_EVALUATION_FAILED");
        result.Error.Message.ShouldContain("family.matches");
    }

    [Fact]
    public async Task GivenRulePathThatFailsToParse_WhenDeidentifying_ThenOperationFailsAndNothingIsReturnedInClear()
    {
        // Regression: a path that cannot parse was downgraded to a warning. Built by hand because the
        // loader now rejects it; this covers an options instance that reaches the engine unvalidated.
        var engine = DeIdTestHelpers.CreateEngine(RuleOptions("Patient.name.where(family = 99999999999999999999999999)"));

        var result = await engine.DeidentifyAsync(OneGivenNamePatient);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("RULE_EVALUATION_FAILED");
    }

    [Fact]
    public async Task GivenPathThatThrowsOnlyForSomeResources_WhenDeidentifyingAffectedResource_ThenOperationFails()
    {
        // Regression: the same rule redacted one resource and left another untouched with a success.
        var engine = DeIdTestHelpers.CreateEngine(RuleOptions(DataDependentPath));

        var result = await engine.DeidentifyAsync(TwoGivenNamesPatient);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("RULE_EVALUATION_FAILED");
    }

    [Fact]
    public async Task GivenPathThatThrowsOnlyForSomeResources_WhenDeidentifyingUnaffectedResource_ThenRuleIsApplied()
    {
        // Regression: at the base commit this rule redacted nothing even for data that does not trigger
        // the failure (the swallowed error left the name in clear), so it is not a mere guard.
        var engine = DeIdTestHelpers.CreateEngine(RuleOptions(DataDependentPath));

        var result = await engine.DeidentifyAsync(OneGivenNamePatient);

        result.IsSuccess.ShouldBeTrue();
        result.Value.DeidentifiedJson.ShouldNotContain("Doe");
    }

    [Fact]
    public async Task GivenRuleThatMatchesNothing_WhenDeidentifying_ThenOperationSucceedsWithoutWarnings()
    {
        // Guard: an absent element is legitimate and must stay distinguishable from an evaluation failure.
        var engine = DeIdTestHelpers.CreateEngine(RuleOptions("Patient.address"));

        var result = await engine.DeidentifyAsync(OneGivenNamePatient);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Warnings.ShouldBeEmpty();
        result.Value.DeidentifiedJson.ShouldContain("Doe");
    }

    [Fact]
    public async Task GivenGeneralizeCaseThatCannotBeEvaluated_WhenDeidentifyingWithDefaultErrorHandling_ThenOperationFails()
    {
        // Regression: the default StopOnError mode recorded a warning and returned the partially
        // de-identified resource as a success.
        var engine = DeIdTestHelpers.CreateEngine(GeneralizeOptions(errorHandling: null));

        var result = await engine.DeidentifyAsync(OneGivenNamePatient);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("PROCESSOR_ERROR");
    }

    [Fact]
    public async Task GivenGeneralizeCaseThatCannotBeEvaluated_WhenDeidentifyingWithFailFast_ThenOperationFails()
    {
        // Guard: FailFast already failed the operation.
        var engine = DeIdTestHelpers.CreateEngine(GeneralizeOptions(ErrorHandlingMode.FailFast));

        var result = await engine.DeidentifyAsync(OneGivenNamePatient);

        result.IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenGeneralizeCaseThatCannotBeEvaluated_WhenDeidentifyingWithLogAndContinue_ThenWarningsReachTheResult()
    {
        // Guard: LogAndContinue is an explicit opt-in, so the operation continues, but the failure
        // must be visible to the caller rather than only in logs.
        var engine = DeIdTestHelpers.CreateEngine(GeneralizeOptions(ErrorHandlingMode.LogAndContinue));

        var result = await engine.DeidentifyAsync(OneGivenNamePatient);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Warnings.ShouldHaveSingleItem().ShouldContain("Processor failed for rule");
    }

    [Fact]
    public async Task GivenGeneralizeCaseThatExceedsAnEvaluationLimit_WhenDeidentifyingWithLogAndContinue_ThenOperationFailsWithTheLimit()
    {
        // Regression: the limit was downgraded to a warning and the value returned unchanged as a success.
        var overlongPattern = new string('a', 4097);
        var engine = DeIdTestHelpers.CreateEngine(
            GeneralizeOptions(ErrorHandlingMode.LogAndContinue, $"$this.matches('{overlongPattern}')"));

        var result = await engine.DeidentifyAsync(OneGivenNamePatient);

        result.IsSuccess.ShouldBeFalse();
        result.Error.IsResourceLimit.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenRuleWithUnregisteredMethod_WhenDeidentifying_ThenOperationFails()
    {
        // Regression: the rule was skipped with a warning and the data returned in clear.
        var options = new DeIdOptions
        {
            FhirVersion = "R4",
            Rules = [new FhirPathRule { Path = "Patient.name", Method = "NOSUCHMETHOD" }]
        };
        var engine = DeIdTestHelpers.CreateEngine(options);

        var result = await engine.DeidentifyAsync(OneGivenNamePatient);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("PROCESSOR_NOT_FOUND");
    }

    private static DeIdOptions RuleOptions(string path) => new()
    {
        FhirVersion = "R4",
        Rules = [new FhirPathRule { Path = path, Method = "REDACT" }]
    };

    private static DeIdOptions GeneralizeOptions(ErrorHandlingMode? errorHandling, string casePredicate = "$this.matches('[')") => new()
    {
        FhirVersion = "R4",
        Rules =
        [
            new FhirPathRule
            {
                Path = "Patient.name.family",
                Method = "GENERALIZE",
                Settings = ImmutableDictionary<string, object>.Empty
                    .Add("cases", $$"""{"{{casePredicate}}":"'x'"}""")
                    .Add("otherValues", "keep")
            }
        ],
        Processing = errorHandling is null ? null : new ProcessingOptions { ErrorHandling = errorHandling.Value }
    };
}
