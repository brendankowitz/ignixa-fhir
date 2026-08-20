// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Abstractions;

/// <summary>
/// Marks an <see cref="IElement"/> whose value belongs to the FHIRPath <c>System</c> namespace rather
/// than to a FHIR model: a literal, or a value the evaluator itself produced such as the
/// <c>System.Integer</c> from <c>count()</c> or the <c>System.Boolean</c> from <c>exists()</c>.
/// </summary>
/// <remarks>
/// <para>
/// FHIRPath's type operators need this distinction because the two namespaces overlap in spelling:
/// <c>Patient.active</c> is a FHIR <c>boolean</c> and must not satisfy <c>is Boolean</c>, while
/// <c>exists()</c> yields a System <c>Boolean</c> and must. Nothing else in <see cref="IElement"/>
/// carries it — <c>InstanceType</c> is spelled in FHIR's lower camel case for both.
/// </para>
/// <para>
/// It is a declared contract rather than something inferred from the implementing type because the
/// engine wraps System values in more than one class and the classes are private to their evaluation
/// path. Inferring it from the CLR class name once made the compiled path disagree with the
/// interpreter about the very same <c>count()</c> result: the interpreter's wrapper happened to be
/// called <c>PrimitiveElement</c> and the compiler's <c>LiteralElement</c>, so only one of them was
/// recognised. Any new wrapper for an engine-produced value has to implement this interface;
/// <c>SystemValueTypeMatchingTests</c> fails if a path stops declaring it.
/// </para>
/// <para>
/// Elements read from a resource tree must not implement it, whatever their primitive type.
/// </para>
/// </remarks>
public interface ISystemValueElement : IElement
{
}
