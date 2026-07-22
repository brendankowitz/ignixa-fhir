using Ignixa.ConformanceMatrix.Cli.Serving;
using Shouldly;

namespace Ignixa.ConformanceMatrix.Cli.Tests.Serving;

public class TestScriptRegistryTests
{
    [Fact]
    public void GivenUniqueStems_WhenLoading_ThenIdsAreFileStems()
    {
        // Arrange
        using var dir = new TempTestsDirectory();
        dir.WriteScript("Search/PatientSearch.json", TempTestsDirectory.ValidScriptJson("PatientSearch"));
        dir.WriteScript("CRUD/PatientCreate.json", TempTestsDirectory.ValidScriptJson("PatientCreate"));

        // Act
        var registry = TestScriptRegistry.Load(dir.Root);

        // Assert
        registry.Count.ShouldBe(2);
        registry.InvalidCount.ShouldBe(0);
        registry.TryGet("PatientSearch").ShouldNotBeNull();
        registry.TryGet("PatientCreate").ShouldNotBeNull();
        registry.TryGet("PatientSearch")!.RelativePath.ShouldBe("Search/PatientSearch.json");
    }

    [Fact]
    public void GivenDuplicateStems_WhenLoading_ThenIdsAreRelativePathsWithoutExtension()
    {
        // Arrange
        using var dir = new TempTestsDirectory();
        dir.WriteScript("Search/Basic.json", TempTestsDirectory.ValidScriptJson("SearchBasic"));
        dir.WriteScript("CRUD/Basic.json", TempTestsDirectory.ValidScriptJson("CrudBasic"));

        // Act
        var registry = TestScriptRegistry.Load(dir.Root);

        // Assert
        registry.Count.ShouldBe(2);
        registry.TryGet("Basic").ShouldBeNull();
        registry.TryGet("Search/Basic").ShouldNotBeNull();
        registry.TryGet("CRUD/Basic").ShouldNotBeNull();
        registry.TryGet("Search/Basic")!.Name.ShouldBe("SearchBasic");
        registry.TryGet("CRUD/Basic")!.Name.ShouldBe("CrudBasic");
    }

    [Fact]
    public void GivenInvalidJson_WhenLoading_ThenEntryIsListedWithParseError()
    {
        // Arrange
        using var dir = new TempTestsDirectory();
        dir.WriteScript("Broken.json", "{ not json");

        // Act
        var registry = TestScriptRegistry.Load(dir.Root);

        // Assert
        var entry = registry.TryGet("Broken");
        entry.ShouldNotBeNull();
        entry!.Definition.ShouldBeNull();
        entry.ParseError.ShouldNotBeNull();
        registry.InvalidCount.ShouldBe(1);
    }

    [Fact]
    public void GivenMixOfValidAndInvalidScripts_WhenLoading_ThenCountsReflectBoth()
    {
        // Arrange
        using var dir = new TempTestsDirectory();
        dir.WriteScript("Good.json", TempTestsDirectory.ValidScriptJson("Good"));
        dir.WriteScript("Bad.json", "not json at all");

        // Act
        var registry = TestScriptRegistry.Load(dir.Root);

        // Assert
        registry.Count.ShouldBe(2);
        registry.InvalidCount.ShouldBe(1);
        registry.TryGet("Good")!.ParseError.ShouldBeNull();
        registry.TryGet("Bad")!.ParseError.ShouldNotBeNull();
    }

    [Fact]
    public void GivenUnknownId_WhenLookingUp_ThenReturnsNull()
    {
        // Arrange
        using var dir = new TempTestsDirectory();
        dir.WriteScript("Good.json", TempTestsDirectory.ValidScriptJson("Good"));

        // Act
        var registry = TestScriptRegistry.Load(dir.Root);

        // Assert
        registry.TryGet("DoesNotExist").ShouldBeNull();
    }
}
