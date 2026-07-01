using Ignixa.TestScript.Client;

namespace Ignixa.TestScript.Reporting;

public sealed record HttpExchange(TestRequest? Request, TestResponse? Response);
