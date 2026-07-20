using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.Tests;

public class DeployReportClassifierTests
{
    private static string ReadFixture(string fileName)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));

    [Fact]
    public void GivenASelfConsistencyReport_WhenClassified_ThenIsAutoSafe()
    {
        var xml = ReadFixture("self-consistency-safe.xml");
        DeployReportClassifier.IsAutoSafe(xml).ShouldBeTrue();
    }

    [Fact]
    public void GivenAReportWithAnUnrecognizedColumnDrop_WhenClassified_ThenIsNotAutoSafe()
    {
        var xml = ReadFixture("synthetic-destructive-drop.xml");
        DeployReportClassifier.IsAutoSafe(xml).ShouldBeFalse();
    }

    [Fact]
    public void GivenACategoryEShapedDefaultConstraintDiff_WhenClassified_ThenIsAutoSafe()
    {
        var xml = ReadFixture("synthetic-category-e.xml");
        DeployReportClassifier.IsAutoSafe(xml).ShouldBeTrue();
    }

    [Fact]
    public void GivenAnEmptyReport_WhenClassified_ThenIsAutoSafe()
    {
        const string xml = """<?xml version="1.0" encoding="utf-8"?><DeploymentReport xmlns="http://schemas.microsoft.com/sqlserver/dac/DeployReport/2012/02"><Operations /></DeploymentReport>""";
        DeployReportClassifier.IsAutoSafe(xml).ShouldBeTrue();
    }
}
