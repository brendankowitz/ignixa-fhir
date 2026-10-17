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
    public async Task GivenBracketsInsideAShortStringLiteral_WhenRun_ThenTheyAreNotAProblem()
    {
        using TestFolder folder = Clinic();
        string literal = new string('(', 30) + new string('[', 30);
        string view = Select($$"""{"column":[{"name":"x","path":"'{{literal}}'"}]}""");

        Facts(await RunAsync(folder, new ViewRequest(view))).Total.ShouldBe(3);
    }

    // Every bracket counts, quoted or not: a literal of more than 64 unbalanced brackets is refused too, which is the documented price
    // of a count that cannot be switched off by a quote or backtick inside a FHIRPath comment.
    [Fact]
    public async Task GivenMoreThanTheLimitOfBracketsInAStringLiteral_WhenRun_ThenItIsRefusedAllTheSame()
    {
        using TestFolder folder = Clinic();
        string view = Select($$"""{"column":[{"name":"x","path":"'{{new string('(', 200)}}'"}]}""");

        IssueItem error = Error(await RunAsync(folder, new ViewRequest(view)));

        error.Code.ShouldBe("invalid-view");
        error.Diagnostics.ShouldContain("nested more than 64 levels");
    }

    // A quote or backtick inside a comment is nothing to FHIRPath; counting that skipped quoted text stopped at it.
    public static TheoryData<string, string> CommentBypasses()
    {
        string deep = new string('(', 1500) + "id" + new string(')', 1500);
        string deepName = new string('(', 1500) + "name" + new string(')', 1500);
        return new TheoryData<string, string>
        {
            { "column /* ' */", Select(Column($"/* ' */ {deep} /* ' */")) },
            { "column // '", Select(Column($"// '\\n{deep}\\n// '")) },
            { "column /* ` */", Select(Column($"/* ` */ {deep} /* ` */")) },
            { "where /* ' */", $$"""{"resourceType":"ViewDefinition","resource":"Patient","select":[{"column":[{"name":"x","path":"id"}]}],"where":[{"path":"/* ' */{{new string('(', 1500)}}true{{new string(')', 1500)}}/* ' */"}]}""" },
            { "forEach /* ' */", Select($$"""{"forEach":"/* ' */{{deepName}}/* ' */","column":[{"name":"x","path":"family"}]}""") },
            { "repeat /* ' */", Select($$"""{"repeat":["/* ' */{{deepName}}/* ' */"],"column":[{"name":"x","path":"id"}]}""") },
        };
    }

    private static string Column(string path) => $$"""{"column":[{"name":"x","path":"{{path}}"}]}""";

    [Theory]
    [MemberData(nameof(CommentBypasses))]
    public async Task GivenDeepNestingHiddenBehindAQuoteInAComment_WhenRun_ThenItIsStillRefused(string label, string view)
    {
        label.ShouldNotBeNullOrEmpty();
        using TestFolder folder = Clinic();

        IssueItem error = Error(await RunAsync(folder, new ViewRequest(view)));

        error.Code.ShouldBe("invalid-view");
        error.Diagnostics.ShouldContain("nested more than 64 levels");
    }

    [Fact]
    public async Task GivenAnExtraWhereWithNestingHiddenBehindAQuoteInAComment_WhenRun_ThenItIsAnInvalidParameter()
    {
        using TestFolder folder = Clinic();
        string where = "/* ' */" + new string('(', 1500) + "true" + new string(')', 1500) + "/* ' */";

        Error(await RunAsync(folder, new ViewRequest(PatientView, Where: where))).Code.ShouldBe("invalid-parameter");
    }

    // These do not look like a loop to a scan of the text; the evaluator stops them at 100 levels.
    [Theory]
    [InlineData("$this.where(true)")]
    [InlineData("$this | $this")]
    [InlineData("$this.first()")]
    [InlineData("select($this)")]
    [InlineData("%rootResource")]
    [InlineData("/* c */ $this")]
    [InlineData("$this // c")]
    public async Task GivenARepeatThatLoopsWithoutLookingLikeThis_WhenRun_ThenTheEvaluatorStopsItAndTheCallerIsTold(string repeat)
    {
        using TestFolder folder = Clinic();
        string view = Select($$"""{"repeat":["{{repeat}}"],"column":[{"name":"x","path":"id"}]}""");

        IssueItem error = Error(await RunAsync(folder, new ViewRequest(view)));

        error.Code.ShouldBe("view-evaluation-failed");
        error.Diagnostics.ShouldContain("repeat went deeper than 100 levels");
    }

    [Fact]
    public async Task GivenALoopingRepeatInAFolderViewDefinition_WhenRunById_ThenItIsAnEnvelopeWithoutTheFolderTextQuoted()
    {
        using TestFolder folder = Clinic().Write("views/loop.json", """
            {"resourceType":"ViewDefinition","id":"vd-loop","resource":"Patient","select":[{"repeat":["$this.where(true)"],"column":[{"name":"x","path":"id"}]}]}
            """);

        IssueItem error = Error(await RunAsync(folder, new ViewRequest("vd-loop")));

        error.Code.ShouldBe("view-evaluation-failed");
        error.Diagnostics.ShouldContain("digest");
        error.Diagnostics.ShouldNotContain("where(true)");
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
