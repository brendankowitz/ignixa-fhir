// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ignixa.DataLayer.SqlEntityFramework.Entities;

/// <summary>
/// Entity for TokenNumberNumberCompositeSearchParam table (Token|Number|Number composite search parameters).
/// Used for composite search parameters like component-value-concept on MolecularSequence.
/// </summary>
[Table("TokenNumberNumberCompositeSearchParam")]
public class TokenNumberNumberCompositeSearchParamEntity
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
    /// Gets or sets the single low value for the first number component.
    /// </summary>
    public decimal? SingleValue2 { get; set; }

    /// <summary>
    /// Gets or sets the low value for the first number range comparison.
    /// </summary>
    public decimal? LowValue2 { get; set; }

    /// <summary>
    /// Gets or sets the high value for the first number range comparison.
    /// </summary>
    public decimal? HighValue2 { get; set; }

    /// <summary>
    /// Gets or sets the single low value for the second number component.
    /// </summary>
    public decimal? SingleValue3 { get; set; }

    /// <summary>
    /// Gets or sets the low value for the second number range comparison.
    /// </summary>
    public decimal? LowValue3 { get; set; }

    /// <summary>
    /// Gets or sets the high value for the second number range comparison.
    /// </summary>
    public decimal? HighValue3 { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether there are number overflow values.
    /// </summary>
    public bool HasRange { get; set; }
}
