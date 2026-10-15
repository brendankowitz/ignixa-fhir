namespace Ignixa.DataLayer.SqlServer;

internal sealed record ReindexResourceRow(
    string ResourceId,
    int Version,
    byte[] RawResource,
    long ResourceSurrogateId,
    string RequestMethod,
    DateTimeOffset LastModified);
