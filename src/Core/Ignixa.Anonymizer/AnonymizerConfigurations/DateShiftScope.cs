using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Ignixa.Anonymizer.AnonymizerConfigurations;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DateShiftScope
{
    [EnumMember(Value = "resource")]
    Resource,
    [EnumMember(Value = "file")]
    File,
    [EnumMember(Value = "folder")]
    Folder
}
