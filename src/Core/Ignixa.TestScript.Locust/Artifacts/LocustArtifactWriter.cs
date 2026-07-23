using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ignixa.TestScript.Locust.Diagnostics;
using Ignixa.TestScript.Locust.Ir;

namespace Ignixa.TestScript.Locust.Artifacts;

/// <summary>
/// Writes a <see cref="LocustIrDocument"/> and its diagnostics, together with the fixed Locust
/// loader, runtime stub, and pinned Python requirements, into a single flat output directory.
/// The output directory is replaced atomically: an existing directory at the target path is only
/// removed after every artifact file has been produced successfully in a sibling staging
/// directory.
/// </summary>
public sealed class LocustArtifactWriter
{
    private const string IrFileName = "testscript.ir.json";
    private const string DiagnosticsFileName = "diagnostics.json";

    private static readonly IReadOnlyList<string> s_assetFileNames =
    [
        "locustfile.py",
        "ignixa_testscript_runtime.py",
        "requirements.txt"
    ];

    private static readonly UTF8Encoding s_utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions s_diagnosticsSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly Func<string, Stream> _openEmbeddedAsset;

    /// <summary>
    /// Validates, once per process, that the writer's hardcoded set of planned output file names
    /// contains no duplicates before any instance can write to the file system.
    /// </summary>
    static LocustArtifactWriter()
    {
        EnsureNoDuplicatePlannedOutputs();
    }

    /// <summary>
    /// Creates a writer that resolves the Locust loader, runtime stub, and pinned requirements
    /// from the embedded resources of this assembly.
    /// </summary>
    public LocustArtifactWriter()
        : this(OpenEmbeddedAssetFromAssembly)
    {
    }

    /// <summary>
    /// Creates a writer that resolves embedded assets through <paramref name="openEmbeddedAsset"/>.
    /// Used by tests to inject asset content or simulate asset-resolution failures.
    /// </summary>
    /// <param name="openEmbeddedAsset">
    /// A delegate invoked with each logical asset file name (e.g. <c>locustfile.py</c>) that
    /// returns a readable stream positioned at the start of the asset content. The writer disposes
    /// the returned stream after use.
    /// </param>
    internal LocustArtifactWriter(Func<string, Stream> openEmbeddedAsset)
    {
        ArgumentNullException.ThrowIfNull(openEmbeddedAsset);
        _openEmbeddedAsset = openEmbeddedAsset;
    }

    /// <summary>
    /// Writes the flat Locust artifact for <paramref name="document"/> into
    /// <paramref name="outputDirectory"/>, replacing any existing directory at that path
    /// atomically only once every file has been produced successfully.
    /// </summary>
    /// <param name="document">The compiled intermediate representation to serialize as <c>testscript.ir.json</c>.</param>
    /// <param name="diagnostics">The diagnostics to serialize as <c>diagnostics.json</c>, in their given order.</param>
    /// <param name="outputDirectory">The target directory. Created if missing; replaced atomically if it exists.</param>
    /// <param name="cancellationToken">A token used to observe cancellation requests.</param>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> or <paramref name="diagnostics"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="outputDirectory"/> is <see langword="null"/> or blank.</exception>
    /// <exception cref="IOException"><paramref name="outputDirectory"/> already exists as a file, or an underlying I/O operation fails.</exception>
    public async Task WriteAsync(
        LocustIrDocument document,
        IReadOnlyList<LocustDiagnostic> diagnostics,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        cancellationToken.ThrowIfCancellationRequested();

        string fullOutputDirectory = Path.GetFullPath(outputDirectory);

        if (File.Exists(fullOutputDirectory))
        {
            throw new IOException(
                $"Cannot write Locust artifact to '{fullOutputDirectory}': the path already exists as a file.");
        }

        string parentDirectory = Path.GetDirectoryName(fullOutputDirectory) is { Length: > 0 } directoryName
            ? directoryName
            : Directory.GetCurrentDirectory();
        Directory.CreateDirectory(parentDirectory);

        string invocationId = Guid.NewGuid().ToString("N");
        string outputLeafName = Path.GetFileName(fullOutputDirectory);
        string stagingDirectory = Path.Combine(parentDirectory, $"{outputLeafName}.staging-{invocationId}");
        string backupDirectory = Path.Combine(parentDirectory, $"{outputLeafName}.backup-{invocationId}");

        Directory.CreateDirectory(stagingDirectory);

        try
        {
            await WriteStagingFilesAsync(stagingDirectory, document, diagnostics, cancellationToken)
                .ConfigureAwait(false);

            SwapIntoPlace(fullOutputDirectory, stagingDirectory, backupDirectory);
        }
        catch
        {
            TryDeleteDirectory(stagingDirectory);
            TryDeleteDirectory(backupDirectory);
            throw;
        }
    }

    private static void SwapIntoPlace(string fullOutputDirectory, string stagingDirectory, string backupDirectory)
    {
        bool outputExisted = Directory.Exists(fullOutputDirectory);
        if (outputExisted)
        {
            Directory.Move(fullOutputDirectory, backupDirectory);
        }

        try
        {
            Directory.Move(stagingDirectory, fullOutputDirectory);
        }
        catch
        {
            TryDeleteDirectory(fullOutputDirectory);
            if (outputExisted && Directory.Exists(backupDirectory))
            {
                Directory.Move(backupDirectory, fullOutputDirectory);
            }

            throw;
        }

        if (outputExisted)
        {
            TryDeleteDirectory(backupDirectory);
        }
    }

    private async Task WriteStagingFilesAsync(
        string stagingDirectory,
        LocustIrDocument document,
        IReadOnlyList<LocustDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string irJson = LocustIrSerializer.Serialize(document);
        await WriteUtf8NoBomFileAsync(Path.Combine(stagingDirectory, IrFileName), irJson, cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        string diagnosticsJson = SerializeDiagnostics(diagnostics);
        await WriteUtf8NoBomFileAsync(Path.Combine(stagingDirectory, DiagnosticsFileName), diagnosticsJson, cancellationToken)
            .ConfigureAwait(false);

        foreach (string assetFileName in s_assetFileNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await CopyEmbeddedAssetAsync(stagingDirectory, assetFileName, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task CopyEmbeddedAssetAsync(string stagingDirectory, string assetFileName, CancellationToken cancellationToken)
    {
        string destinationPath = Path.Combine(stagingDirectory, assetFileName);
        using Stream source = _openEmbeddedAsset(assetFileName);
        await using FileStream destination = new(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteUtf8NoBomFileAsync(string path, string content, CancellationToken cancellationToken)
    {
        byte[] bytes = s_utf8NoBom.GetBytes(content);
        await using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private static string SerializeDiagnostics(IReadOnlyList<LocustDiagnostic> diagnostics) =>
        JsonSerializer.Serialize(diagnostics, s_diagnosticsSerializerOptions);

    private static Stream OpenEmbeddedAssetFromAssembly(string assetFileName)
    {
        Assembly assembly = typeof(LocustArtifactWriter).Assembly;
        string suffix = $".Python.{assetFileName}";
        string[] matches = [.. assembly.GetManifestResourceNames().Where(name => name.EndsWith(suffix, StringComparison.Ordinal))];

        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one embedded resource ending with '{suffix}' in assembly " +
                $"'{assembly.FullName}', but found {matches.Length}.");
        }

        return assembly.GetManifestResourceStream(matches[0])
            ?? throw new InvalidOperationException($"Embedded resource '{matches[0]}' could not be opened.");
    }

    private static void EnsureNoDuplicatePlannedOutputs()
    {
        List<string> plannedOutputs = [IrFileName, DiagnosticsFileName, .. s_assetFileNames];
        var uniqueNames = new HashSet<string>(plannedOutputs, StringComparer.Ordinal);
        if (uniqueNames.Count != plannedOutputs.Count)
        {
            throw new InvalidOperationException(
                "The Locust artifact writer has a duplicate planned output file name.");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
