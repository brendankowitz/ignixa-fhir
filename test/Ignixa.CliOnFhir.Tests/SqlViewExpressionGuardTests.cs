// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Views;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;
using static Ignixa.CliOnFhir.Tests.ViewFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// ViewDefinitions that, unguarded, end the process with a stack overflow, which nothing can catch: each must come back as an error
/// envelope. (Run against a build without the guards, this class takes the test host down.)
/// </summary>
public class SqlViewExpressionGuardTests
{
    private static string Select(string body) => $$"""{"resourceType":"ViewDefinition","resource":"Patient","select":[{{body}}]}""";

    [Theory]
    [InlineData("$this")]
    [InlineData("  $this  ")]
    [InlineData("($this)")]
    [InlineData("( ( $this ) )")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GivenARepeatThatCannotMoveOn_WhenRun_ThenItIsRefusedAsAnInvalidView(string repeat)
    {
        using TestFolder folder = Clinic();
        string view = Select($$"""{"repeat":["{{repeat}}"],"column":[{"name":"x","path":"id"}]}""");

        IssueItem error = Error(await RunAsync(folder, new ViewRequest(view)));

        error.Code.ShouldBe("invalid-view");
        error.Diagnostics.ShouldContain("select[0].repeat[0]");
        error.Diagnostics.ShouldContain("does not move on");
    }

    [Fact]
    public async Task GivenARepeatInANestedSelect_WhenItCannotMoveOn_ThenItIsFoundThere()
    {
        using TestFolder folder = Clinic();
        string view = Select("""{"column":[{"name":"x","path":"id"}],"select":[{"repeat":["name","$this"],"column":[{"name":"y","path":"id"}]}]}""");

        IssueItem error = Error(await RunAsync(folder, new ViewRequest(view)));

        error.Code.ShouldBe("invalid-view");
        error.Diagnostics.ShouldContain("select[0].select[0].repeat[1]");
    }

    [Fact]
    public async Task GivenARepeatThatNavigates_WhenRun_ThenItIsAccepted()
    {
        using TestFolder folder = Clinic();
        string view = Select("""{"repeat":["extension"],"column":[{"name":"x","path":"id"}]}""");

        Facts(await RunAsync(folder, new ViewRequest(view))).Total.ShouldBe(0);
    }

    [Theory]
    [InlineData(600)]
    [InlineData(65)]
    public async Task GivenAPathNestedTooDeep_WhenRun_ThenItIsRefusedAsAnInvalidView(int depth)
    {
        using TestFolder folder = Clinic();
        string path = new string('(', depth) + "id" + new string(')', depth);
        string view = Select($$"""{"column":[{"name":"x","path":"{{path}}"}]}""");

        IssueItem error = Error(await RunAsync(folder, new ViewRequest(view)));

        error.Code.ShouldBe("invalid-view");
        error.Diagnostics.ShouldContain("select[0].column[0].path");
        error.Diagnostics.ShouldContain("nested more than 64 levels");
    }

    [Fact]
    public async Task GivenAPathAtTheDepthLimit_WhenRun_ThenItIsAccepted()
    {
        using TestFolder folder = Clinic();
        string path = new string('(', 64) + "id" + new string(')', 64);
        string view = Select($$"""{"column":[{"name":"x","path":"{{path}}"}]}""");

        Facts(await RunAsync(folder, new ViewRequest(view))).Total.ShouldBe(3);
    }

    [Fact]
    public async Task GivenBracketsInsideStringLiterals_WhenRun_ThenTheyAreNotCountedAsNesting()
    {
        using TestFolder folder = Clinic();
        string literal = new string('(', 200) + new string('[', 200);
        string view = Select($$"""{"column":[{"name":"x","path":"'{{literal}}'"},{"name":"y","path":"`{{literal}}`.exists()"}]}""");

        // The second column is not a name that exists: it is the lexical guard that is asked, not the answer.
        ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest(view));

        envelope.Issues.Where(issue => issue.Code == "invalid-view").Select(issue => issue.Diagnostics).ShouldNotContain(text => text.Contains("nested", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GivenAnExpressionLongerThanTheLimit_WhenRun_ThenItIsRefusedWhereverItIs()
    {
        using TestFolder folder = Clinic();
        string longPath = string.Join(" and ", Enumerable.Repeat("gender.exists()", 400));
        string[] views =
        [
            Select($$"""{"column":[{"name":"x","path":"{{longPath}}"}]}"""),
            $$"""{"resourceType":"ViewDefinition","resource":"Patient","where":[{"path":"{{longPath}}"}],"select":[{"column":[{"name":"x","path":"id"}]}]}""",
            Select($$"""{"forEach":"{{longPath}}","column":[{"name":"x","path":"id"}]}"""),
            Select($$"""{"column":[{"name":"x","path":"id"}],"unionAll":[{"column":[{"name":"x","path":"{{longPath}}"}]}]}"""),
        ];

        foreach (string view in views)
        {
            IssueItem error = Error(await RunAsync(folder, new ViewRequest(view)));
            error.Code.ShouldBe("invalid-view");
            error.Diagnostics.ShouldContain("longer than 4096 characters");
        }
    }

    [Fact]
    public async Task GivenAnExtraWhereThatIsTooLongOrTooDeep_WhenRun_ThenItIsAnInvalidParameter()
    {
        using TestFolder folder = Clinic();

        foreach (string where in new[] { new string('(', 600) + "true" + new string(')', 600), string.Join(" and ", Enumerable.Repeat("gender.exists()", 400)) })
        {
            Error(await RunAsync(folder, new ViewRequest(PatientView, Where: where))).Code.ShouldBe("invalid-parameter");
        }
    }
}
