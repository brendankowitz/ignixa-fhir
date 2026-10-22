// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Validation;

namespace Ignixa.Validation.Abstractions.Terminology;

public sealed record TerminologyIssue(IssueSeverity Severity, TerminologyIssueKind Kind, string Message);
