using Ignixa.TestScript.XUnit;

namespace Ignixa.TestScript.Tests.XUnit;

public class TestScriptDataAttributeTests
{
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData");

    [Fact]
    public void GivenGlobMatchingKnownFiles_WhenGetData_ThenReturnsMatchingFilePaths()
    {
        var attribute = new TestScriptDataAttribute("*.json", TestDataPath);

        var data = attribute.GetData(null!).ToList();

        data.ShouldNotBeEmpty();
        data.ShouldAllBe(row => row.Length == 1);
        data.ShouldAllBe(row => ((string)row[0]).EndsWith(".json", StringComparison.OrdinalIgnoreCase));
        data.ShouldAllBe(row => File.Exists((string)row[0]));
    }

    [Fact]
    public void GivenGlobMatchingSpecificFile_WhenGetData_ThenReturnsSingleEntry()
    {
        var attribute = new TestScriptDataAttribute("simple-read.json", TestDataPath);

        var data = attribute.GetData(null!).ToList();

        data.Count.ShouldBe(1);
        ((string)data[0][0]).ShouldEndWith("simple-read.json");
    }

    [Fact]
    public void GivenGlobWithNoMatches_WhenGetData_ThenThrowsInvalidOperationException()
    {
        var attribute = new TestScriptDataAttribute("*.does-not-exist", TestDataPath);

        Should.Throw<InvalidOperationException>(() => attribute.GetData(null!).ToList())
            .Message.ShouldContain("*.does-not-exist");
    }

    [Fact]
    public void GivenGlobWithNoMatches_WhenGetData_ThenExceptionMessageContainsBasePath()
    {
        var attribute = new TestScriptDataAttribute("*.does-not-exist", TestDataPath);

        Should.Throw<InvalidOperationException>(() => attribute.GetData(null!).ToList())
            .Message.ShouldContain(TestDataPath);
    }

    [Fact]
    public void GivenNullBasePath_WhenConstructed_ThenDefaultsToAppContextBaseDirectory()
    {
        var attribute = new TestScriptDataAttribute("*.json");

        attribute.BasePath.ShouldBeNull();
        attribute.GlobPattern.ShouldBe("*.json");
    }

    [Fact]
    public void GivenExplicitBasePath_WhenConstructed_ThenBasePathIsSet()
    {
        var attribute = new TestScriptDataAttribute("**/*.json", TestDataPath);

        attribute.BasePath.ShouldBe(TestDataPath);
        attribute.GlobPattern.ShouldBe("**/*.json");
    }

    [Fact]
    public void GivenRecursiveGlob_WhenGetData_ThenReturnsAbsolutePaths()
    {
        var attribute = new TestScriptDataAttribute("**/*.json", TestDataPath);

        var data = attribute.GetData(null!).ToList();

        data.ShouldNotBeEmpty();
        data.ShouldAllBe(row => Path.IsPathRooted((string)row[0]));
    }
}
