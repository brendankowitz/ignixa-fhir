namespace Ignixa.FhirMappingLanguage.Mutator;

/// <summary>
/// Specifies how property mutation should behave for arrays vs single values.
/// Used by IJsonNodeMutator to determine whether to replace or append values.
/// </summary>
public enum PropertyMutationMode
{
    /// <summary>
    /// Auto-detect based on: 1) Existing property type (array vs single), 2) FML list mode hints.
    /// Default mode when caller doesn't specify explicit behavior.
    /// </summary>
    AutoDetect,

    /// <summary>
    /// Replace existing value (for single-valued properties, max=1).
    /// Used when FML list modes are: onlyOne, share, single.
    /// </summary>
    Replace,

    /// <summary>
    /// Append to array (for multi-valued properties, max>1).
    /// Used when FML list modes are: first, last, notFirst, notLast.
    /// </summary>
    Append
}
