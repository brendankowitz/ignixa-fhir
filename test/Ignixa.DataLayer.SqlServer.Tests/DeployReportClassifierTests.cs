using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.Tests;

public class DeployReportClassifierTests
{
    private const string ReportNs = "http://schemas.microsoft.com/sqlserver/dac/DeployReport/2012/02";

    private static string ReadFixture(string fileName)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));

    private static DeployClassification Outcome(string xml)
        => DeployReportClassifier.Classify(xml).Outcome;

    [Fact]
    public void GivenASelfConsistencyReport_WhenClassified_ThenIsAutoSafe()
    {
        var xml = ReadFixture("self-consistency-safe.xml");
        Outcome(xml).ShouldBe(DeployClassification.AutoSafe);
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
    public void GivenAReportWithADestructiveTableDrop_WhenClassified_ThenIsUnsafe()
    {
        var xml = ReadFixture("synthetic-destructive-drop.xml");
        Outcome(xml).ShouldBe(DeployClassification.Unsafe);
    }

    [Fact]
    public void GivenAnEmptyReport_WhenClassified_ThenIsAutoSafe()
    {
        var xml = $"""<?xml version="1.0" encoding="utf-8"?><DeploymentReport xmlns="{ReportNs}"><Operations /></DeploymentReport>""";
        Outcome(xml).ShouldBe(DeployClassification.AutoSafe);
    }

    [Fact]
    public void GivenADefaultConstraintCanonicalizationDiff_WhenClassified_ThenIsAutoSafe()
    {
        var xml = ReadFixture("synthetic-safe-default-constraint-alter.xml");
        Outcome(xml).ShouldBe(DeployClassification.AutoSafe);
    }

    // A near-duplicate fixture (synthetic-safe-alter-unrecognized-table.xml) was removed: it was
    // structurally identical to this one, differing only by table name, and since this classifier
    // no longer consults any table-name allow-list both drove the exact same "item has no Issue
    // child" branch.
    [Fact]
    public void GivenATableAlterWithNoIssueMarker_WhenClassified_ThenIsAutoSafe()
    {
        var xml = ReadFixture("synthetic-safe-table-alter-no-issue.xml");
        Outcome(xml).ShouldBe(DeployClassification.AutoSafe);
    }

    [Fact]
    public void GivenARealDataIssueAlertShape_WhenClassified_ThenIsUnsafeAndNamesTheFlaggedItem()
    {
        var xml = ReadFixture("synthetic-destructive-alter-with-issue.xml");
        var result = DeployReportClassifier.Classify(xml);

        result.Outcome.ShouldBe(DeployClassification.Unsafe);
        // The reasons are what make the verdict actionable -- SchemaDeployer embeds them in its
        // exception and the CLI prints them, so an operator learns WHICH object tripped the gate
        // instead of being told to re-read raw XML.
        result.Reasons.ShouldNotBeEmpty();
        result.ReasonSummary.ShouldContain("BackgroundJobs");
    }

    // Pins the doc comment's core claim -- Create/Refresh are exempt from the Issue-marker check
    // unconditionally. Without this, a refactor "simplifying" NeverDestructiveOperations could
    // silently start treating an exempt operation's Issue as destructive, or drop the exemption.
    [Theory]
    [InlineData("Create")]
    [InlineData("Refresh")]
    public void GivenANeverDestructiveOperationWithAnIssueMarker_WhenClassified_ThenIsAutoSafe(string operationName)
    {
        var xml = $"""
            <?xml version="1.0" encoding="utf-8"?><DeploymentReport xmlns="{ReportNs}"><Operations><Operation Name="{operationName}"><Item Value="[dbo].[SomeNewTable]" Type="SqlTable"><Issue Id="1" /></Item></Operation></Operations></DeploymentReport>
            """;
        Outcome(xml).ShouldBe(DeployClassification.AutoSafe);
    }

    // Envelope guards: an unrecognized root element/namespace (e.g. a future DacFx version bumping
    // the report schema) must never be silently treated as "no operations found, therefore safe".
    [Fact]
    public void GivenAReportWithAnUnrecognizedNamespace_WhenClassified_ThenIsUnclassifiable()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?><DeploymentReport><Operations><Operation Name="Alter"><Item Value="[dbo].[BackgroundJobs]" Type="SqlTable"><Issue Id="1" /></Item></Operation></Operations></DeploymentReport>
            """;
        Outcome(xml).ShouldBe(DeployClassification.Unclassifiable);
    }

    [Fact]
    public void GivenAReportMissingTheOperationsElement_WhenClassified_ThenIsUnclassifiable()
    {
        var xml = $"""<?xml version="1.0" encoding="utf-8"?><DeploymentReport xmlns="{ReportNs}" />""";
        Outcome(xml).ShouldBe(DeployClassification.Unclassifiable);
    }

    // Payload guards. These fire on ANY unrecognized child, not just when every child is
    // unrecognized: a partial rename (one valid sibling next to an unrecognized one carrying the
    // destructive marker) is the exact shape that previously slipped through as "safe".
    [Fact]
    public void GivenOperationsWithAnUnrecognizedChildElement_WhenClassified_ThenIsUnclassifiable()
    {
        var xml = $"""
            <?xml version="1.0" encoding="utf-8"?><DeploymentReport xmlns="{ReportNs}"><Operations><Renamed Name="Alter"><Item Value="[dbo].[BackgroundJobs]" Type="SqlTable"><Issue Id="1" /></Item></Renamed></Operations></DeploymentReport>
            """;
        Outcome(xml).ShouldBe(DeployClassification.Unclassifiable);
    }

    [Fact]
    public void GivenOperationsMixingARecognizedAndAnUnrecognizedChild_WhenClassified_ThenIsUnclassifiable()
    {
        var xml = $"""
            <?xml version="1.0" encoding="utf-8"?><DeploymentReport xmlns="{ReportNs}"><Operations><Operation Name="Create"><Item Value="[dbo].[A]" Type="SqlTable" /></Operation><OperationV2 Name="Alter"><ItemV2 Value="[dbo].[B]" Type="SqlTable"><Issue Id="1" /></ItemV2></OperationV2></Operations></DeploymentReport>
            """;
        Outcome(xml).ShouldBe(DeployClassification.Unclassifiable);
    }

    [Fact]
    public void GivenAnOperationWithAnUnrecognizedChildElement_WhenClassified_ThenIsUnclassifiable()
    {
        var xml = $"""
            <?xml version="1.0" encoding="utf-8"?><DeploymentReport xmlns="{ReportNs}"><Operations><Operation Name="Alter"><Renamed Value="[dbo].[BackgroundJobs]" Type="SqlTable"><Issue Id="1" /></Renamed></Operation></Operations></DeploymentReport>
            """;
        Outcome(xml).ShouldBe(DeployClassification.Unclassifiable);
    }

    // Every operation in a real DacFx report names its affected objects via Item children
    // (verified against the captured self-consistency report: Drop/Create/UnbindTable/
    // TableRebuild/Refresh all carry at least one). An Operation that moved its content into
    // attributes would otherwise be skipped silently and reported as "nothing found, safe".
    [Fact]
    public void GivenAnOperationWithNoItemChildrenAtAll_WhenClassified_ThenIsUnclassifiable()
    {
        var xml = $"""
            <?xml version="1.0" encoding="utf-8"?><DeploymentReport xmlns="{ReportNs}"><Operations><Operation Name="Drop" Value="[dbo].[Resource]" /></Operations></DeploymentReport>
            """;
        Outcome(xml).ShouldBe(DeployClassification.Unclassifiable);
    }

    // Validation must reach the Item's children too, or the fail-open simply moves one level
    // deeper: a renamed/re-namespaced <Issue> leaves a genuinely destructive Drop looking
    // unflagged, and the report classifies as auto-safe.
    [Fact]
    public void GivenAnItemWithAnUnrecognizedIssueElement_WhenClassified_ThenIsUnclassifiable()
    {
        var xml = $"""
            <?xml version="1.0" encoding="utf-8"?><DeploymentReport xmlns="{ReportNs}"><Operations><Operation Name="Drop"><Item Value="[dbo].[Resource]" Type="SqlTable"><IssueV2 Id="1" /></Item></Operation></Operations></DeploymentReport>
            """;
        Outcome(xml).ShouldBe(DeployClassification.Unclassifiable);
    }

    [Fact]
    public void GivenAnOperationsElementCarryingOnlyTextContent_WhenClassified_ThenIsUnclassifiable()
    {
        var xml = $"""
            <?xml version="1.0" encoding="utf-8"?><DeploymentReport xmlns="{ReportNs}"><Operations>Drop [dbo].[Resource]</Operations></DeploymentReport>
            """;
        Outcome(xml).ShouldBe(DeployClassification.Unclassifiable);
    }

    // A genuinely empty <Operations /> (no children at all) stays safe -- proving the guards above
    // discriminate on "unrecognized children present" rather than merely "no Operation elements".
    [Fact]
    public void GivenAnOperationsElementWithNoChildrenAtAll_WhenClassified_ThenIsAutoSafe()
    {
        var xml = $"""<?xml version="1.0" encoding="utf-8"?><DeploymentReport xmlns="{ReportNs}"><Operations /></DeploymentReport>""";
        Outcome(xml).ShouldBe(DeployClassification.AutoSafe);
    }

    // Alert reconciliation is keyed on the Issue Id, not on "did I see any Issue at all". An
    // existence-only check let an unrelated marker elsewhere in the document discharge a genuinely
    // unaccounted-for data-loss alert.
    [Fact]
    public void GivenADataIssueAlertWithNoCorrespondingItemIssue_WhenClassified_ThenIsUnclassifiable()
    {
        var xml = $"""
            <?xml version="1.0" encoding="utf-8"?><DeploymentReport xmlns="{ReportNs}"><Alerts><Alert Name="DataIssue"><Issue Value="The column [dbo].[BackgroundJobs].[Gone] is being dropped, data loss could occur." Id="1" /></Alert></Alerts><Operations><Operation Name="Alter"><Item Value="[dbo].[BackgroundJobs]" Type="SqlTable" /></Operation></Operations></DeploymentReport>
            """;
        Outcome(xml).ShouldBe(DeployClassification.Unclassifiable);
    }

    // Regression guard for a demonstrated fail-open: an unrelated Issue on an exempt operation
    // (Id=1) must NOT discharge a separate, genuinely unaccounted-for data-loss alert (Id=2).
    // Under an existence-only cross-check this report classified as auto-safe.
    [Fact]
    public void GivenAnUnaccountedDataIssueAlertAlongsideAnUnrelatedExemptIssue_WhenClassified_ThenIsUnclassifiable()
    {
        var xml = $"""
            <?xml version="1.0" encoding="utf-8"?><DeploymentReport xmlns="{ReportNs}"><Alerts><Alert Name="DataIssue"><Issue Value="unrelated" Id="1" /><Issue Value="The column [dbo].[T].[Gone] is being dropped, data loss could occur." Id="2" /></Alert></Alerts><Operations><Operation Name="Create"><Item Value="[dbo].[New]" Type="SqlTable"><Issue Id="1" /></Item></Operation></Operations></DeploymentReport>
            """;
        var result = DeployReportClassifier.Classify(xml);

        result.Outcome.ShouldBe(DeployClassification.Unclassifiable);
        result.ReasonSummary.ShouldContain("Id=2");
    }

    // A DataIssue alert resolving to an operation kind this classifier treats as never destructive
    // is a genuine contradiction between DacFx's signal and this class's premise -- defer to a
    // human rather than trusting either half.
    [Fact]
    public void GivenADataIssueAlertResolvingToAnExemptOperation_WhenClassified_ThenIsUnclassifiable()
    {
        var xml = $"""
            <?xml version="1.0" encoding="utf-8"?><DeploymentReport xmlns="{ReportNs}"><Alerts><Alert Name="DataIssue"><Issue Value="data loss could occur" Id="1" /></Alert></Alerts><Operations><Operation Name="Create"><Item Value="[dbo].[SomeTable]" Type="SqlTable"><Issue Id="1" /></Item></Operation></Operations></DeploymentReport>
            """;
        Outcome(xml).ShouldBe(DeployClassification.Unclassifiable);
    }

    // Only DataIssue alerts gate the reconciliation -- DacFx emits other alert kinds routinely and
    // treating them as unreadable would reject ordinary safe reports.
    [Fact]
    public void GivenANonDataIssueAlertWithNoItemIssue_WhenClassified_ThenIsAutoSafe()
    {
        var xml = $"""
            <?xml version="1.0" encoding="utf-8"?><DeploymentReport xmlns="{ReportNs}"><Alerts><Alert Name="DataMotion"><Issue Value="rows will be moved" Id="1" /></Alert></Alerts><Operations><Operation Name="Alter"><Item Value="[dbo].[BackgroundJobs]" Type="SqlTable" /></Operation></Operations></DeploymentReport>
            """;
        Outcome(xml).ShouldBe(DeployClassification.AutoSafe);
    }

    // A DataIssue Issue element missing its Id attribute is a report shape this classifier cannot
    // reconcile against the inline Item markers -- it must not be silently dropped from
    // consideration, which would make an otherwise-flagged report look like it has no alerts at all.
    [Fact]
    public void GivenADataIssueAlertIssueMissingIdAttribute_WhenClassified_ThenIsUnclassifiable()
    {
        var xml = $"""
            <?xml version="1.0" encoding="utf-8"?><DeploymentReport xmlns="{ReportNs}"><Alerts><Alert Name="DataIssue"><Issue Value="The column [dbo].[T].[Gone] is being dropped, data loss could occur." /></Alert></Alerts><Operations><Operation Name="Alter"><Item Value="[dbo].[T]" Type="SqlTable" /></Operation></Operations></DeploymentReport>
            """;
        Outcome(xml).ShouldBe(DeployClassification.Unclassifiable);
    }

    // A DataIssue alert whose child is renamed/re-namespaced (e.g. a future DacFx emitting IssueV2
    // instead of Issue) must fail closed the same way an unrecognized Operation/Item child does --
    // not be silently ignored, leaving zero reconciled alert ids and a false AutoSafe.
    [Fact]
    public void GivenADataIssueAlertWithAnUnrecognizedChildElement_WhenClassified_ThenIsUnclassifiable()
    {
        var xml = $"""
            <?xml version="1.0" encoding="utf-8"?><DeploymentReport xmlns="{ReportNs}"><Alerts><Alert Name="DataIssue"><IssueV2 Id="1" /></Alert></Alerts><Operations><Operation Name="Alter"><Item Value="[dbo].[T]" Type="SqlTable" /></Operation></Operations></DeploymentReport>
            """;
        Outcome(xml).ShouldBe(DeployClassification.Unclassifiable);
    }

    // An unrecognized element directly under <Alerts> (not an <Alert> at all) is the same class of
    // shape drift Operations/Operation/Item already guard against.
    [Fact]
    public void GivenAnAlertsElementWithAnUnrecognizedChild_WhenClassified_ThenIsUnclassifiable()
    {
        var xml = $"""
            <?xml version="1.0" encoding="utf-8"?><DeploymentReport xmlns="{ReportNs}"><Alerts><Notice Value="something" /></Alerts><Operations><Operation Name="Alter"><Item Value="[dbo].[T]" Type="SqlTable" /></Operation></Operations></DeploymentReport>
            """;
        Outcome(xml).ShouldBe(DeployClassification.Unclassifiable);
    }

    // Input that isn't XML at all is a genuinely exceptional condition, distinct from a report
    // whose shape we can't read -- it stays an exception rather than becoming Unclassifiable.
    [Fact]
    public void GivenMalformedNonXmlInput_WhenClassified_ThenThrowsXmlException()
    {
        Should.Throw<System.Xml.XmlException>(() => DeployReportClassifier.Classify("not xml at all"));
    }

    [Fact]
    public void GivenNullOrEmptyInput_WhenClassified_ThenThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() => DeployReportClassifier.Classify(string.Empty));
        Should.Throw<ArgumentException>(() => DeployReportClassifier.Classify(null!));
    }

    [Fact]
    public void GivenAnAutoSafeReport_WhenClassified_ThenReportsNoReasons()
    {
        var xml = ReadFixture("self-consistency-safe.xml");
        var result = DeployReportClassifier.Classify(xml);

        result.IsAutoSafe.ShouldBeTrue();
        result.Reasons.ShouldBeEmpty();
    }
}
