// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using System.CommandLine.Parsing;

namespace Ignixa.CliOnFhir.Commands;

/// <summary>
/// <c>--as-of &lt;instant&gt;</c>: one option on the root command, recursive, so it can be written before or after any verb. Only the
/// verbs in <see cref="Consumers"/> read it; every other verb refuses it, since answering without the instant asked for would read
/// as an answer anchored to it. Its value is checked by the verb that reads it, so a malformed instant is a request error in the
/// envelope, not a parse error.
/// </summary>
internal static class AsOfOption
{
    /// <summary>The verbs that read the option.</summary>
    public static readonly IReadOnlySet<string> Consumers = new HashSet<string>(StringComparer.Ordinal) { "view", "draft" };

    /// <summary>The option, shared by the root command and the verbs that read it.</summary>
    public static Option<string?> Option { get; } = new("--as-of")
    {
        Description = "The instant the data is read as of, a FHIR instant such as 2024-05-01T00:00:00Z (default: the folder's data-pull time, "
            + "from a Bulk Data export manifest or its Bundles' timestamp, else the clock). Used by view (%asOf) and draft (authoredOn, effective, issued) only for now",
        Recursive = true,
    };

    /// <summary>Adds the option to <paramref name="root"/> and makes every verb that does not read it refuse it.</summary>
    public static void Register(RootCommand root)
    {
        ArgumentNullException.ThrowIfNull(root);

        root.Options.Add(Option);
        foreach (Command command in Descendants(root).Where(command => !Consumers.Contains(command.Name)))
        {
            command.Validators.Add(Refuse);
        }
    }

    // Validators run for the verb the command line ends at, so every verb and subverb gets one.
    private static IEnumerable<Command> Descendants(Command command) =>
        command.Subcommands.SelectMany(subcommand => (IEnumerable<Command>)[subcommand, .. Descendants(subcommand)]);

    private static void Refuse(CommandResult result)
    {
        if (result.GetResult(Option) is not null)
        {
            result.AddError($"--as-of is used only by {string.Join(" and ", Consumers.Order(StringComparer.Ordinal))} for now; '{result.Command.Name}' would answer without it.");
        }
    }
}
