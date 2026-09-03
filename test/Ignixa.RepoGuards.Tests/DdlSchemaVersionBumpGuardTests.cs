// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Shouldly;

namespace Ignixa.RepoGuards.Tests;

/// <summary>
/// Guards <c>SchemaVersionConstants.CurrentVersion</c> against the DDL it is supposed to describe.
/// <c>SchemaDeployer.UpgradeIfNeededAsync</c> decides whether a tenant needs upgrading purely by comparing
/// that recorded number against the tenant's stamped one -- it never compares dacpac content -- and
/// <c>DeployIfEmptyAsync</c> returns early whenever <c>dbo.Resource</c> already exists. So DDL that ships
/// without a bump reaches no existing tenant at all: both deploy paths skip, the only trace is a Debug line
/// saying the tenant is already current, and the first call into a newly added procedure fails at runtime
/// with "Could not find stored procedure".
/// <para>
/// That is not hypothetical. Every terminology object added between schema version 1 and this guard --
/// <c>dbo.ImportTermValueSet</c>, <c>dbo.ImportTermConceptMap</c>, two table types, the CS_AS collation on
/// every code column -- shipped under an unchanged <c>CurrentVersion = 1</c>, and nothing in the build,
/// the schema compiler or the test suite failed. Deployed against a database provisioned before those
/// changes, <c>UpgradeIfNeededAsync</c> was verified to return silently having applied none of it.
/// </para>
/// <para>
/// <b>What this guard can and cannot enforce.</b> It fails whenever the deployable content of
/// <c>Ignixa.DataLayer.SqlServer.Database</c> stops matching <see cref="PinnedFingerprints"/>, which forces
/// whoever changed the DDL to open this file and state what they did. It cannot tell an author who appends
/// a new (version, fingerprint) entry from one who overwrites the last entry's fingerprint and leaves
/// <c>CurrentVersion</c> alone -- no in-tree test can, since both are just edits to this file. What it
/// converts is a silent omission into a deliberate, one-line, reviewable edit: the correct fix appends a
/// row and bumps a constant, the incorrect one edits a row in place, and those read differently in a diff.
/// </para>
/// </summary>
public class DdlSchemaVersionBumpGuardTests
{
    /// <summary>
    /// Append-only: one entry per schema version, holding the fingerprint of the Database project's
    /// deployable content as of that version. The last entry must describe the working tree.
    /// <para>
    /// Version 1 is deliberately absent rather than back-filled. There is no single DDL state that
    /// corresponds to it: the schema changed repeatedly while <c>CurrentVersion</c> sat at 1, which is
    /// exactly the defect this guard exists to stop recurring, so any fingerprint recorded for version 1
    /// would be a fabricated one picked from an arbitrary point in that range.
    /// </para>
    /// </summary>
    private static readonly (int Version, string Fingerprint)[] PinnedFingerprints =
    [
        (2, "960c429bd4b96ab2378a72f89407bcab215a7b149ddbd51959194b67a05c8b81"),
    ];

    private const string DatabaseProjectRelativePath = "src/DataLayer/Ignixa.DataLayer.SqlServer.Database";

    private const string SchemaVersionConstantsRelativePath =
        "src/DataLayer/Ignixa.DataLayer.SqlServer/SchemaVersionConstants.cs";

    /// <summary>
    /// Everything DacFx builds into the dacpac, plus the <c>.sqlproj</c> itself -- its <c>DSP</c> and
    /// <c>ModelCollation</c> change the deployed model without any <c>.sql</c> file being touched, and this
    /// branch's retarget to <c>SqlAzureV12</c> is precisely such a change. <c>README.md</c> is excluded
    /// because it deploys nothing.
    /// </summary>
    private static readonly string[] DeployableExtensions = [".sql", ".sqlproj"];

    [Fact]
    public void GivenTheDatabaseProject_WhenItsDeployableContentIsFingerprinted_ThenItMatchesThePinForTheCurrentSchemaVersion()
    {
        var repoRoot = RepoRoot.Find();
        var databaseProjectDirectory = Path.Combine(repoRoot, DatabaseProjectRelativePath.Replace('/', Path.DirectorySeparatorChar));

        Directory.Exists(databaseProjectDirectory).ShouldBeTrue(
            $"{databaseProjectDirectory} does not exist -- update {nameof(DatabaseProjectRelativePath)} if the project moved.");

        var actualFingerprint = ComputeFingerprint(databaseProjectDirectory);
        var (pinnedVersion, pinnedFingerprint) = PinnedFingerprints[^1];

        // Compared as a bool rather than with ShouldBe so the failure is the message below and not
        // Shouldly's 64-character-wide hex diff, which says nothing useful about two SHA-256 digests.
        string.Equals(actualFingerprint, pinnedFingerprint, StringComparison.Ordinal).ShouldBeTrue(
            $"""
             The deployable content of {DatabaseProjectRelativePath} no longer matches the fingerprint pinned
             for schema version {pinnedVersion}.

             DDL changes do not reach an already-deployed tenant unless SchemaVersionConstants.CurrentVersion
             is raised: SchemaDeployer.UpgradeIfNeededAsync compares only that number, never dacpac content,
             so an un-bumped change is applied to nobody and reports success.

             To fix, in {SchemaVersionConstantsRelativePath}:
               1. Raise CurrentVersion to {pinnedVersion + 1}.
               2. Append a changelog line "// Version {pinnedVersion + 1} (expand|contract) -- <what changed>".
             Then in this file, APPEND (do not edit the existing row) to {nameof(PinnedFingerprints)}:
               ({pinnedVersion + 1}, "{actualFingerprint}"),

             If you are still iterating on version {pinnedVersion} and it has not shipped anywhere, replacing
             the version {pinnedVersion} fingerprint with {actualFingerprint} is the right edit instead -- but
             say so in review, because it is indistinguishable here from forgetting the bump.
             """);
    }

    [Fact]
    public void GivenThePinnedFingerprints_WhenComparedToTheDeclaredConstants_ThenTheyAgree()
    {
        var repoRoot = RepoRoot.Find();
        var source = File.ReadAllText(Path.Combine(repoRoot, SchemaVersionConstantsRelativePath.Replace('/', Path.DirectorySeparatorChar)));

        var declaredVersion = ParseCurrentVersion(source);
        var (pinnedVersion, _) = PinnedFingerprints[^1];

        declaredVersion.ShouldBe(
            pinnedVersion,
            $"SchemaVersionConstants.CurrentVersion is {declaredVersion} but the newest entry in " +
            $"{nameof(PinnedFingerprints)} is for version {pinnedVersion}. Whichever is behind, the two must " +
            "be raised together -- a fingerprint pinned against a version nobody stamps guards nothing.");

        for (var i = 1; i < PinnedFingerprints.Length; i++)
        {
            PinnedFingerprints[i].Version.ShouldBeGreaterThan(
                PinnedFingerprints[i - 1].Version,
                $"{nameof(PinnedFingerprints)} must be append-only and strictly increasing by version.");
        }

        foreach (var (version, _) in PinnedFingerprints)
        {
            source.Contains(
                $"// Version {version.ToString(CultureInfo.InvariantCulture)} ",
                StringComparison.Ordinal)
                .ShouldBeTrue(
                    $"SchemaVersionConstants' changelog has no line for version {version}. The changelog is " +
                    "the only place that records whether a version is expand or contract, which is what tells " +
                    "an operator whether an old build can still read a tenant this version has been applied to.");
        }
    }

    /// <summary>
    /// The fingerprint is the whole guard: if it ignored a change, every assertion above would pass over a
    /// schema that had silently drifted. These cases pin the three ways it must discriminate -- edited
    /// content, an added file, a removed file -- and the one way it must not: a line-ending difference,
    /// which varies with each developer's <c>core.autocrlf</c> and has nothing to do with the deployed
    /// schema.
    /// </summary>
    [Fact]
    public void GivenTwoDirectoriesDifferingOnlyInDeployableContent_WhenFingerprinted_ThenTheFingerprintsDiffer()
    {
        var directory = CreateTemporaryDirectory();

        try
        {
            var tablePath = Path.Combine(directory, "Tables", "Widget.sql");
            Directory.CreateDirectory(Path.GetDirectoryName(tablePath)!);
            File.WriteAllText(tablePath, "CREATE TABLE dbo.Widget (Code NVARCHAR (256) NOT NULL);\n");

            var baseline = ComputeFingerprint(directory);

            File.WriteAllText(tablePath, "CREATE TABLE dbo.Widget (Code NVARCHAR (512) NOT NULL);\n");
            ComputeFingerprint(directory).ShouldNotBe(baseline, "an edited column width must change the fingerprint");

            File.WriteAllText(tablePath, "CREATE TABLE dbo.Widget (Code NVARCHAR (256) NOT NULL);\n");
            ComputeFingerprint(directory).ShouldBe(baseline, "restoring the content must restore the fingerprint");

            File.WriteAllText(tablePath, "CREATE TABLE dbo.Widget (Code NVARCHAR (256) NOT NULL);\r\n");
            ComputeFingerprint(directory).ShouldBe(baseline, "a line-ending difference must NOT change the fingerprint");

            File.WriteAllText(tablePath, "﻿CREATE TABLE dbo.Widget (Code NVARCHAR (256) NOT NULL);\n");
            ComputeFingerprint(directory).ShouldBe(baseline, "a byte-order mark must NOT change the fingerprint");

            File.WriteAllText(tablePath, "CREATE TABLE dbo.Widget (Code NVARCHAR (256) NOT NULL);\n");

            var addedPath = Path.Combine(directory, "Types", "WidgetList.sql");
            Directory.CreateDirectory(Path.GetDirectoryName(addedPath)!);
            File.WriteAllText(addedPath, "CREATE TYPE dbo.WidgetList AS TABLE (Code NVARCHAR (256) NOT NULL);\n");
            var withAddedFile = ComputeFingerprint(directory);
            withAddedFile.ShouldNotBe(baseline, "an added .sql file must change the fingerprint");

            File.Delete(addedPath);
            ComputeFingerprint(directory).ShouldBe(baseline, "removing that file again must restore the fingerprint");

            var readmePath = Path.Combine(directory, "README.md");
            File.WriteAllText(readmePath, "# not deployable\n");
            ComputeFingerprint(directory).ShouldBe(baseline, "a non-deployable file must NOT change the fingerprint");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"IgnixaDdlFingerprint_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>
    /// SHA-256 over every deployable file's repo-relative path and content, ordered by path so the walk
    /// order of the file system cannot change the result. Content is normalized to LF and stripped of a
    /// byte-order mark first: both vary with how a developer's git is configured to check the tree out, and
    /// neither reaches the deployed schema, so letting either move the fingerprint would make this guard
    /// fail for reasons that have nothing to do with DDL.
    /// </summary>
    private static string ComputeFingerprint(string databaseProjectDirectory)
    {
        var files = Directory
            .EnumerateFiles(databaseProjectDirectory, "*", SearchOption.AllDirectories)
            .Where(path => DeployableExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Where(path => !IsUnderBuildOutput(databaseProjectDirectory, path))
            .Select(path => (
                RelativePath: Path.GetRelativePath(databaseProjectDirectory, path).Replace('\\', '/'),
                FullPath: path))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToList();

        files.ShouldNotBeEmpty($"No deployable files found under {databaseProjectDirectory}.");

        var builder = new StringBuilder();
        foreach (var (relativePath, fullPath) in files)
        {
            builder.Append(relativePath).Append('\n');
            builder.Append(Normalize(File.ReadAllText(fullPath))).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static bool IsUnderBuildOutput(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        return relative.StartsWith("bin/", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("obj/", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string content)
        => content.TrimStart('﻿').Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static int ParseCurrentVersion(string source)
    {
        var match = Regex.Match(
            source,
            @"public\s+const\s+int\s+CurrentVersion\s*=\s*(?<version>\d+)\s*;",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        match.Success.ShouldBeTrue(
            $"Could not find 'public const int CurrentVersion = <n>;' in {SchemaVersionConstantsRelativePath}. " +
            "If the constant was renamed or moved, this guard must be updated to follow it -- it is the value " +
            "SchemaDeployer gates every tenant upgrade on.");

        return int.Parse(match.Groups["version"].Value, CultureInfo.InvariantCulture);
    }
}
