/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * The lexically nested store behind defineVariable().
 */

using System.Collections.Immutable;
using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Evaluation;

/// <summary>
/// A frame of <c>defineVariable</c> bindings that can see its enclosing frames but never writes to them.
/// </summary>
/// <remarks>
/// <para>
/// FHIRPath scopes a defined variable to "the rest of the expression" it was defined in, which is a lexical
/// rule, not a global one: a name defined inside a <c>select()</c> or <c>where()</c> argument, or inside one
/// operand of <c>|</c>, must be invisible once that argument or operand is done (official cases
/// <c>defineVariable9</c>/<c>10</c>/<c>12</c>/<c>16</c> and <c>dvUsageOutsideScopeThrows</c>).
/// </para>
/// <para>
/// Two properties make this a chain of frames rather than a copied dictionary. First, <see cref="Define"/>
/// always writes to the frame it is called on, so entering a scope with <see cref="Fork"/> is enough to
/// contain every definition made inside it. Second, <see cref="Fork"/> keeps a reference to the parent
/// rather than copying it, which costs one small allocation per iteration and - more importantly - stays
/// correct when the evaluator's lazily enumerated focus defines a variable after the child frame was
/// created, which a snapshot copy would silently miss.
/// </para>
/// <para>
/// Frames are mutable by design: <c>a.defineVariable('v', …).select(%v)</c> has to carry <c>v</c> from the
/// focus of a call into the call's own argument evaluation, and the evaluator passes one context object
/// down that path rather than threading a new one back out.
/// </para>
/// </remarks>
public sealed class VariableScope
{
    private readonly VariableScope? _parent;
    private Dictionary<string, ImmutableList<IElement>>? _definitions;

    /// <summary>
    /// Creates a root scope with no enclosing frame.
    /// </summary>
    public VariableScope()
    {
    }

    private VariableScope(VariableScope parent)
    {
        _parent = parent;
    }

    /// <summary>
    /// Creates a nested scope that can read this one but whose own definitions stay local to it.
    /// </summary>
    public VariableScope Fork()
    {
        return new VariableScope(this);
    }

    /// <summary>
    /// Binds a name in this frame, shadowing any binding of the same name in an enclosing frame.
    /// </summary>
    public void Define(string name, ImmutableList<IElement> value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);

        _definitions ??= new Dictionary<string, ImmutableList<IElement>>(StringComparer.OrdinalIgnoreCase);
        _definitions[name] = value;
    }

    /// <summary>
    /// Looks a name up in this frame and then outwards through the enclosing frames.
    /// </summary>
    /// <returns><see langword="true"/> when the name is bound; a bound name may still hold an empty collection.</returns>
    public bool TryResolve(string name, out ImmutableList<IElement> value)
    {
        for (var scope = this; scope is not null; scope = scope._parent)
        {
            if (scope._definitions is not null && scope._definitions.TryGetValue(name, out var found))
            {
                value = found;
                return true;
            }
        }

        value = ImmutableList<IElement>.Empty;
        return false;
    }
}
