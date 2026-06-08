namespace Ignixa.TestScript.Expressions;

public abstract record AssertCriteria;

public sealed record ResponseStatusCriteria(string Status) : AssertCriteria;

public sealed record ResponseCodeCriteria(string Code) : AssertCriteria;

public sealed record ContentTypeCriteria(string ContentType) : AssertCriteria;

public sealed record ResourceTypeCriteria(string ResourceType) : AssertCriteria;

public sealed record HeaderCriteria(string Field, string? Value = null, AssertOperator? Operator = null) : AssertCriteria;

public sealed record FhirPathCriteria(string Expression) : AssertCriteria;

public sealed record FhirPathValueCriteria(string Expression, string? Value, AssertOperator Operator) : AssertCriteria;

public sealed record RequestMethodCriteria(string Method) : AssertCriteria;

public sealed record RequestUrlCriteria(string Url, AssertOperator? Operator = null) : AssertCriteria;
