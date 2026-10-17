// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Output;

namespace Ignixa.CliOnFhir.Drafting;

/// <summary>
/// Writes a draft to <c>&lt;Type&gt;-&lt;id&gt;.json</c> in a directory outside the data folder, with the output machinery every
/// verb that writes uses (<see cref="OutputDirectory"/>, <see cref="OutputTarget"/>, <see cref="OutputFile"/>): the directory is
/// refused inside, around or (on Linux, by device and inode, bind mounts included) the same as the folder; the file is written to a
/// temporary file beside it, created exclusively and checked by its open handle; an existing file is replaced only with
/// <c>overwrite</c>, and only by the complete draft, in one atomic rename (as <c>view --out</c> replaces its one file), so a run that
/// fails leaves it as it was.
/// </summary>
internal static class DraftOutput
{
    /// <summary>The format the target is labelled with: one FHIR JSON resource.</summary>
    public const string Format = "json";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The checked file the draft would be written to, refused before anything else is done.</summary>
    /// <exception cref="ToolRequestException"><c>out-refused</c>.</exception>
    public static OutputTarget Target(string folderPath, string directory, string resourceType, string id, bool overwrite)
    {
        OutputDirectory checkedDirectory = OutputDirectory.Resolve(folderPath, directory, overwrite);
        return OutputTarget.ForFile(folderPath, Path.Combine(checkedDirectory.Path, $"{resourceType}-{id}.json"), Format, overwrite);
    }

    /// <summary>Writes <paramref name="resource"/> to <paramref name="target"/>; what could not be cleaned up is in <paramref name="notes"/>.</summary>
    /// <exception cref="ToolRequestException"><c>out-refused</c> or <c>out-write-failed</c>; the existing file, if any, is as it was unless a note says otherwise.</exception>
    public static async Task<DraftFileFacts> WriteAsync(OutputTarget target, JsonObject resource, List<IssueItem> notes, DraftOutputSeams? seams, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(notes);

        byte[] bytes = Utf8.GetBytes(resource.ToJsonString(Indented) + "\n");
        OutputFile file = target.Begin(notes, seams?.BeforeCreate);
        try
        {
            seams?.AfterBegin?.Invoke(file);
            await file.Stream.WriteAsync(bytes, cancellationToken);
            await file.Stream.FlushAsync(cancellationToken);
            file.Commit();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Note(file.Discard(), notes);
            throw ToolRequestException.Error("out-write-failed", $"Writing '{target.Path}' failed: {ex.Message}. The partial file was removed unless a note says otherwise.");
        }
        catch
        {
            Note(file.Discard(), notes);
            throw;
        }

        return new DraftFileFacts(target.Path, bytes.Length);
    }

    private static void Note(IssueItem? left, List<IssueItem> notes)
    {
        if (left is not null)
        {
            notes.Add(left);
        }
    }
}
