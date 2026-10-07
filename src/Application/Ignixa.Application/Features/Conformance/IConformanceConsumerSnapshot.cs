namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// A fully prepared set of local conformance consumers that can be published without I/O.
/// </summary>
public interface IConformanceConsumerSnapshot
{
    long Generation { get; }
}
