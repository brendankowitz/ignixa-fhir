// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;

namespace Ignixa.SqlOnFhir.Fabric.Delta;

/// <summary>
/// An existing table directory cannot be used for a <see cref="FabricSilverContract"/>: it is not the table the
/// contract describes, or it is not a Delta table at all. Nothing is corrected automatically.
/// </summary>
/// <remarks>
/// The message names the <see cref="Aspect"/> and, where one applies, the contract property, but never contains schema
/// text, property values or undeclared property names read from the table.
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1032:Implement standard exception constructors",
    Justification = "Every mismatch names its aspect; a parameterless or message-only constructor would create one that does not.")]
public sealed class FabricSilverTableMismatchException : Exception
{
    internal FabricSilverTableMismatchException(FabricSilverTableAspect aspect, string? propertyName, string message)
        : base(message)
    {
        Aspect = aspect;
        PropertyName = propertyName;
    }

    /// <summary>The part of the table that does not match.</summary>
    public FabricSilverTableAspect Aspect { get; }

    /// <summary>
    /// The contract table property at fault for <see cref="FabricSilverTableAspect.ChangeDataFeed"/> and
    /// <see cref="FabricSilverTableAspect.Configuration"/>; otherwise <see langword="null"/>.
    /// </summary>
    public string? PropertyName { get; }
}
