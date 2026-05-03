// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
namespace Ignixa.DeId.Cli;

public class DeIdToolOptions
{
    public bool IsRecursive { get; set; }
    public bool SkipExistedFile { get; set; }
    public bool ValidateInput { get; set; }
    public bool ValidateOutput { get; set; }
}
