// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>The server's answer to a <see cref="BindingQuestion"/>, and for a failed request whether asking again may succeed.</summary>
internal sealed record RemoteBindingAnswer(string Result, string Reason, TransientFailure Failure);
