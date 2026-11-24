// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Tests.Analysis;

/// <summary>
/// A real schema that throws a chosen exception when one type is looked up, so analysis fails part-way
/// through a walk the way a defective schema or a cancelled request would.
/// </summary>
internal sealed class ThrowingSchemaProvider(IFhirSchemaProvider inner, string failingTypeName, Func<Exception> failure)
    : IFhirSchemaProvider
{
    public FhirVersion Version => inner.Version;

    public IReadOnlySet<string> ResourceTypeNames => inner.ResourceTypeNames;

    public string FullVersion => inner.FullVersion;

    public IReferenceMetadataProvider ReferenceMetadataProvider => inner.ReferenceMetadataProvider;

    public IValueSetProvider ValueSetProvider => inner.ValueSetProvider;

    public IType? GetTypeDefinition(string typeName) =>
        string.Equals(typeName, failingTypeName, StringComparison.Ordinal) ? throw failure() : inner.GetTypeDefinition(typeName);

    public bool IsKnownType(string typeName) => inner.IsKnownType(typeName);
}
