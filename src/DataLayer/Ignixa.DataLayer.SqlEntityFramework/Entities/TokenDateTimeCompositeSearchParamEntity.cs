// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ignixa.DataLayer.SqlEntityFramework.Entities;

/// <summary>
/// Entity for TokenDateTimeCompositeSearchParam table (Token|DateTime composite search parameters).
/// </summary>
[Table("TokenDateTimeCompositeSearchParam")]
public class TokenDateTimeCompositeSearchParamEntity
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
    /// Gets or sets the system identifier for the token component.
    /// References the System table for system URI lookup.
    /// </summary>
    public int? SystemId1 { get; set; }

    /// <summary>
    /// Gets or sets the code for the token component.
    /// </summary>
    [Required]
    [MaxLength(256)]
    public string Code1 { get; set; } = null!;

    /// <summary>
    /// Gets or sets overflow code for the token component (when code exceeds 256 chars).
    /// </summary>
    public string? CodeOverflow1 { get; set; }

    /// <summary>
    /// Gets or sets the start date/time for the datetime component.
    /// </summary>
    public DateTime StartDateTime2 { get; set; }

    /// <summary>
    /// Gets or sets the end date/time for the datetime component.
    /// </summary>
    public DateTime EndDateTime2 { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is the minimum value.
    /// </summary>
    public bool IsMin { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is the maximum value.
    /// </summary>
    public bool IsMax { get; set; }
}
