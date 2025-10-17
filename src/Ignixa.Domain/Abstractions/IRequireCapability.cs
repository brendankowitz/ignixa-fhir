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
///         $"rest.resource.where(type = '{ResourceType}').interaction.where(code = 'read').exists()";
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
    /// <item>Read: <c>rest.resource.where(type = 'Patient').interaction.where(code = 'read').exists()</c></item>
    /// <item>Search: <c>rest.resource.where(type = 'Observation').interaction.where(code = 'search-type').exists()</c></item>
    /// <item>Update: <c>rest.resource.where(type = 'Condition').interaction.where(code = 'update').exists()</c></item>
    /// </list>
    /// </example>
    string GetCapabilityRequirementExpression();
}
