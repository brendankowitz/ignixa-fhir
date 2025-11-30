// <copyright file="IValidationSchemaResolver.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

namespace Ignixa.Validation.Abstractions;

/// <summary>
/// Resolves validation schemas by canonical URL.
/// Implementations may cache compiled schemas for performance.
/// </summary>
public interface IValidationSchemaResolver
{
    /// <summary>
    /// Gets the validation schema for a given canonical URL (e.g., StructureDefinition URL).
    /// </summary>
    /// <param name="canonicalUrl">The canonical URL of the schema to retrieve.</param>
    /// <returns>The validation schema, or null if not found.</returns>
    ValidationSchema? GetSchema(string canonicalUrl);
}
