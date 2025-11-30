// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ignixa.DataLayer.SqlEntityFramework.Entities;

/// <summary>
/// Entity representing the System table.
/// Stores system URIs (code systems, identifier systems) with unique IDs.
/// </summary>
[Table("System")]
public class SystemEntity
{
    /// <summary>
    /// Gets or sets the unique identifier for the system.
    /// </summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int SystemId { get; set; }

    /// <summary>
    /// Gets or sets the system URI (e.g., "http://loinc.org", "http://snomed.info/sct").
    /// </summary>
    [Required]
    [MaxLength(450)]
    public string Value { get; set; } = null!;
}
