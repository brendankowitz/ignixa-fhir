using FluentAssertions;
using Ignixa.Fml.Cli.Commands;

namespace Ignixa.Fml.Cli.Tests;

/// <summary>
/// Basic tests for the FML CLI commands.
/// </summary>
public class CommandTests
{
    [Fact]
    public void GivenConvertCommand_WhenCreated_ThenHasRequiredOptions()
    {
        // Arrange & Act
        var command = ConvertCommand.Create();

        // Assert
        command.Should().NotBeNull();
        command.Name.Should().Be("convert");
        command.Options.Should().Contain(o => o.Name == "map");
        command.Options.Should().Contain(o => o.Name == "input");
        command.Options.Should().Contain(o => o.Name == "out");
        command.Options.Should().Contain(o => o.Name == "context");
        command.Options.Should().Contain(o => o.Name == "format");
    }

    [Fact]
    public void GivenPreviewCommand_WhenCreated_ThenHasRequiredOptions()
    {
        // Arrange & Act
        var command = PreviewCommand.Create();

        // Assert
        command.Should().NotBeNull();
        command.Name.Should().Be("preview");
        command.Options.Should().Contain(o => o.Name == "map");
        command.Options.Should().Contain(o => o.Name == "input");
        command.Options.Should().Contain(o => o.Name == "context");
    }

    [Fact]
    public void GivenValidateCommand_WhenCreated_ThenHasRequiredOptions()
    {
        // Arrange & Act
        var command = ValidateCommand.Create();

        // Assert
        command.Should().NotBeNull();
        command.Name.Should().Be("validate");
        command.Options.Should().Contain(o => o.Name == "map");
        command.Options.Should().Contain(o => o.Name == "context");
    }
}
