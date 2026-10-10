namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

/// <summary>
/// The job-level phase reported as <c>phase</c>. Declared in order: a job's phase only ever advances, so a
/// tenant that is still draining while another reindexes does not move the job back.
/// </summary>
[Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum ReindexPhase
{
    BarrierDelay,
    Draining,
    Reindexing,
    Completing
}
