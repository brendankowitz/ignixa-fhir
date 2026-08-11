using Ignixa.SchemaUpgrade.Cli;
using Shouldly;

namespace Ignixa.SchemaUpgrade.Cli.Tests;

public class RootCommandHelpTests
{
    [Fact]
    public async Task GivenHelpFlag_WhenParsed_ThenOutputListsTenantIdAndConfirmOptions()
    {
        var originalOut = Console.Out;
        using var capturedOut = new StringWriter();
        Console.SetOut(capturedOut);

        try
        {
            await Program.CreateRootCommand().Parse(["--help"]).InvokeAsync();
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = capturedOut.ToString();
        output.ShouldContain("--tenant-id");
        output.ShouldContain("--confirm");
    }

    [Fact]
    public void GivenRootCommand_WhenInspectingOptions_ThenTenantIdIsRequiredAndConfirmIsNot()
    {
        var root = Program.CreateRootCommand();

        var tenantIdOption = root.Options.Single(o => o.Name == "--tenant-id");
        var confirmOption = root.Options.Single(o => o.Name == "--confirm");

        tenantIdOption.Required.ShouldBeTrue();
        confirmOption.Required.ShouldBeFalse();
    }
}
