namespace Ignixa.Search.Tests.Indexing;

/// <summary>
/// One converter registration, retaining the converter's type name so a census failure can name the
/// class a reader has to go and find.
/// </summary>
internal sealed record ConverterRegistration(string ConverterName, string FhirType, Type SearchValueType)
{
    public ConverterPair Pair => new(FhirType, SearchValueType);
}
