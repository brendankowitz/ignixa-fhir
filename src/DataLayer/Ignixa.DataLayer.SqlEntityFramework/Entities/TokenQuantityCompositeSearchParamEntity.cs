// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ignixa.DataLayer.SqlEntityFramework.Entities;

/// <summary>
/// Entity for TokenQuantityCompositeSearchParam table (Token|Quantity composite search parameters).
/// Used for composite search parameters like code-value-quantity.
/// </summary>
[Table("TokenQuantityCompositeSearchParam")]
public class TokenQuantityCompositeSearchParamEntity
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
    /// Gets or sets the system identifier for the quantity component.
    /// References the System table for unit system URI lookup.
    /// </summary>
    public int? SystemId2 { get; set; }

    /// <summary>
    /// Gets or sets the quantity code identifier.
    /// References the QuantityCode table for unit code lookup.
    /// </summary>
    public int? QuantityCodeId { get; set; }

    /// <summary>
    /// Gets or sets the single low value for quantity comparison.
    /// </summary>
    public decimal? SingleValue { get; set; }

    /// <summary>
    /// Gets or sets the low value for quantity range comparison.
    /// </summary>
    public decimal? LowValue { get; set; }

    /// <summary>
    /// Gets or sets the high value for quantity range comparison.
    /// </summary>
    public decimal? HighValue { get; set; }
}
