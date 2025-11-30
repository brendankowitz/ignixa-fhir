/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Global code analysis suppressions for FHIR Mapping Language.
 */

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Single is a FHIR Mapping Language list mode keyword", Scope = "member", Target = "~F:Ignixa.FhirMappingLanguage.Lexer.MappingTokenKind.Single")]
[assembly: SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Single is a FHIR Mapping Language list mode", Scope = "member", Target = "~F:Ignixa.FhirMappingLanguage.Expressions.ListMode.Single")]
[assembly: SuppressMessage("Design", "CA1024:Use properties where appropriate", Justification = "GetErrors performs collection enumeration and is better as a method", Scope = "member", Target = "~M:Ignixa.FhirMappingLanguage.Evaluation.ExecutionError.GetErrors~System.Collections.Generic.IEnumerable{Ignixa.FhirMappingLanguage.Evaluation.ExecutionError}")]
[assembly: SuppressMessage("Design", "CA1024:Use properties where appropriate", Justification = "GetErrors performs collection enumeration and is better as a method", Scope = "member", Target = "~M:Ignixa.FhirMappingLanguage.Validation.ValidationResult.GetErrors~System.Collections.Generic.IEnumerable{Ignixa.FhirMappingLanguage.Validation.ValidationError}")]
[assembly: SuppressMessage("Globalization", "CA1308:In method 'Execute', replace the call to 'ToLowerInvariant' with 'ToUpperInvariant'", Justification = "FHIR Mapping Language transform functions require lowercase output", Scope = "member", Target = "~M:Ignixa.FhirMappingLanguage.Transforms.StandardTransforms.Execute(Ignixa.FhirMappingLanguage.Evaluation.EvaluationContext,System.Collections.Generic.IEnumerable{System.Object})~System.Object")]
[assembly: SuppressMessage("Style", "CA1510:Use 'ArgumentNullException.ThrowIfNull' instead of explicitly throwing a new exception instance", Justification = "Using explicit throws for better control flow and debugging", Scope = "member", Target = "~M:Ignixa.FhirMappingLanguage.Registry.MapRegistry.Register(Ignixa.FhirMappingLanguage.Expressions.MapExpression)")]
[assembly: SuppressMessage("Style", "CA1510:Use 'ArgumentNullException.ThrowIfNull' instead of explicitly throwing a new exception instance", Justification = "Using explicit throws for better control flow and debugging", Scope = "member", Target = "~M:Ignixa.FhirMappingLanguage.Registry.MapRegistry.#ctor")]
[assembly: SuppressMessage("Style", "CA1510:Use 'ArgumentNullException.ThrowIfNull' instead of explicitly throwing a new exception instance", Justification = "Using explicit throws for better control flow and debugging", Scope = "member", Target = "~M:Ignixa.FhirMappingLanguage.Registry.MapRegistry.Resolve(System.String)~Ignixa.FhirMappingLanguage.Expressions.MapExpression")]
