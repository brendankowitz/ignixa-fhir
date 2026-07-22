using Ignixa.ConformanceMatrix.Cli.Serving;
using Ignixa.TestScript.Expressions;
using Ignixa.TestScript.Model;
using Shouldly;

namespace Ignixa.ConformanceMatrix.Cli.Tests.Serving;

public class DefinitionRewriterTests
{
    private static TestScriptDefinition MakeDefinition() => new()
    {
        Metadata = new TestScriptMetadata { Name = "Test" },
        Setup = [new OperationExpression { Type = "read", Url = "health" }],
        Teardown = [new OperationExpression { Type = "delete", Url = "Patient/1" }],
        Tests =
        [
            new TestPhaseDefinition
            {
                Name = "tc1",
                Actions =
                [
                    new OperationExpression { Type = "read", Url = "Patient/1" },
                    new AssertExpression { Criteria = new ResponseStatusCriteria("okay") }
                ]
            }
        ]
    };

    [Fact]
    public void GivenDefaultOptions_WhenApplying_ThenReturnsTheSameDefinitionInstance()
    {
        // Arrange
        var definition = MakeDefinition();
        var options = new RunRequestOptions();

        // Act
        var result = DefinitionRewriter.Apply(definition, options);

        // Assert
        result.IsValid.ShouldBeTrue();
        ReferenceEquals(result.Definition, definition).ShouldBeTrue();
    }

    [Fact]
    public void GivenRunSetupFalse_WhenApplying_ThenSetupIsClearedAndTeardownIsUntouched()
    {
        // Arrange
        var definition = MakeDefinition();
        var options = new RunRequestOptions(RunSetup: false);

        // Act
        var result = DefinitionRewriter.Apply(definition, options);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Definition!.Setup.ShouldBeEmpty();
        result.Definition.Teardown.ShouldHaveSingleItem();
    }

    [Fact]
    public void GivenRunTeardownFalse_WhenApplying_ThenTeardownIsClearedAndSetupIsUntouched()
    {
        // Arrange
        var definition = MakeDefinition();
        var options = new RunRequestOptions(RunTeardown: false);

        // Act
        var result = DefinitionRewriter.Apply(definition, options);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Definition!.Teardown.ShouldBeEmpty();
        result.Definition.Setup.ShouldHaveSingleItem();
    }

    [Fact]
    public void GivenAssertionsNone_WhenApplying_ThenTestActionsKeepOnlyOperationsButSetupAndTeardownAreUntouched()
    {
        // Arrange
        var definition = MakeDefinition();
        var options = new RunRequestOptions(Assertions: "none");

        // Act
        var result = DefinitionRewriter.Apply(definition, options);

        // Assert
        result.IsValid.ShouldBeTrue();
        var actions = result.Definition!.Tests[0].Actions;
        actions.ShouldHaveSingleItem();
        actions[0].ShouldBeOfType<OperationExpression>();
        result.Definition.Setup.ShouldHaveSingleItem();
        result.Definition.Teardown.ShouldHaveSingleItem();
    }

    [Fact]
    public void GivenAssertionsStatusOnly_WhenApplying_ThenRejectedAsAPhase3Feature()
    {
        // Arrange
        var definition = MakeDefinition();
        var options = new RunRequestOptions(Assertions: "status-only");

        // Act
        var result = DefinitionRewriter.Apply(definition, options);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Error.ShouldNotBeNull();
        result.Error.ShouldContain("Phase 3");
    }

    [Fact]
    public void GivenUnknownAssertionsValue_WhenApplying_ThenRejected()
    {
        // Arrange
        var definition = MakeDefinition();
        var options = new RunRequestOptions(Assertions: "bogus");

        // Act
        var result = DefinitionRewriter.Apply(definition, options);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Error.ShouldNotBeNull();
        result.Error.ShouldContain("bogus");
    }
}
