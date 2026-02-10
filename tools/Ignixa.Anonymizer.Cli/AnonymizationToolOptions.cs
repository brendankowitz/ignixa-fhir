// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
namespace Ignixa.Anonymizer.Cli;

public class AnonymizationToolOptions
{
    public bool IsRecursive { get; set; }
    public bool SkipExistedFile { get; set; }
    public bool ValidateInput { get; set; }
    public bool ValidateOutput { get; set; }
}
