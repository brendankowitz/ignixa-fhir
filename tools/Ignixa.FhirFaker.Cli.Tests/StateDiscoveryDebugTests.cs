using Ignixa.FhirFaker.Cli.Discovery;
using Xunit.Abstractions;

namespace Ignixa.FhirFaker.Cli.Tests;

public class StateDiscoveryDebugTests
{
    private readonly ITestOutputHelper _output;

    public StateDiscoveryDebugTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Debug_StateDiscovery()
    {
        // Check names
        var names = StateDiscovery.GetObservationStateNames().ToList();
        _output.WriteLine($"Found {names.Count} observation states:");
        foreach (var name in names.Take(10))
        {
            _output.WriteLine($"  - {name}");
        }

        // Check if BloodGlucose is in there
        var hasBloodGlucose = names.Contains("BloodGlucose");
        _output.WriteLine($"\nContains 'BloodGlucose': {hasBloodGlucose}");

        // Try to create it
        var state = StateDiscovery.CreateObservationState("BloodGlucose");
        _output.WriteLine($"Created state: {state?.GetType().Name ?? "NULL"}");
    }
}
