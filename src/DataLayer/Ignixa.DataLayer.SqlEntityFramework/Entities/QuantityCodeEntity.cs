// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ignixa.DataLayer.SqlEntityFramework.Entities;

/// <summary>
/// Entity representing the QuantityCode table.
/// Stores unit codes for quantity search parameters with unique IDs.
/// </summary>
[Table("QuantityCode")]
public class QuantityCodeEntity
{
    /// <summary>
    /// Gets or sets the unique identifier for the quantity code.
    /// </summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int QuantityCodeId { get; set; }

    /// <summary>
    /// Gets or sets the unit code (e.g., "mg", "kg", "mmol/L").
    /// </summary>
    [Required]
    [MaxLength(256)]
    public string Value { get; set; } = null!;
}
