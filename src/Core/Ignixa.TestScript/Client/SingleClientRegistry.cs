namespace Ignixa.TestScript.Client;

public sealed class SingleClientRegistry(IFhirClient client) : IFhirClientRegistry
{
    public IFhirClient GetDestination(int? destination)
    {
        if (destination is not null and not 1)
            throw new InvalidOperationException(
                $"Multi-server not supported. Destination {destination} requested but only default server is configured.");

        return client;
    }
}
