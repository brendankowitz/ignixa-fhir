// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ignixa.DataLayer.SqlEntityFramework.Entities;

/// <summary>
/// Entity for TokenTokenCompositeSearchParam table (Token|Token composite search parameters).
/// Used for composite search parameters like combo-code-value-concept.
/// </summary>
[Table("TokenTokenCompositeSearchParam")]
public class TokenTokenCompositeSearchParamEntity
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
    /// Gets or sets the system identifier for the first token component.
    /// References the System table for system URI lookup.
    /// </summary>
    public int? SystemId1 { get; set; }

    /// <summary>
    /// Gets or sets the code for the first token component.
    /// </summary>
    [Required]
    [MaxLength(256)]
    public string Code1 { get; set; } = null!;

    /// <summary>
    /// Gets or sets overflow code for the first token component (when code exceeds 256 chars).
    /// </summary>
    public string? CodeOverflow1 { get; set; }

    /// <summary>
    /// Gets or sets the system identifier for the second token component.
    /// References the System table for system URI lookup.
    /// </summary>
    public int? SystemId2 { get; set; }

    /// <summary>
    /// Gets or sets the code for the second token component.
    /// </summary>
    [Required]
    [MaxLength(256)]
    public string Code2 { get; set; } = null!;

    /// <summary>
    /// Gets or sets overflow code for the second token component (when code exceeds 256 chars).
    /// </summary>
    public string? CodeOverflow2 { get; set; }
}
