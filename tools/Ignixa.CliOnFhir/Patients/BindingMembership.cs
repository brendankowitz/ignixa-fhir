// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology;

namespace Ignixa.CliOnFhir.Patients;

/// <summary>
/// Whether a code the patient tools show (a status, a gender, a quantity comparator) is in its element's required-binding
/// ValueSet, by the session's terminology store as loaded when the call started (never the terminology server: the patient
/// tools never wait on the network). Only created when terminology is loaded; the tools never load it themselves, so a
/// patient call does not download a package. For one call; not thread-safe.
/// </summary>
internal sealed class BindingMembership
{
    private readonly IFhirSchemaProvider _schema;
    private readonly TerminologyStore _store;
    private readonly Dictionary<(string Type, string Element), string?> _valueSets = [];
    private readonly Dictionary<(string ValueSet, string Code), (string Result, string Reason)> _answers = [];

    private BindingMembership(IFhirSchemaProvider schema, TerminologyStore store)
    {
        _schema = schema;
        _store = store;
    }

    /// <summary>Membership over the session's loaded terminology; null when no terminology call has loaded it.</summary>
    public static BindingMembership? IfLoaded(FolderSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return session.Terminology.Current is { } state ? new BindingMembership(session.Schema, state.Store) : null;
    }

    /// <summary>
    /// <paramref name="code"/> checked against the required binding of <paramref name="definitionType"/>.<paramref name="element"/>
    /// (a resource type, or a datatype such as <c>Quantity</c>): <c>valid</c>, <c>invalid</c> or <c>unknown</c> with the
    /// ValueSet; null when the element has no required binding.
    /// </summary>
    public (string Result, string ValueSet)? Check(string definitionType, string element, string code)
    {
        if (ValueSetOf(definitionType, element) is not { } valueSet)
        {
            return null;
        }

        if (!_answers.TryGetValue((valueSet, code), out (string Result, string Reason) answer))
        {
            answer = StoreTerminologyService.CheckLocal(_store, valueSet, system: null, code);
            _answers[(valueSet, code)] = answer;
        }

        return (answer.Result, valueSet);
    }

    private string? ValueSetOf(string definitionType, string element)
    {
        if (!_valueSets.TryGetValue((definitionType, element), out string? valueSet))
        {
            valueSet = _schema.GetTypeDefinition(definitionType)?.Children
                .FirstOrDefault(child => child.Info.Name == element) is ITypeExtended { Binding: { ValueSet: { Length: > 0 } url } binding }
                && string.Equals(binding.Strength, "required", StringComparison.OrdinalIgnoreCase)
                    ? url
                    : null;
            _valueSets[(definitionType, element)] = valueSet;
        }

        return valueSet;
    }
}
