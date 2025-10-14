// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Sparky.Extensions.Models;

/// <summary>
/// Represents a FHIR Coding (system, version, code, display).
/// Lightweight alternative to Hl7.Fhir.Model.Coding.
/// </summary>
public class CodingInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CodingInfo"/> class.
    /// </summary>
    public CodingInfo()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CodingInfo"/> class.
    /// </summary>
    /// <param name="system">The code system URI.</param>
    /// <param name="code">The code value.</param>
    public CodingInfo(string system, string code)
    {
        System = system;
        Code = code;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CodingInfo"/> class.
    /// </summary>
    /// <param name="system">The code system URI.</param>
    /// <param name="code">The code value.</param>
    /// <param name="display">The display text.</param>
    public CodingInfo(string system, string code, string display)
        : this(system, code)
    {
        Display = display;
    }

    /// <summary>
    /// Gets or sets the code system URI (e.g., "http://hl7.org/fhir/observation-status").
    /// </summary>
    public string System { get; set; }

    /// <summary>
    /// Gets or sets the version of the code system (optional).
    /// </summary>
    public string Version { get; set; }

    /// <summary>
    /// Gets or sets the code value (e.g., "final").
    /// </summary>
    public string Code { get; set; }

    /// <summary>
    /// Gets or sets the human-readable display text.
    /// </summary>
    public string Display { get; set; }

    /// <summary>
    /// Gets or sets whether this coding was chosen directly by the user.
    /// </summary>
    public bool? UserSelected { get; set; }
}
