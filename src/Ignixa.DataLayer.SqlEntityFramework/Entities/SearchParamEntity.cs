// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ignixa.DataLayer.SqlEntityFramework.Entities;

/// <summary>
/// Entity representing the SearchParam table.
/// Stores search parameter definitions with unique IDs.
/// </summary>
[Table("SearchParam")]
public class SearchParamEntity
{
    /// <summary>
    /// Gets or sets the unique identifier for the search parameter.
    /// </summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public short SearchParamId { get; set; }

    /// <summary>
    /// Gets or sets the URI of the search parameter (e.g., "http://hl7.org/fhir/SearchParameter/Patient-name").
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string Uri { get; set; } = null!;

    /// <summary>
    /// Gets or sets the status of the search parameter (e.g., "active", "draft", "retired").
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = null!;

    /// <summary>
    /// Gets or sets the last updated timestamp for this search parameter.
    /// </summary>
    [Required]
    public DateTimeOffset LastUpdated { get; set; }

    /// <summary>
    /// Gets or sets whether this search parameter is partially supported.
    /// </summary>
    public bool IsPartiallySupported { get; set; }
}
