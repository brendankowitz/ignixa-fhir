// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;

namespace Ignixa.NarrativeGenerator.Engine;

/// <summary>
/// Scriban template loader that uses <see cref="ITemplateResolver"/> to resolve template includes.
/// </summary>
/// <remarks>
/// <para>
/// This loader enables template composition by supporting Scriban's include directive:
/// <code>
/// {{~ include "Html/Datatypes/Identifier" ~}}
/// </code>
/// </para>
/// <para>
/// Template paths support the following formats:
/// </para>
/// <list type="bullet">
///   <item>"Html/Datatypes/Identifier" - Loads Templates/Html/Datatypes/Identifier.scriban</item>
///   <item>"Md/Datatypes/HumanName" - Loads Templates/Md/Datatypes/HumanName.scriban</item>
///   <item>"Compact/Patient" - Loads Templates/Compact/Patient.scriban</item>
/// </list>
/// <para>
/// Templates are cached by the TemplateContext.CachedTemplates property using the path as the cache key.
/// </para>
/// </remarks>
internal class ScribanTemplateLoader(ITemplateResolver templateResolver) : ITemplateLoader
{
    private readonly ITemplateResolver _templateResolver = templateResolver ?? throw new ArgumentNullException(nameof(templateResolver));

    /// <summary>
    /// Gets an absolute path for the specified include template name.
    /// </summary>
    /// <param name="context">The current template context.</param>
    /// <param name="callerSpan">The source location of the include directive.</param>
    /// <param name="templateName">The template name from the include directive (e.g., "Html/Datatypes/Identifier").</param>
    /// <returns>
    /// The normalized template path used as a cache key. Returns the template name normalized with forward slashes.
    /// </returns>
    /// <remarks>
    /// The returned path serves as both a unique cache key and the path for the Load/LoadAsync methods.
    /// Scriban caches compiled templates using this path in TemplateContext.CachedTemplates.
    /// </remarks>
    public string GetPath(TemplateContext context, SourceSpan callerSpan, string templateName)
    {
        ArgumentNullException.ThrowIfNull(templateName);

        // Normalize the path: use forward slashes consistently
        var normalizedPath = templateName
            .Replace('\\', '/')
            .Trim('/');

        return normalizedPath;
    }

    /// <summary>
    /// Loads template content synchronously using the path returned by <see cref="GetPath"/>.
    /// </summary>
    /// <param name="context">The current template context.</param>
    /// <param name="callerSpan">The source location of the include directive.</param>
    /// <param name="templatePath">The template path returned by <see cref="GetPath"/>.</param>
    /// <returns>The template content as a string.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the template cannot be found.</exception>
    /// <remarks>
    /// This synchronous method blocks on the async resolver. For better performance,
    /// use Scriban's async rendering which will call <see cref="LoadAsync"/> instead.
    /// </remarks>
    public string Load(TemplateContext context, SourceSpan callerSpan, string templatePath)
    {
        // Block on the async method - not ideal but required by ITemplateLoader interface
        var content = _templateResolver.ResolveByPathAsync(templatePath, CancellationToken.None)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();

        if (content is null)
        {
            throw new InvalidOperationException(
                $"Template include '{templatePath}' not found. " +
                $"Ensure the template exists at Templates/{templatePath}.scriban and is marked as an embedded resource.");
        }

        return content;
    }

    /// <summary>
    /// Loads template content asynchronously using the path returned by <see cref="GetPath"/>.
    /// </summary>
    /// <param name="context">The current template context.</param>
    /// <param name="callerSpan">The source location of the include directive.</param>
    /// <param name="templatePath">The template path returned by <see cref="GetPath"/>.</param>
    /// <returns>A <see cref="ValueTask{T}"/> containing the template content.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the template cannot be found.</exception>
    /// <remarks>
    /// This is the preferred method for loading templates as it doesn't block the calling thread.
    /// Scriban will use this method when rendering templates asynchronously via Template.RenderAsync.
    /// </remarks>
    public async ValueTask<string> LoadAsync(TemplateContext context, SourceSpan callerSpan, string templatePath)
    {
        var content = await _templateResolver.ResolveByPathAsync(templatePath, CancellationToken.None);

        if (content is null)
        {
            throw new InvalidOperationException(
                $"Template include '{templatePath}' not found. " +
                $"Ensure the template exists at Templates/{templatePath}.scriban and is marked as an embedded resource.");
        }

        return content;
    }
}
