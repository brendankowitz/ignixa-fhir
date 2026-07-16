using Microsoft.CodeAnalysis;

namespace Ignixa.Search.Sql.Generators;

[Generator]
public sealed class TrivialProbeGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static ctx =>
            ctx.AddSource("GeneratorProbe.g.cs",
                "namespace Ignixa.Search.Sql.Generators.Probe; internal static class GeneratorProbe { internal const bool Ran = true; }"));
    }
}
