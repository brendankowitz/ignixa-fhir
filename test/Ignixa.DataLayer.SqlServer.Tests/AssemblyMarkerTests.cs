namespace Ignixa.DataLayer.SqlServer.Tests;

public class AssemblyMarkerTests
{
    [Fact]
    public void GivenTheProject_WhenBuilt_ThenAssemblyMarkerExposesTheProjectName()
    {
        // Arrange & Act
        var name = AssemblyMarker.ProjectName;

        // Assert
        name.ShouldBe("Ignixa.DataLayer.SqlServer");
    }
}
