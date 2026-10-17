// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Output;

/// <summary>
/// The NDJSON files one run writes into an <see cref="OutputDirectory"/>, <c>&lt;Type&gt;.ndjson</c> per resource type, all or none.
/// Each file is an <see cref="OutputFile"/>: a temporary file beside its target, created exclusively, checked by its open handle and
/// held open until the end. A file is begun when its first resource is written. <see cref="CommitAsync"/> closes every file,
/// prepares every one (each must still be where it was created) before it renames any, and only then renames them into place; a run
/// that fails calls <see cref="DiscardAsync"/>, which leaves no new file and every existing one as it was. Not thread-safe.
/// </summary>
/// <remarks>
/// The renames are one per file, so they are not one atomic step. Each existing target is renamed aside before its file takes its
/// place (<see cref="OutputFile.CommitWithBackup"/>), and a rename that fails after others succeeded rolls those back: the new files
/// are removed and the old ones restored, through the checked directory only (<c>out-rollback-left</c> when that cannot be done).
/// </remarks>
/// <param name="directory">The checked directory.</param>
/// <param name="notes">Receives what the run could not clean up (<c>out-temp-left</c>, <c>out-directory-left</c>) and other notes on the files.</param>
internal sealed partial class NdjsonDirectory(OutputDirectory directory, List<IssueItem> notes)
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Dictionary<string, OutputTarget> _targets = new(StringComparer.Ordinal);
    private readonly List<TypeFile> _files = [];
    private readonly Dictionary<string, TypeFile> _byType = new(StringComparer.Ordinal);

    /// <summary>Runs after each file is begun and before anything is written to it; a test swaps its directory there.</summary>
    internal Action<OutputFile>? AfterBegin { get; init; }

    /// <summary>The directory written into.</summary>
    public string Path => directory.Path;

    /// <summary>
    /// Checks the file <paramref name="resourceType"/>'s resources go to, so that a run can refuse before it reads anything: it must not
    /// exist unless <c>overwrite</c> was given, and must not be a directory.
    /// </summary>
    /// <exception cref="ToolRequestException"><c>out-refused</c>.</exception>
    public void Check(string resourceType) => Target(resourceType);

    /// <summary>Writes one resource, compact JSON on one line, to its type's file, beginning the file if this is its first.</summary>
    /// <exception cref="ToolRequestException"><c>out-refused</c> or <c>out-write-failed</c>: the file could not be begun.</exception>
    public async Task WriteAsync(string resourceType, string json, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(json);

        if (!_byType.TryGetValue(resourceType, out TypeFile? file))
        {
            file = Begin(resourceType);
        }

        await file.Writer.WriteAsync(json.AsMemory(), cancellationToken);
        await file.Writer.WriteAsync("\n".AsMemory(), cancellationToken);
        file.Resources++;
        file.Bytes += Utf8.GetByteCount(json) + 1;
    }

    /// <summary>
    /// Closes every file and, once each has been checked to still be where it was created, renames each over its target. An existing
    /// file of a type the run wrote nothing for is left as it was, with an <c>out-kept</c> note.
    /// </summary>
    /// <returns>The files written, in the order they were begun.</returns>
    /// <exception cref="ToolRequestException">
    /// <c>out-refused</c> or <c>out-write-failed</c>. What was renamed before the failure has been rolled back (or an
    /// <c>out-rollback-left</c> warning in the notes says what could not be). The caller then calls <see cref="DiscardAsync"/>.
    /// </exception>
    public async Task<IReadOnlyList<NdjsonFileFacts>> CommitAsync()
    {
        foreach (TypeFile file in _files)
        {
            await CloseAsync(file);
        }

        foreach (TypeFile file in _files)
        {
            file.Output.Prepare();
        }

        for (int i = 0; i < _files.Count; i++)
        {
            try
            {
                _files[i].Output.CommitWithBackup();
            }
            catch (ToolRequestException)
            {
                RollBack(i);
                throw;
            }
        }

        foreach (TypeFile file in _files)
        {
            if (file.Output.DropBackup() is { } left)
            {
                notes.Add(left);
            }
        }

        foreach ((string resourceType, OutputTarget target) in _targets)
        {
            if (!_byType.ContainsKey(resourceType) && File.Exists(target.Path))
            {
                notes.Add(new IssueItem("information", "out-kept", $"The existing file '{target.Path}' was left as it was: the run wrote no {resourceType} resource, so it holds what an earlier run wrote.", Location: null));
            }
        }

        return [.. _files.Select(file => new NdjsonFileFacts(file.ResourceType, file.Target.Path, file.Resources, file.Bytes))];
    }

    // Puts back what the files up to and including the one that failed changed, last first: the files renamed into place are removed
    // and the targets they replaced restored, so a failed run leaves no new file and every existing one as it was. What cannot be put
    // back is a warning in the notes.
    private void RollBack(int failed)
    {
        for (int i = failed; i >= 0; i--)
        {
            if (_files[i].Output.Rollback() is { } left)
            {
                notes.Add(left);
            }
        }
    }

    /// <summary>
    /// Ends a run that does not commit: closes every file and removes it and the directories the run created, last begun first, through
    /// <see cref="OutputFile.Discard"/>, which deletes nothing through a moved or replaced directory and reports what it left. A file
    /// already renamed into place by a commit that failed part-way is kept. Nothing here throws for a file system failure.
    /// </summary>
    public async Task DiscardAsync()
    {
        for (int i = _files.Count - 1; i >= 0; i--)
        {
            try
            {
                await CloseAsync(_files[i]);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // What could not be flushed belonged to a result that is being thrown away; the file is discarded next.
            }

            if (_files[i].Output.Discard() is { } left)
            {
                notes.Add(left);
            }
        }
    }

    /// <summary>The file name of <paramref name="resourceType"/>'s resources: <c>&lt;Type&gt;.ndjson</c>.</summary>
    /// <exception cref="ArgumentException">Not a resource type name (a capital letter, then letters and digits), so not safe as a file name.</exception>
    public static string FileName(string resourceType)
    {
        ArgumentNullException.ThrowIfNull(resourceType);

        return TypeName().IsMatch(resourceType)
            ? $"{resourceType}.ndjson"
            : throw new ArgumentException("A resource type name is a capital letter followed by letters and digits.", nameof(resourceType));
    }

    private static async Task CloseAsync(TypeFile file)
    {
        if (!file.Closed)
        {
            file.Closed = true;
            await file.Writer.DisposeAsync();
        }
    }

    private OutputTarget Target(string resourceType)
    {
        if (!_targets.TryGetValue(resourceType, out OutputTarget? target))
        {
            target = directory.File(FileName(resourceType));
            _targets.Add(resourceType, target);
        }

        return target;
    }

    private TypeFile Begin(string resourceType)
    {
        OutputTarget target = Target(resourceType);
        OutputFile output = target.Begin(notes);
        var file = new TypeFile(resourceType, target, output);
        _files.Add(file);
        _byType.Add(resourceType, file);
        AfterBegin?.Invoke(output);
        return file;
    }

    [GeneratedRegex("^[A-Z][A-Za-z0-9]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex TypeName();

    // One type's file: its target, the open temporary file, and the writer over it, which closes only its own buffer (UnclosedStream).
    [SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "CloseAsync ends every writer, on commit and on discard alike; the file under it is ended by OutputFile.Commit or Discard.")]
    private sealed class TypeFile(string resourceType, OutputTarget target, OutputFile output)
    {
        public string ResourceType { get; } = resourceType;

        public OutputTarget Target { get; } = target;

        public OutputFile Output { get; } = output;

        public StreamWriter Writer { get; } = new(output.Stream, Utf8);

        public long Resources { get; set; }

        public long Bytes { get; set; }

        public bool Closed { get; set; }
    }
}
