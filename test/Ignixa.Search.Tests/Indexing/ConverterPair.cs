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

/// <summary>
/// One converter registration, retaining the converter's type name so a census failure can name the
/// class a reader has to go and find.
/// </summary>
internal sealed record ConverterRegistration(string ConverterName, string FhirType, Type SearchValueType)
{
    public ConverterPair Pair => new(FhirType, SearchValueType);
}
