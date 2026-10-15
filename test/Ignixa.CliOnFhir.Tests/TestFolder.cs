// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Tests;

/// <summary>A temporary folder of test files, deleted on dispose.</summary>
internal sealed class TestFolder : IDisposable
{
    public TestFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cli-on-fhir-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public TestFolder Write(string relativePath, string content)
    {
        string fullPath = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return this;
    }

    public byte[] ReadBytes(string relativePath) => File.ReadAllBytes(System.IO.Path.Combine(Path, relativePath));

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
