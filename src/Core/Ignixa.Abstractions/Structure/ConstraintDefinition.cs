// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Abstractions;

/// <summary>
/// Concrete implementation of IConstraint for codegen use.
/// </summary>
public sealed class ConstraintDefinition : IConstraint
{
    public required string Key { get; init; }
    public required string Expression { get; init; }
    public string? Human { get; init; }
    public required string Severity { get; init; }
    public string? Xpath { get; init; }
}
