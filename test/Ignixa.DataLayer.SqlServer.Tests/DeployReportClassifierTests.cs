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

    // Proves the Issue-child signal is checked for Drop operations too, not just Alter --
    // Drop isn't blanket-exempted like Create/Refresh. Shape verified against real DacFx output
    // (Task 9 empirical check): dropping an object genuinely absent from source (here simulated
    // via /p:DropObjectsNotInSource=true against a scratch table not in the dacpac -- production
    // code never sets that option, but this proves the Issue-child marker generalizes to Drop
    // when it does trigger) produces exactly this shape -- <Operation Name="Drop"><Item
    // Type="SqlTable"><Issue Id="N" /></Item></Operation> cross-referencing a DataIssue alert.
    // Originally this fixture used Type="SqlSimpleColumn" with no Issue child, modeling a
    // standalone column-drop op; Task 9's real-DacFx check found that shape doesn't occur in
    // practice -- a column absent from source always folds into a table-level Alter+Issue (per
    // this class's own doc comment and the Ground Truth section of Task 9's brief), never a
    // standalone Drop/SqlSimpleColumn item. Updated to the shape DacFx actually produces.
    [Fact]
    public void GivenAReportWithADestructiveTableDrop_WhenClassified_ThenIsNotAutoSafe()
    {
        var xml = ReadFixture("synthetic-destructive-drop.xml");
        DeployReportClassifier.IsAutoSafe(xml).ShouldBeFalse();
    }

    [Fact]
    public void GivenAnEmptyReport_WhenClassified_ThenIsAutoSafe()
    {
        const string xml = """<?xml version="1.0" encoding="utf-8"?><DeploymentReport xmlns="http://schemas.microsoft.com/sqlserver/dac/DeployReport/2012/02"><Operations /></DeploymentReport>""";
        DeployReportClassifier.IsAutoSafe(xml).ShouldBeTrue();
    }

    [Fact]
    public void GivenADefaultConstraintCanonicalizationDiff_WhenClassified_ThenIsAutoSafe()
    {
        var xml = ReadFixture("synthetic-safe-default-constraint-alter.xml");
        DeployReportClassifier.IsAutoSafe(xml).ShouldBeTrue();
    }

    [Fact]
    public void GivenATableAlterWithNoIssueMarker_WhenClassified_ThenIsAutoSafe()
    {
        var xml = ReadFixture("synthetic-safe-table-alter-no-issue.xml");
        DeployReportClassifier.IsAutoSafe(xml).ShouldBeTrue();
    }

    [Fact]
    public void GivenAnAdditiveAlterOnATableNeverPreviouslyAllowListed_WhenClassified_ThenIsAutoSafe()
    {
        var xml = ReadFixture("synthetic-safe-alter-unrecognized-table.xml");
        DeployReportClassifier.IsAutoSafe(xml).ShouldBeTrue();
    }

    [Fact]
    public void GivenARealDataIssueAlertShape_WhenClassified_ThenIsNotAutoSafe()
    {
        var xml = ReadFixture("synthetic-destructive-alter-with-issue.xml");
        DeployReportClassifier.IsAutoSafe(xml).ShouldBeFalse();
    }
}
