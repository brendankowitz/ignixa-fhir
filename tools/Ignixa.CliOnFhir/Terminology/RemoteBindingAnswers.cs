// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Terminology.Tools;

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>
/// The terminology server's answers to the validator's binding questions, one question per ValueSet, system, code and version.
/// <list type="bullet">
/// <item>A definitive answer (the server's verdict, or a failure asking again would repeat: a 4xx, a malformed answer, an
/// OperationOutcome) is kept for the session.</item>
/// <item>A transient failure (timeout, connection failure, 5xx) is not: a connection failure or 5xx is asked once more at
/// once, then the failure stands for the rest of the validate call (<see cref="BeginCall"/>), so a failing question costs at
/// most two requests per call, never two per resource. A timeout is not asked again in the call: it already waited the
/// full timeout.</item>
/// <item>After <see cref="BreakerThreshold"/> different questions have failed transiently in a row, with no success between
/// them, or after one timeout, the server is not asked again in the call: every question not already answered is
/// <c>tx-server-unavailable (not-asked)</c> at once. A question's retry is the same question, so one question that fails
/// twice costs the call its two requests and nothing more; a success closes the breaker again.</item>
/// <item>A caller waiting on a question another caller cancelled asks it again with its own token.</item>
/// </list>
/// Safe to use concurrently; concurrent validate calls share it, so one call's <see cref="BeginCall"/> lets the other ask
/// failed questions again (more requests, never a wrong answer).
/// </summary>
internal sealed class RemoteBindingAnswers
{
    /// <summary>Different questions failing transiently in a row after which the server is not asked again in the call.</summary>
    public const int BreakerThreshold = 2;

    /// <summary>The reason given for a question not asked because the server failed repeatedly in this call.</summary>
    public const string NotAskedReason = $"{StoreTerminologyService.TxUnavailableReason} (not-asked)";

    private readonly ConcurrentDictionary<BindingQuestion, Task<RemoteBindingAnswer>> _asked = new();
    private readonly ConcurrentDictionary<BindingQuestion, (string Result, string Reason)> _failedThisCall = new();

    // The questions that failed transiently since the last success (a set: a question's retry adds nothing), and whether
    // one of them timed out, which opens the breaker by itself.
    private readonly ConcurrentDictionary<BindingQuestion, byte> _failingSinceSuccess = new();
    private int _timedOutSinceSuccess;

    /// <summary>Starts a validate call: questions that failed transiently are asked again, and the breaker closes.</summary>
    public void BeginCall()
    {
        _failedThisCall.Clear();
        CloseBreaker();
    }

    /// <summary>The answer to <paramref name="question"/>, asking the server with <paramref name="ask"/> when needed.</summary>
    public async Task<(string Result, string Reason)> AnswerAsync(BindingQuestion question, Func<CancellationToken, Task<RemoteBindingAnswer>> ask, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ask);

        bool retried = false;
        while (true)
        {
            if (_failedThisCall.TryGetValue(question, out (string Result, string Reason) failed))
            {
                return failed;
            }

            if (BreakerOpen && !_asked.ContainsKey(question))
            {
                return (TermValidate.Unknown, NotAskedReason);
            }

            Task<RemoteBindingAnswer>? created = null;
            Task<RemoteBindingAnswer> asked = _asked.GetOrAdd(question, _ => created = CountedAsync(question, ask, cancellationToken));
            bool mine = ReferenceEquals(asked, created);
            RemoteBindingAnswer answer;
            try
            {
                answer = await asked;
            }
            catch (OperationCanceledException) when (!mine && !cancellationToken.IsCancellationRequested)
            {
                // Another caller's question, cancelled by that caller's token: ask again with this one.
                _asked.TryRemove(KeyValuePair.Create(question, asked));
                continue;
            }
            catch (Exception)
            {
                _asked.TryRemove(KeyValuePair.Create(question, asked));
                throw;
            }

            if (answer.Failure == TransientFailure.None)
            {
                return (answer.Result, answer.Reason);
            }

            _asked.TryRemove(KeyValuePair.Create(question, asked));
            if (retried || answer.Failure == TransientFailure.Timeout || BreakerOpen)
            {
                _failedThisCall[question] = (answer.Result, answer.Reason);
                return (answer.Result, answer.Reason);
            }

            retried = true;
        }
    }

    private bool BreakerOpen => Volatile.Read(ref _timedOutSinceSuccess) == 1 || _failingSinceSuccess.Count >= BreakerThreshold;

    private void CloseBreaker()
    {
        _failingSinceSuccess.Clear();
        Volatile.Write(ref _timedOutSinceSuccess, 0);
    }

    private async Task<RemoteBindingAnswer> CountedAsync(BindingQuestion question, Func<CancellationToken, Task<RemoteBindingAnswer>> ask, CancellationToken cancellationToken)
    {
        RemoteBindingAnswer answer = await ask(cancellationToken);
        switch (answer.Failure)
        {
            case TransientFailure.None:
                CloseBreaker();
                break;
            case TransientFailure.Timeout:
                Volatile.Write(ref _timedOutSinceSuccess, 1);
                break;
            default:
                _failingSinceSuccess[question] = 0;
                break;
        }

        return answer;
    }
}
