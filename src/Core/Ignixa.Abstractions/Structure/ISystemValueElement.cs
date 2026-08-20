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
/// recognised.
/// </para>
/// <para>
/// <strong>What holds the set of implementors complete.</strong> Declaring the contract moves the
/// failure from "the wrong classes match a name pattern" to "a producer was never given the
/// declaration", which is silent in a different way: the value simply loses its System spelling from
/// R5 onwards, and below R5 the cast alias hides it. That is how <c>IndexElement</c> and
/// <c>StringElement</c> - <c>$index</c> and the <c>%ucum</c>/<c>%sct</c>/<c>%vs-</c> constants - were
/// missed when the first six were marked.
/// </para>
/// <para>
/// <c>SystemValueElementDeclarationTests</c> is what closes that. It reflects over every
/// <see cref="IElement"/> implementor in the FhirPath, FHIR Mapping Language and SQL-on-FHIR
/// assemblies and requires each one to appear in a table stating whether it declares this interface
/// and why. A new element type fails the build until someone records that decision, and removing the
/// declaration from a listed producer fails too. It does not decide the answer for a new type - it
/// only makes an omission impossible to commit silently.
/// </para>
/// <para>
/// The behavioural tests are narrower and should not be read as covering this: they pin the wrappers
/// the expressions in them happen to reach, which is a handful, not all of them.
/// </para>
/// <para>
/// Elements read from a resource tree must not implement it, whatever their primitive type.
/// </para>
/// </remarks>
public interface ISystemValueElement : IElement
{
}
