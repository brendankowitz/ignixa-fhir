// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Ignixa.CliOnFhir.Catalog;

namespace Ignixa.CliOnFhir.Validation;

/// <summary>
/// A validator issue's element path as a summary groups and shows it: without repetition indexes (<c>Patient.name[0].given[1]</c>
/// is <c>Patient.name.given</c>), so the same rule at the same element groups across resources and repetitions. A path can name
/// a property the data invented, so each segment that is not shaped as an element name (a letter, then up to 63 letters, digits
/// or <c>_</c>, optionally <c>[x]</c>) or a choice type (<c>ofType(dateTime)</c>) is replaced by its <see cref="FhirIds.Digest"/>,
/// as the code-system inventory does.
/// </summary>
internal static partial class IssuePath
{
    public static string Normalize(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (path.Length == 0)
        {
            return path;
        }

        IEnumerable<string> segments = Index().Replace(path, string.Empty)
            .Split('.')
            .Select(segment => ElementName().IsMatch(segment) ? segment : FhirIds.Digest(segment));
        return string.Join('.', segments);
    }

    [GeneratedRegex("\\[[0-9]+\\]")]
    private static partial Regex Index();

    [GeneratedRegex("^(_?[A-Za-z][A-Za-z0-9_]{0,63}(\\[x\\])?|ofType\\([A-Za-z][A-Za-z0-9]{0,63}\\))$")]
    private static partial Regex ElementName();
}
