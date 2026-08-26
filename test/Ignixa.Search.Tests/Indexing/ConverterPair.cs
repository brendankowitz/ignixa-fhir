namespace Ignixa.Search.Tests.Indexing;

/// <summary>
/// One key of the converter manager's dictionary: the FHIR type an element reports as its
/// <c>InstanceType</c>, and the search value type the search parameter's declared type demands.
/// The indexer skips any element whose pair has no converter.
/// </summary>
internal readonly record struct ConverterPair(string FhirType, Type SearchValueType)
{
    public override string ToString() => $"({FhirType} -> {SearchValueType.Name})";
}
