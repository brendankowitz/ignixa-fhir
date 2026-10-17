// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary>What answered: the local store (with the package or folder file) or a remote terminology server.</summary>
/// <param name="Kind"><c>local</c> or <c>tx-server</c>.</param>
/// <param name="Package">For a local answer from a package, <c>id@version</c>; null for the folder or a server.</param>
/// <param name="File">For a local answer, the file within the package or the folder.</param>
/// <param name="Server">For a remote answer, the server's base URL.</param>
/// <param name="ReportedVersion">For a remote answer, the code system (or ValueSet) version the server reported; null when it reported none.</param>
public sealed record AnswerSource(string Kind, string? Package, string? File, string? Server, string? ReportedVersion)
{
    public const string LocalKind = "local";
    public const string TxServerKind = "tx-server";

    public static AnswerSource Local(ResourceOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(origin);

        return new AnswerSource(LocalKind, origin.Source.IsFolder ? null : origin.Source.Name, origin.File, Server: null, ReportedVersion: null);
    }

    public static AnswerSource Remote(Uri server, string? reportedVersion)
    {
        ArgumentNullException.ThrowIfNull(server);

        return new AnswerSource(TxServerKind, Package: null, File: null, server.AbsoluteUri, reportedVersion);
    }
}
