// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.NarrativeGenerator;
using Ignixa.NarrativeGenerator.Engine;
using Ignixa.NarrativeGenerator.Engine.ScriptFunctions;
using Ignixa.NarrativeGenerator.Security;
using Microsoft.Extensions.Localization;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering narrative generation services in the dependency injection container.
/// </summary>
public static class NarrativeGeneratorServiceCollectionExtensions
{
    /// <summary>
    /// Adds narrative generation services to the service collection.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The service collection for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method registers the following services as singletons:
    /// </para>
    /// <list type="bullet">
    ///   <item><see cref="ITemplateResolver"/> - Resolves Scriban templates from embedded resources</item>
    ///   <item><see cref="FhirPathScriptFunctions"/> - Provides FHIRPath functions for templates</item>
    ///   <item><see cref="NarrativeTemplateEngine"/> - Renders Scriban templates</item>
    ///   <item><see cref="XhtmlSanitizer"/> - Sanitizes generated HTML for XSS protection</item>
    ///   <item><see cref="INarrativeGenerator"/> - Main orchestrator for narrative generation</item>
    /// </list>
    /// <para>
    /// This method also registers localization services via <c>AddLocalization()</c>.
    /// </para>
    /// <para>
    /// <strong>Prerequisites:</strong> An <see cref="ISchema"/> implementation must be registered
    /// in the service collection before calling this method, as it is required by
    /// <see cref="FhirPathScriptFunctions"/>.
    /// </para>
    /// <para>
    /// <strong>Alternative Usage:</strong> For scenarios without dependency injection,
    /// use the static factory method <see cref="FhirNarrativeGenerator.Create(ISchema, IStringLocalizer?)"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddSingleton&lt;ISchema&gt;(sp => new JsonSchema(FhirVersion.R4));
    /// services.AddNarrativeGenerator();
    /// </code>
    /// </example>
    public static IServiceCollection AddNarrativeGenerator(this IServiceCollection services)
    {
        // Note: Localization is optional - callers can register IStringLocalizer if needed

        // Register template resolver (loads templates from embedded resources)
        services.AddSingleton<ITemplateResolver, TemplateResolver>();

        // Register FHIRPath script functions (requires ISchema)
        services.AddSingleton<FhirPathScriptFunctions>(sp =>
        {
            var schema = sp.GetRequiredService<ISchema>();
            return new FhirPathScriptFunctions(schema);
        });

        // Register template engine with optional localization
        services.AddSingleton<NarrativeTemplateEngine>(sp =>
        {
            var fhirPathFunctions = sp.GetRequiredService<FhirPathScriptFunctions>();

            // Try to get optional IStringLocalizer for localization support
            var stringLocalizer = sp.GetService<Microsoft.Extensions.Localization.IStringLocalizer>();

            return new NarrativeTemplateEngine(fhirPathFunctions, stringLocalizer);
        });

        // Register XHTML sanitizer
        services.AddSingleton<XhtmlSanitizer>();

        // Register main narrative generator (requires ISchema, ITemplateResolver, NarrativeTemplateEngine, XhtmlSanitizer)
        services.AddSingleton<INarrativeGenerator>(sp =>
        {
            var templateResolver = sp.GetRequiredService<ITemplateResolver>();
            var templateEngine = sp.GetRequiredService<NarrativeTemplateEngine>();
            var sanitizer = sp.GetRequiredService<XhtmlSanitizer>();
            var schema = sp.GetRequiredService<ISchema>();

            return new FhirNarrativeGenerator(templateResolver, templateEngine, sanitizer, schema);
        });

        return services;
    }
}
