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
    // Drop isn't blanket-exempted like Create/Refresh. Shape verified against real DacFx output:
    // dropping an object genuinely absent from source (here simulated via
    // /p:DropObjectsNotInSource=true against a scratch table not in the dacpac -- production
    // code never sets that option, but this proves the Issue-child marker generalizes to Drop
    // when it does trigger) produces exactly this shape -- <Operation Name="Drop"><Item
    // Type="SqlTable"><Issue Id="N" /></Item></Operation> cross-referencing a DataIssue alert.
    // A column absent from source always folds into a table-level Alter+Issue (per this
    // class's own doc comment), never a standalone Drop/SqlSimpleColumn item.
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

    // synthetic-safe-table-alter-no-issue.xml and synthetic-safe-alter-unrecognized-table.xml
    // are structurally identical (a single no-Issue Alter/SqlTable item, differing only by table
    // name) since this classifier no longer references any table-name allow-list -- both exercise
    // the exact same "item has no Issue child" branch. Kept as one test; see
    // GivenACreateOperationWithAnIssueMarker_WhenClassified_ThenIsStillAutoSafe below for the
    // exemption-specific coverage this consolidation makes room for.
    [Fact]
    public void GivenATableAlterWithNoIssueMarker_WhenClassified_ThenIsAutoSafe()
    {
        var xml = ReadFixture("synthetic-safe-table-alter-no-issue.xml");
        DeployReportClassifier.IsAutoSafe(xml).ShouldBeTrue();
    }

    [Fact]
    public void GivenARealDataIssueAlertShape_WhenClassified_ThenIsNotAutoSafe()
    {
        var xml = ReadFixture("synthetic-destructive-alter-with-issue.xml");
        DeployReportClassifier.IsAutoSafe(xml).ShouldBeFalse();
    }

    // Proves the doc comment's core claim -- Create/Refresh are exempt from the Issue-marker
    // check unconditionally, not just "usually don't carry one". Without this test, a future
    // refactor that "simplifies" NeverDestructiveOperations handling could silently start
    // treating a Create's Issue marker as destructive (or vice versa drop the exemption) with no
    // red test to catch it.
    [Fact]
    public void GivenACreateOperationWithAnIssueMarker_WhenClassified_ThenIsStillAutoSafe()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?><DeploymentReport xmlns="http://schemas.microsoft.com/sqlserver/dac/DeployReport/2012/02"><Operations><Operation Name="Create"><Item Value="[dbo].[SomeNewTable]" Type="SqlTable"><Issue Id="1" /></Item></Operation></Operations></DeploymentReport>
            """;
        DeployReportClassifier.IsAutoSafe(xml).ShouldBeTrue();
    }

    // Regression test for a fail-open bug: a wrong/missing root element or namespace (e.g. a
    // future DacFx/SqlPackage version bumping the report schema) must not be silently treated
    // the same as "no operations" (which would classify a genuinely destructive, unparsed diff as
    // auto-safe). The gate must fail loud, not fail open.
    [Fact]
    public void GivenAReportWithAnUnrecognizedNamespace_WhenClassified_ThenThrows()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?><DeploymentReport><Operations><Operation Name="Alter"><Item Value="[dbo].[BackgroundJobs]" Type="SqlTable"><Issue Id="1" /></Item></Operation></Operations></DeploymentReport>
            """;
        Should.Throw<InvalidOperationException>(() => DeployReportClassifier.IsAutoSafe(xml));
    }

    [Fact]
    public void GivenAReportMissingTheOperationsElement_WhenClassified_ThenThrows()
    {
        const string xml = """<?xml version="1.0" encoding="utf-8"?><DeploymentReport xmlns="http://schemas.microsoft.com/sqlserver/dac/DeployReport/2012/02" />""";
        Should.Throw<InvalidOperationException>(() => DeployReportClassifier.IsAutoSafe(xml));
    }

    [Fact]
    public void GivenMalformedNonXmlInput_WhenClassified_ThenThrowsXmlException()
    {
        Should.Throw<System.Xml.XmlException>(() => DeployReportClassifier.IsAutoSafe("not xml at all"));
    }

    [Fact]
    public void GivenNullOrEmptyInput_WhenClassified_ThenThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() => DeployReportClassifier.IsAutoSafe(string.Empty));
        Should.Throw<ArgumentException>(() => DeployReportClassifier.IsAutoSafe(null!));
    }
}
