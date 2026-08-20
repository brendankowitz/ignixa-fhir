using Ignixa.SchemaUpgrade.Cli;
using Shouldly;

namespace Ignixa.SchemaUpgrade.Cli.Tests;

public class ConfirmApplyTests
{
    // ConfirmApply's false return is exactly what makes RunAsync return exit code 1 without
    // deploying anything (see Program.RunAsync: `if (!ConfirmApply(...)) { ...; return 1; }`).
    // RunAsync itself needs a live tenant database and can't be driven end-to-end in a unit test,
    // so the decline-to-exit-code-1 contract is verified at this seam instead.
    [Theory]
    [InlineData("n")]
    [InlineData("N")]
    [InlineData("no")]
    [InlineData("")]
    public void GivenOperatorDeclines_WhenConfirmApply_ThenReturnsFalse(string response)
    {
        using var input = new StringReader(response);
        using var output = new StringWriter();

        var confirmed = Program.ConfirmApply(autoConfirm: false, input, output);

        confirmed.ShouldBeFalse();
    }

    [Theory]
    [InlineData("y")]
    [InlineData("Y")]
    public void GivenOperatorAccepts_WhenConfirmApply_ThenReturnsTrue(string response)
    {
        using var input = new StringReader(response);
        using var output = new StringWriter();

        var confirmed = Program.ConfirmApply(autoConfirm: false, input, output);

        confirmed.ShouldBeTrue();
    }

    [Fact]
    public void GivenOperatorDeclines_WhenConfirmApply_ThenPromptIsWrittenToOutput()
    {
        using var input = new StringReader("n");
        using var output = new StringWriter();

        Program.ConfirmApply(autoConfirm: false, input, output);

        output.ToString().ShouldContain("Apply this diff?");
    }

    [Fact]
    public void GivenAutoConfirm_WhenConfirmApply_ThenReturnsTrueWithoutPromptingOrReadingInput()
    {
        using var input = new ThrowsOnReadTextReader();
        using var output = new StringWriter();

        var confirmed = Program.ConfirmApply(autoConfirm: true, input, output);

        confirmed.ShouldBeTrue();
        output.ToString().ShouldBeEmpty();
    }

    private sealed class ThrowsOnReadTextReader : TextReader
    {
        public override string? ReadLine() =>
            throw new InvalidOperationException("Input should not be read when --confirm was already supplied.");
    }
}
