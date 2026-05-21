namespace Ignixa.TestScript.Client;

public interface IFhirClientRegistry
{
    IFhirClient GetDestination(int? destination);
}
