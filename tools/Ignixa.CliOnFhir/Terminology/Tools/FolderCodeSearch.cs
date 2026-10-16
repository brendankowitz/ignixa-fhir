// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary>A search parameter through which a resource type carries some of the codes.</summary>
/// <param name="ResourceType">The resource type.</param>
/// <param name="Parameter">The token search parameter, for example <c>code</c>.</param>
/// <param name="Resources">Resources of the type carrying any of the codes through it.</param>
public sealed record FolderCodeSearch(string ResourceType, string Parameter, int Resources);
