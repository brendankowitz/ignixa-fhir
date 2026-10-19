// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Mcp;

/// <summary>
/// A <see cref="System.Text.Json.JsonElement"/> argument that takes a JSON object: its input schema says <c>{"type":"object"}</c>
/// rather than <c>true</c> (anything), so a client sees what to send. The tool still checks the value itself, and fails a value
/// of another kind with its own error.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class ObjectArgumentAttribute : Attribute;
