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
    private readonly Action<string, string> _moveDirectory;

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
    /// from the embedded resources of this assembly, and moves directories with
    /// <see cref="Directory.Move(string, string)"/>.
    /// </summary>
    public LocustArtifactWriter()
        : this(OpenEmbeddedAssetFromAssembly, MoveDirectory)
    {
    }

    /// <summary>
    /// Creates a writer that resolves embedded assets through <paramref name="openEmbeddedAsset"/>
    /// and moves directories with <see cref="Directory.Move(string, string)"/>. Used by tests to
    /// inject asset content or simulate asset-resolution failures.
    /// </summary>
    /// <param name="openEmbeddedAsset">
    /// A delegate invoked with each logical asset file name (e.g. <c>locustfile.py</c>) that
    /// returns a readable stream positioned at the start of the asset content. The writer disposes
    /// the returned stream after use.
    /// </param>
    internal LocustArtifactWriter(Func<string, Stream> openEmbeddedAsset)
        : this(openEmbeddedAsset, MoveDirectory)
    {
    }

    /// <summary>
    /// Creates a writer that resolves embedded assets through <paramref name="openEmbeddedAsset"/>
    /// and moves directories through <paramref name="moveDirectory"/>. Used by tests to
    /// deterministically simulate directory-move failures during the atomic swap.
    /// </summary>
    /// <param name="openEmbeddedAsset">See <see cref="LocustArtifactWriter(Func{string, Stream})"/>.</param>
    /// <param name="moveDirectory">
    /// A delegate invoked as <c>moveDirectory(sourceDirName, destDirName)</c> in place of
    /// <see cref="Directory.Move(string, string)"/> for every directory move performed by the
    /// atomic swap.
    /// </param>
    internal LocustArtifactWriter(Func<string, Stream> openEmbeddedAsset, Action<string, string> moveDirectory)
    {
        ArgumentNullException.ThrowIfNull(openEmbeddedAsset);
        ArgumentNullException.ThrowIfNull(moveDirectory);
        _openEmbeddedAsset = openEmbeddedAsset;
        _moveDirectory = moveDirectory;
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
    /// <exception cref="ArgumentException">
    /// <paramref name="outputDirectory"/> is <see langword="null"/> or blank, or resolves to a filesystem root.
    /// </exception>
    /// <exception cref="IOException">
    /// <paramref name="outputDirectory"/> already exists as a file, an underlying I/O operation fails, or the
    /// atomic swap could not complete (see the exception message for recovery details).
    /// </exception>
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

        string fullOutputDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory));

        if (File.Exists(fullOutputDirectory))
        {
            throw new IOException(
                $"Cannot write Locust artifact to '{fullOutputDirectory}': the path already exists as a file.");
        }

        string outputLeafName = Path.GetFileName(fullOutputDirectory);
        string? parentDirectory = Path.GetDirectoryName(fullOutputDirectory);

        if (string.IsNullOrEmpty(outputLeafName) || string.IsNullOrEmpty(parentDirectory))
        {
            throw new ArgumentException(
                $"Output directory '{fullOutputDirectory}' must not be a filesystem root.",
                nameof(outputDirectory));
        }

        Directory.CreateDirectory(parentDirectory);

        string invocationId = Guid.NewGuid().ToString("N");
        string stagingDirectory = Path.Combine(parentDirectory, $"{outputLeafName}.staging-{invocationId}");
        string backupDirectory = Path.Combine(parentDirectory, $"{outputLeafName}.backup-{invocationId}");

        Directory.CreateDirectory(stagingDirectory);

        try
        {
            await WriteStagingFilesAsync(stagingDirectory, document, diagnostics, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            TryDeleteDirectory(stagingDirectory);
            throw;
        }

        SwapIntoPlace(fullOutputDirectory, stagingDirectory, backupDirectory);
    }

    /// <summary>
    /// Replaces <paramref name="fullOutputDirectory"/> with the completed
    /// <paramref name="stagingDirectory"/>, using <paramref name="backupDirectory"/> as a sibling
    /// holding area for any pre-existing output directory during the swap.
    /// </summary>
    /// <remarks>
    /// If the output directory does not yet exist, the swap is a single move with no backup
    /// involved. If it exists, the original is first moved to <paramref name="backupDirectory"/>;
    /// the backup is only deleted once the new content is confirmed in place. If moving the
    /// staging directory into place fails, the writer attempts to restore the backup: on
    /// successful restore, the original swap failure is rethrown; on a double fault (restore also
    /// fails), the backup is deliberately left in place for manual recovery and both failures are
    /// reported together.
    /// </remarks>
    private void SwapIntoPlace(string fullOutputDirectory, string stagingDirectory, string backupDirectory)
    {
        bool outputExisted = Directory.Exists(fullOutputDirectory);

        if (!outputExisted)
        {
            try
            {
                _moveDirectory(stagingDirectory, fullOutputDirectory);
            }
            catch
            {
                TryDeleteDirectory(fullOutputDirectory);
                TryDeleteDirectory(stagingDirectory);
                throw;
            }

            return;
        }

        try
        {
            _moveDirectory(fullOutputDirectory, backupDirectory);
        }
        catch
        {
            // The original output was not (or only partially) moved away, so there is nothing to
            // restore; the staging attempt is simply abandoned and the original is left as-is.
            TryDeleteDirectory(stagingDirectory);
            throw;
        }

        try
        {
            _moveDirectory(stagingDirectory, fullOutputDirectory);
        }
        catch (Exception swapException)
        {
            TryDeleteDirectory(fullOutputDirectory);

            try
            {
                _moveDirectory(backupDirectory, fullOutputDirectory);
            }
            catch (Exception restoreException)
            {
                // Double fault: neither the new artifact nor the restored original could be placed
                // at the output path. Preserve the backup untouched for manual recovery -- never
                // delete a backup we could not prove was safely restored -- and report both
                // failures together.
                TryDeleteDirectory(stagingDirectory);
                throw new IOException(
                    $"Failed to write the Locust artifact to '{fullOutputDirectory}' and failed to " +
                    "restore the original content from backup afterward. The original content is " +
                    $"preserved, untouched, at '{backupDirectory}' and must be recovered manually.",
                    new AggregateException(swapException, restoreException));
            }

            TryDeleteDirectory(stagingDirectory);
            throw;
        }

        DeleteBackupAfterSuccessfulSwap(backupDirectory);
    }

    /// <summary>
    /// Deletes <paramref name="backupDirectory"/> now that the new output has been placed
    /// successfully. Unlike <see cref="TryDeleteDirectory"/>, this does not swallow failures: if
    /// the backup cannot be removed, an explicit exception is thrown naming the leftover path
    /// rather than silently reporting success.
    /// </summary>
    private static void DeleteBackupAfterSuccessfulSwap(string backupDirectory)
    {
        try
        {
            if (Directory.Exists(backupDirectory))
            {
                Directory.Delete(backupDirectory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException(
                "The Locust artifact was written successfully, but the backup directory " +
                $"'{backupDirectory}' could not be removed and must be deleted manually.",
                ex);
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

    private static void MoveDirectory(string sourceDirName, string destDirName) =>
        Directory.Move(sourceDirName, destDirName);

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
