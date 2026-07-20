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

    // Category F -- discovered by Task 8's real older-schema integration test (not a synthetic
    // guess): Phase B's Task 9 (commit d7e7c600) added six nullable import-tracking columns to
    // dbo.PackageResource. DacFx reports this as a table-level Alter/SqlTable item, exactly the
    // same shape a destructive change to that table would take at the Operation/Item level -- the
    // only thing distinguishing a real column drop is an accompanying <Issue> cross-reference into
    // <Alerts><Alert Name="DataIssue">, which this fixture (matching the real report) omits.
    [Fact]
    public void GivenAPackageResourceAlterReport_WhenClassified_ThenIsAutoSafe()
    {
        var xml = ReadFixture("synthetic-category-f.xml");
        DeployReportClassifier.IsAutoSafe(xml).ShouldBeTrue();
    }

    // Proves Category F's fix is narrow and name-matched, not a general "any table Alter is
    // safe" rule: an Alter on a table NOT covered by an allow-list entry is still rejected.
    [Fact]
    public void GivenAnAlterReportForAnUnrelatedTable_WhenClassified_ThenIsNotAutoSafe()
    {
        const string xml = """<?xml version="1.0" encoding="utf-8"?><DeploymentReport xmlns="http://schemas.microsoft.com/sqlserver/dac/DeployReport/2012/02"><Operations><Operation Name="Alter"><Item Value="[dbo].[SomeOtherTable]" Type="SqlTable" /></Operation></Operations></DeploymentReport>""";
        DeployReportClassifier.IsAutoSafe(xml).ShouldBeFalse();
    }
}
