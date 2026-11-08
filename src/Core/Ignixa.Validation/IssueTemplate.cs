// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;

namespace Ignixa.Validation;

/// <summary>
/// A stable message template for a <see cref="ValidationIssue"/>: an identifier consumers can key on, and a composite format
/// string whose placeholders (<c>{0}</c>, <c>{1}</c>, ...) are filled from <see cref="ValidationIssue.TemplateArguments"/>.
/// </summary>
/// <remarks>
/// The fixed text of <see cref="Format"/> is the validator's own; every placeholder stands for a value taken from the instance
/// or from a definition (an element value, a property name, a ValueSet canonical, a constraint's <c>human</c> text). A consumer
/// that must not echo instance or definition content renders its own text for <see cref="Id"/> and treats every argument as
/// untrusted. <see cref="Id"/> never changes meaning once published; a template whose wording changes keeps its id, one whose
/// arguments change gets a new id.
/// </remarks>
public sealed class IssueTemplate
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IssueTemplate"/> class.
    /// </summary>
    /// <param name="id">The stable template identifier.</param>
    /// <param name="format">The composite format string; its placeholders are the arguments in order.</param>
    /// <param name="argumentCount">How many arguments the format takes.</param>
    public IssueTemplate(string id, string format, int argumentCount)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentOutOfRangeException.ThrowIfNegative(argumentCount);

        Id = id;
        Format = format;
        ArgumentCount = argumentCount;
    }

    /// <summary>Gets the stable template identifier (e.g. <c>binding-required-text-only</c>).</summary>
    public string Id { get; }

    /// <summary>Gets the composite format string the message is rendered from.</summary>
    public string Format { get; }

    /// <summary>Gets the number of arguments <see cref="Format"/> takes.</summary>
    public int ArgumentCount { get; }

    /// <summary>
    /// Renders the message for <paramref name="arguments"/>.
    /// </summary>
    /// <param name="arguments">The values for the placeholders, in order.</param>
    /// <returns>The rendered message.</returns>
    /// <exception cref="ArgumentException">The number of arguments differs from <see cref="ArgumentCount"/>.</exception>
    public string Render(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count != ArgumentCount)
        {
            throw new ArgumentException($"Template '{Id}' takes {ArgumentCount} arguments, not {arguments.Count}.", nameof(arguments));
        }

        return string.Format(CultureInfo.InvariantCulture, Format, [.. arguments]);
    }
}
