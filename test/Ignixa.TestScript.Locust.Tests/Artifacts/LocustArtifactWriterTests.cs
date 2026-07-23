using System.Text;
using System.Text.Json.Nodes;
using Ignixa.TestScript.Locust.Artifacts;
using Ignixa.TestScript.Locust.Diagnostics;
using Ignixa.TestScript.Locust.Ir;

namespace Ignixa.TestScript.Locust.Tests.Artifacts;

public class LocustArtifactWriterTests
{
    private static readonly IReadOnlyList<string> ExpectedFileNames =
    [
        "diagnostics.json",
        "ignixa_testscript_runtime.py",
        "locustfile.py",
        "requirements.txt",
        "testscript.ir.json"
    ];

    private const string ExpectedRequirementsText =
        "locust==2.33.2\nfhirpathpy==2.1.0\nrequests==2.32.3\nazure-identity==1.25.3\n";

    [Fact]
    public async Task GivenDocument_WhenWritten_ThenArtifactIsFlatAndComplete()
    {
        string root = CreateTestRoot();
        string output = Path.Combine(root, "output");
        try
        {
            await new LocustArtifactWriter().WriteAsync(CreateDocument(), [], output, CancellationToken.None);

            Directory.GetDirectories(output).ShouldBeEmpty();
            Directory.GetFiles(output).Select(Path.GetFileName).Order()
                .ShouldBe(ExpectedFileNames.Order());

            foreach (string fileName in ExpectedFileNames)
            {
                AssertNoBom(Path.Combine(output, fileName));
            }

            AssertNoLeftoverSiblings(root, "output");
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Fact]
    public async Task GivenDocument_WhenWritten_ThenIrJsonHasSchemaVersionAndMetadata()
    {
        string root = CreateTestRoot();
        string output = Path.Combine(root, "output");
        try
        {
            await new LocustArtifactWriter().WriteAsync(CreateDocument(), [], output, CancellationToken.None);

            string irJson = await File.ReadAllTextAsync(Path.Combine(output, "testscript.ir.json"));
            JsonNode irNode = JsonNode.Parse(irJson)!;

            irNode["schemaVersion"]!.GetValue<string>().ShouldBe(LocustIrSerializer.SchemaVersion);
            irNode["metadata"]!["name"]!.GetValue<string>().ShouldBe("Basic");
            irNode["metadata"]!["source"]!.GetValue<string>().ShouldBe("basic.json");
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Fact]
    public async Task GivenDiagnostics_WhenWritten_ThenDiagnosticsJsonUsesCamelCaseAndPreservesOrder()
    {
        string root = CreateTestRoot();
        string output = Path.Combine(root, "output");
        try
        {
            LocustDiagnostic[] diagnostics =
            [
                new LocustDiagnostic("LOCUST_METRIC", LocustDiagnosticSeverity.Info, "source:info", "info message"),
                new LocustDiagnostic("LOCUST002", LocustDiagnosticSeverity.Warning, "source:warning", "warning message"),
                new LocustDiagnostic("LOCUST003", LocustDiagnosticSeverity.Error, "source:error", "error message")
            ];

            await new LocustArtifactWriter().WriteAsync(CreateDocument(), diagnostics, output, CancellationToken.None);

            string diagnosticsJson = await File.ReadAllTextAsync(Path.Combine(output, "diagnostics.json"));
            JsonArray array = JsonNode.Parse(diagnosticsJson)!.AsArray();

            array.Count.ShouldBe(3);

            array[0]!["code"]!.GetValue<string>().ShouldBe("LOCUST_METRIC");
            array[0]!["severity"]!.GetValue<string>().ShouldBe("info");
            array[0]!["source"]!.GetValue<string>().ShouldBe("source:info");
            array[0]!["message"]!.GetValue<string>().ShouldBe("info message");

            array[1]!["severity"]!.GetValue<string>().ShouldBe("warning");
            array[2]!["severity"]!.GetValue<string>().ShouldBe("error");

            AssertNoBom(Path.Combine(output, "diagnostics.json"));
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Fact]
    public async Task GivenProductionWriter_WhenWritten_ThenEmbeddedAssetsMatchPinnedLfContentWithNoCarriageReturns()
    {
        string root = CreateTestRoot();
        string output = Path.Combine(root, "output");
        try
        {
            await new LocustArtifactWriter().WriteAsync(CreateDocument(), [], output, CancellationToken.None);

            string requirementsPath = Path.Combine(output, "requirements.txt");
            string locustfilePath = Path.Combine(output, "locustfile.py");
            string runtimePath = Path.Combine(output, "ignixa_testscript_runtime.py");

            AssertNoCarriageReturn(requirementsPath);
            AssertNoCarriageReturn(locustfilePath);
            AssertNoCarriageReturn(runtimePath);

            string requirements = await File.ReadAllTextAsync(requirementsPath);
            requirements.ShouldBe(ExpectedRequirementsText);

            string locustfile = await File.ReadAllTextAsync(locustfilePath);
            locustfile.ShouldContain("import ignixa_testscript_runtime as runtime");
            locustfile.ShouldContain("class IgnixaTestScriptUser(HttpUser):");
            locustfile.ShouldContain("_IR_PATH = Path(__file__).with_name(\"testscript.ir.json\")");
            locustfile.ShouldContain("self.ignixa_state = runtime.initialize_user(_DOCUMENT, self)");

            string runtime = await File.ReadAllTextAsync(runtimePath);
            runtime.ShouldContain("SUPPORTED_SCHEMA_MAJOR = 1");
            runtime.ShouldContain("def initialize_user(document, user):");
            runtime.ShouldContain("def _new_context(document, user_state):");
            runtime.ShouldContain("def execute(document, user, state):");
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Fact]
    public async Task GivenExistingOutputWithSentinel_WhenAssetOpenFails_ThenOriginalDirectoryUnchanged()
    {
        string root = CreateTestRoot();
        string output = Path.Combine(root, "output");
        Directory.CreateDirectory(output);
        string sentinelPath = Path.Combine(output, "sentinel.txt");
        await File.WriteAllTextAsync(sentinelPath, "keep-me");
        try
        {
            var writer = new LocustArtifactWriter(_ => throw new IOException("simulated asset failure"));

            await Should.ThrowAsync<IOException>(() =>
                writer.WriteAsync(CreateDocument(), [], output, CancellationToken.None));

            Directory.GetFiles(output).Select(Path.GetFileName).ShouldBe(["sentinel.txt"]);
            (await File.ReadAllTextAsync(sentinelPath)).ShouldBe("keep-me");

            AssertNoLeftoverSiblings(root, "output");
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Fact]
    public async Task GivenExistingOutputWithSentinel_WhenSubsequentWriteSucceeds_ThenReplacesAtomically()
    {
        string root = CreateTestRoot();
        string output = Path.Combine(root, "output");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "sentinel.txt"), "keep-me");
        try
        {
            await new LocustArtifactWriter().WriteAsync(CreateDocument(), [], output, CancellationToken.None);

            Directory.GetDirectories(output).ShouldBeEmpty();
            Directory.GetFiles(output).Select(Path.GetFileName).Order()
                .ShouldBe(ExpectedFileNames.Order());
            File.Exists(Path.Combine(output, "sentinel.txt")).ShouldBeFalse();

            AssertNoLeftoverSiblings(root, "output");
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Fact]
    public async Task GivenPreCancelledToken_WhenWriteAttempted_ThenOriginalDirectoryUnchanged()
    {
        string root = CreateTestRoot();
        string output = Path.Combine(root, "output");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "sentinel.txt"), "keep-me");
        try
        {
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Should.ThrowAsync<OperationCanceledException>(() =>
                new LocustArtifactWriter().WriteAsync(CreateDocument(), [], output, cts.Token));

            Directory.GetFiles(output).Select(Path.GetFileName).ShouldBe(["sentinel.txt"]);

            AssertNoLeftoverSiblings(root, "output");
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Fact]
    public async Task GivenExistingFileAtOutputPath_WhenWriteAttempted_ThenFileUnchanged()
    {
        string root = CreateTestRoot();
        Directory.CreateDirectory(root);
        string output = Path.Combine(root, "output");
        await File.WriteAllTextAsync(output, "not-a-directory");
        try
        {
            await Should.ThrowAsync<IOException>(() =>
                new LocustArtifactWriter().WriteAsync(CreateDocument(), [], output, CancellationToken.None));

            File.Exists(output).ShouldBeTrue();
            (await File.ReadAllTextAsync(output)).ShouldBe("not-a-directory");

            AssertNoLeftoverSiblings(root, "output");
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Fact]
    public async Task GivenSuccessfulWrite_WhenAssetsRequested_ThenNamesRequestedInDeterministicOrderAndStreamsDisposed()
    {
        string root = CreateTestRoot();
        string output = Path.Combine(root, "output");
        try
        {
            var opener = new RecordingAssetOpener();
            var writer = new LocustArtifactWriter(opener.Open);

            await writer.WriteAsync(CreateDocument(), [], output, CancellationToken.None);

            opener.RequestedAssetNames.ShouldBe(["locustfile.py", "ignixa_testscript_runtime.py", "requirements.txt"]);
            opener.AllStreamsDisposed.ShouldBeTrue();
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Fact]
    public async Task GivenNullDocument_WhenWriteAsyncInvoked_ThenThrowsArgumentNullException()
    {
        string output = Path.Combine(Path.GetTempPath(), $"ignixa-locust-artifact-tests-{Guid.NewGuid():N}");

        await Should.ThrowAsync<ArgumentNullException>(() =>
            new LocustArtifactWriter().WriteAsync(null!, [], output, CancellationToken.None));
    }

    [Fact]
    public async Task GivenNullDiagnostics_WhenWriteAsyncInvoked_ThenThrowsArgumentNullException()
    {
        string output = Path.Combine(Path.GetTempPath(), $"ignixa-locust-artifact-tests-{Guid.NewGuid():N}");

        await Should.ThrowAsync<ArgumentNullException>(() =>
            new LocustArtifactWriter().WriteAsync(CreateDocument(), null!, output, CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GivenBlankOutputDirectory_WhenWriteAsyncInvoked_ThenThrowsArgumentException(string? outputDirectory)
    {
        await Should.ThrowAsync<ArgumentException>(() =>
            new LocustArtifactWriter().WriteAsync(CreateDocument(), [], outputDirectory!, CancellationToken.None));
    }

    [Fact]
    public void GivenNullOpenEmbeddedAssetDelegate_WhenConstructed_ThenThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new LocustArtifactWriter(null!));
    }

    [Fact]
    public void GivenNullMoveDirectoryDelegate_WhenConstructed_ThenThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new LocustArtifactWriter(CreateSucceedingOpener(), null!));
    }

    [Fact]
    public async Task GivenOutputDirectoryWithTrailingSeparator_WhenWritten_ThenArtifactIsFlatWithNoLeftoverSiblings()
    {
        string root = CreateTestRoot();
        string trimmedOutput = Path.Combine(root, "output");
        string outputWithTrailingSeparator = trimmedOutput + Path.DirectorySeparatorChar;
        try
        {
            await new LocustArtifactWriter().WriteAsync(
                CreateDocument(), [], outputWithTrailingSeparator, CancellationToken.None);

            Directory.GetDirectories(trimmedOutput).ShouldBeEmpty();
            Directory.GetFiles(trimmedOutput).Select(Path.GetFileName).Order()
                .ShouldBe(ExpectedFileNames.Order());

            AssertNoLeftoverSiblings(root, "output");
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:/")]
    public void GivenFilesystemRootOutputDirectory_WhenWriteAttempted_ThenThrowsArgumentException(string root)
    {
        Should.Throw<ArgumentException>(() =>
            new LocustArtifactWriter().WriteAsync(CreateDocument(), [], root, CancellationToken.None));
    }

    [Fact]
    public async Task GivenSwapFailureFollowedBySuccessfulRestore_WhenWriteAttempted_ThenOriginalRestoredAndSwapExceptionRethrown()
    {
        string root = CreateTestRoot();
        string output = Path.Combine(root, "output");
        Directory.CreateDirectory(output);
        string sentinelPath = Path.Combine(output, "sentinel.txt");
        await File.WriteAllTextAsync(sentinelPath, "keep-me");
        try
        {
            var move = new SequencedMoveDirectory(
                SequencedMoveDirectory.RealMove,
                SequencedMoveDirectory.Throwing("simulated swap failure"),
                SequencedMoveDirectory.RealMove);
            var writer = new LocustArtifactWriter(CreateSucceedingOpener(), move.Move);

            IOException thrown = await Should.ThrowAsync<IOException>(() =>
                writer.WriteAsync(CreateDocument(), [], output, CancellationToken.None));

            thrown.Message.ShouldBe("simulated swap failure");

            Directory.GetFiles(output).Select(Path.GetFileName).ShouldBe(["sentinel.txt"]);
            (await File.ReadAllTextAsync(sentinelPath)).ShouldBe("keep-me");

            AssertNoLeftoverSiblings(root, "output");
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Fact]
    public async Task GivenSwapFailureAndRestoreFailure_WhenWriteAttempted_ThenBackupPreservedAndExceptionNamesBothFailures()
    {
        string root = CreateTestRoot();
        string output = Path.Combine(root, "output");
        Directory.CreateDirectory(output);
        string sentinelPath = Path.Combine(output, "sentinel.txt");
        await File.WriteAllTextAsync(sentinelPath, "keep-me");
        try
        {
            var move = new SequencedMoveDirectory(
                SequencedMoveDirectory.RealMove,
                SequencedMoveDirectory.Throwing("simulated swap failure"),
                SequencedMoveDirectory.Throwing("simulated restore failure"));
            var writer = new LocustArtifactWriter(CreateSucceedingOpener(), move.Move);

            IOException thrown = await Should.ThrowAsync<IOException>(() =>
                writer.WriteAsync(CreateDocument(), [], output, CancellationToken.None));

            Directory.Exists(output).ShouldBeFalse();

            string[] siblingEntries = [.. Directory.GetFileSystemEntries(root).Select(entry => Path.GetFileName(entry)!)];
            siblingEntries.Length.ShouldBe(1);
            siblingEntries[0].ShouldStartWith("output.backup-");

            string backupDirectory = Path.Combine(root, siblingEntries[0]);
            thrown.Message.ShouldContain(backupDirectory);

            AggregateException aggregate = thrown.InnerException.ShouldBeOfType<AggregateException>();
            aggregate.InnerExceptions.Count.ShouldBe(2);
            aggregate.InnerExceptions[0].Message.ShouldBe("simulated swap failure");
            aggregate.InnerExceptions[1].Message.ShouldBe("simulated restore failure");

            Directory.GetFiles(backupDirectory).Select(Path.GetFileName).ShouldBe(["sentinel.txt"]);
            (await File.ReadAllTextAsync(Path.Combine(backupDirectory, "sentinel.txt"))).ShouldBe("keep-me");
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Fact]
    public async Task GivenCancellationDuringAssetCopy_WhenWriteAttempted_ThenOriginalUnchangedAndStagingCleaned()
    {
        string root = CreateTestRoot();
        string output = Path.Combine(root, "output");
        Directory.CreateDirectory(output);
        string sentinelPath = Path.Combine(output, "sentinel.txt");
        await File.WriteAllTextAsync(sentinelPath, "keep-me");
        try
        {
            using var cts = new CancellationTokenSource();
            var callCount = 0;

            Stream OpenAndCancelAfterFirstAsset(string assetFileName)
            {
                callCount++;
                if (callCount == 1)
                {
                    cts.Cancel();
                }

                return new MemoryStream(Encoding.UTF8.GetBytes($"# {assetFileName}\n"));
            }

            var writer = new LocustArtifactWriter(OpenAndCancelAfterFirstAsset);

            await Should.ThrowAsync<OperationCanceledException>(() =>
                writer.WriteAsync(CreateDocument(), [], output, cts.Token));

            Directory.GetFiles(output).Select(Path.GetFileName).ShouldBe(["sentinel.txt"]);
            (await File.ReadAllTextAsync(sentinelPath)).ShouldBe("keep-me");

            AssertNoLeftoverSiblings(root, "output");
            callCount.ShouldBe(1);
        }
        finally
        {
            SafeDelete(root);
        }
    }

    private static LocustIrDocument CreateDocument() => new()
    {
        Metadata = new LocustIrMetadata("Basic", "basic.json", "4.0")
    };

    private static string CreateTestRoot() =>
        Path.Combine(Path.GetTempPath(), $"ignixa-locust-artifact-tests-{Guid.NewGuid():N}");

    private static Func<string, Stream> CreateSucceedingOpener() =>
        assetFileName => new MemoryStream(Encoding.UTF8.GetBytes($"# {assetFileName}\n"));

    private static void AssertNoBom(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        bool hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        hasBom.ShouldBeFalse();
    }

    private static void AssertNoCarriageReturn(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        bytes.ShouldNotContain((byte)0x0D);
    }

    private static void AssertNoLeftoverSiblings(string root, string expectedEntryName)
    {
        Directory.GetFileSystemEntries(root).Select(Path.GetFileName).ShouldBe([expectedEntryName]);
    }

    private static void SafeDelete(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed class RecordingAssetOpener
    {
        private readonly List<string> _requestedAssetNames = [];
        private readonly List<TrackingStream> _openedStreams = [];

        public IReadOnlyList<string> RequestedAssetNames => _requestedAssetNames;

        public bool AllStreamsDisposed => _openedStreams.TrueForAll(s => s.IsDisposed);

        public Stream Open(string assetFileName)
        {
            _requestedAssetNames.Add(assetFileName);
            var stream = new TrackingStream(Encoding.UTF8.GetBytes($"# {assetFileName} content\n"));
            _openedStreams.Add(stream);
            return stream;
        }
    }

    private sealed class TrackingStream(byte[] buffer) : MemoryStream(buffer, writable: false)
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// A test double for the writer's <c>Action&lt;string, string&gt; moveDirectory</c> seam that
    /// applies a distinct behavior per call index (real move, or a thrown exception), falling back
    /// to a real <see cref="Directory.Move(string, string)"/> for any call beyond the configured
    /// sequence.
    /// </summary>
    private sealed class SequencedMoveDirectory(params Action<string, string>[] behaviors)
    {
        private readonly List<Action<string, string>> _behaviors = [.. behaviors];
        private int _callIndex;

        public static void RealMove(string sourceDirName, string destDirName) =>
            Directory.Move(sourceDirName, destDirName);

        public static Action<string, string> Throwing(string message) =>
            (_, _) => throw new IOException(message);

        public void Move(string sourceDirName, string destDirName)
        {
            Action<string, string> behavior = _callIndex < _behaviors.Count
                ? _behaviors[_callIndex]
                : RealMove;
            _callIndex++;
            behavior(sourceDirName, destDirName);
        }
    }
}
