// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Abstractions;

/// <summary>
/// Marks a command or query as requiring specific server capabilities.
/// Implementations provide a FHIRPath expression that is evaluated against the server's CapabilityStatement.
/// The expression should return a boolean value indicating whether the capability is supported.
/// </summary>
/// <example>
/// <code>
/// public record GetResourceQuery(string ResourceType, string Id)
///     : IRequest&lt;SearchEntryResult?&gt;, IRequiresCapability
/// {
///     public string GetCapabilityRequirementExpression() =>
///         "rest.resource.where(type = %resourceType).interaction.where(code = 'read').exists()";
///
///     public string? CapabilityResourceType => ResourceType;
/// }
/// </code>
/// </example>
public interface IRequireCapability
{
    /// <summary>
    /// Returns a FHIRPath expression to validate against the server's CapabilityStatement.
    /// The expression must return a boolean value (true if capability is supported, false otherwise).
    /// </summary>
    /// <returns>FHIRPath expression string.</returns>
    /// <example>
    /// <list type="bullet">
    /// <item>Read: <c>rest.resource.where(type = %resourceType).interaction.where(code = 'read').exists()</c></item>
    /// <item>Search: <c>rest.resource.where(type = %resourceType).interaction.where(code = 'search-type').exists()</c></item>
    /// <item>Update: <c>rest.resource.where(type = %resourceType).interaction.where(code = 'update').exists()</c></item>
    /// </list>
    /// </example>
    string GetCapabilityRequirementExpression();

    /// <summary>
    /// Gets the resource type bound to <c>%resourceType</c> when the expression is evaluated, or
    /// <c>null</c> when the expression does not reference it.
    /// Request values must reach the expression through this variable and never through string
    /// interpolation: a value such as <c>Patient' or true or type = '</c> would otherwise be parsed as
    /// expression syntax, and every distinct value would add an entry to the process-wide expression cache.
    /// </summary>
    string? CapabilityResourceType => null;
}
