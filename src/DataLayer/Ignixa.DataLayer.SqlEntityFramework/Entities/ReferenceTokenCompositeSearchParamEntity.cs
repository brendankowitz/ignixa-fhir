// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ignixa.DataLayer.SqlEntityFramework.Entities;

/// <summary>
/// Entity for ReferenceTokenCompositeSearchParam table (Reference|Token composite search parameters).
/// Used for composite search parameters like relationship on DocumentReference.
/// </summary>
[Table("ReferenceTokenCompositeSearchParam")]
public class ReferenceTokenCompositeSearchParamEntity
{
    /// <summary>
    /// Gets or sets the resource type identifier.
    /// </summary>
    public short ResourceTypeId { get; set; }

    /// <summary>
    /// Gets or sets the resource surrogate identifier.
    /// </summary>
    public long ResourceSurrogateId { get; set; }

    /// <summary>
    /// Gets or sets the search parameter identifier.
    /// </summary>
    public short SearchParamId { get; set; }

    /// <summary>
    /// Gets or sets the base URI for external references.
    /// </summary>
    [MaxLength(128)]
    public string? BaseUri1 { get; set; }

    /// <summary>
    /// Gets or sets the reference resource type identifier for the reference component.
    /// </summary>
    public short? ReferenceResourceTypeId1 { get; set; }

    /// <summary>
    /// Gets or sets the reference resource ID for the reference component.
    /// </summary>
    [Required]
    [MaxLength(64)]
    public string ReferenceResourceId1 { get; set; } = null!;

    /// <summary>
    /// Gets or sets the system identifier for the token component.
    /// References the System table for system URI lookup.
    /// </summary>
    public int? SystemId2 { get; set; }

    /// <summary>
    /// Gets or sets the code for the token component.
    /// </summary>
    [Required]
    [MaxLength(256)]
    public string Code2 { get; set; } = null!;
}
