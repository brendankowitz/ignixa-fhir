namespace Ignixa.Conformance.Events.Events;

public record StructureDefinitionActivated(
    string Canonical,
    string Type,
    string Kind,
    string SourcePackage,
    string SnapshotJson);

public record StructureDefinitionDeactivated(
    string Canonical,
    string Reason);
