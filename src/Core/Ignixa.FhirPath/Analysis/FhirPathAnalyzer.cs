// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Parser;
using Ignixa.FhirPath.Visitors;

namespace Ignixa.FhirPath.Analysis;

/// <summary>
/// Comprehensive analyzer for FhirPath expressions providing type inference and validation.
/// </summary>
/// <remarks>
/// <para>
/// This analyzer performs static analysis on FhirPath expressions by walking the AST
/// and inferring types at each step using the FHIR schema definitions.
/// </para>
/// <para>
/// Key capabilities:
/// </para>
/// <list type="bullet">
///   <item><description>Type inference for all 13 expression types</description></item>
///   <item><description>Validation of property access, function calls, and type compatibility</description></item>
///   <item><description>Path resolution to FHIR schema types</description></item>
/// </list>
/// <para>
/// This is a general-purpose FhirPath expression analyzer. Domain-specific logic
/// (such as search parameter type resolution) should be built on top of this analyzer
/// by using its type inference capabilities.
/// </para>
/// <example>
/// <code>
/// var analyzer = new FhirPathAnalyzer(schema);
/// var result = analyzer.Analyze("Patient.name.family", "Patient");
/// // result.InferredTypes contains FhirPathType("string", collection=true)
/// </code>
/// </example>
/// </remarks>
public sealed class FhirPathAnalyzer : DefaultFhirPathExpressionVisitor<AnalysisContext, FhirPathTypeSet>
{
    /// <summary>
    /// FHIR primitive types that are string-based and compatible with 'string' type.
    /// </summary>
    private static readonly HashSet<string> StringSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "string", "id", "code", "uri", "url", "canonical", "oid", "uuid", "markdown"
    };

    /// <summary>
    /// Numeric types compatible with numeric operations.
    /// </summary>
    private static readonly HashSet<string> NumericTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "integer", "decimal", "long"
    };

    private readonly IFhirSchemaProvider _schema;
    private readonly SymbolTable _symbolTable;
    private readonly SystemTypeConstructionAnalyzer _systemTypeConstructionAnalyzer;
    private readonly FhirPathParser _parser;
    private readonly Lazy<HashSet<string>> _rootPropertyNames;
    private IFhirPathExpressionVisitor<AnalysisContext, FhirPathTypeSet>? _childVisitor;

    /// <summary>
    /// Creates a new FhirPath analyzer with the specified schema provider.
    /// </summary>
    public FhirPathAnalyzer(IFhirSchemaProvider schema)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _symbolTable = new SymbolTable(schema);
        _parser = new FhirPathParser();
        _rootPropertyNames = new Lazy<HashSet<string>>(
            () => _schema.ResourceTypeNames
                .Select(_schema.GetTypeDefinition)
                .Where(type => type != null)
                .SelectMany(type => type!.Children)
                .Select(child => child.Info.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            isThreadSafe: true);
        _systemTypeConstructionAnalyzer = new SystemTypeConstructionAnalyzer(
            _symbolTable,
            propertyName => _rootPropertyNames.Value.Contains(propertyName));
    }

    internal void SetChildVisitor(IFhirPathExpressionVisitor<AnalysisContext, FhirPathTypeSet> visitor)
    {
        _childVisitor = visitor;
    }

    /// <summary>
    /// Analyzes a FhirPath expression against the specified root type.
    /// </summary>
    /// <param name="expression">The parsed FhirPath expression</param>
    /// <param name="rootTypeName">The root type name (e.g., "Patient")</param>
    /// <returns>Analysis result with inferred types and validation issues</returns>
    public AnalysisResult Analyze(Expression expression, string rootTypeName)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(rootTypeName);

        var context = AnalysisContext.Create(_schema, rootTypeName);
        var nodeTypes = new Dictionary<Expression, FhirPathTypeSet>(ReferenceEqualityComparer.Instance);
        
        // First pass: Analyze with the regular analyzer to get types and collect issues
        FhirPathTypeSet types = new FhirPathTypeSet();
        try
        {
            types = expression.AcceptVisitor(this, context);
        }
        catch (Exception ex)
        {
            context.AddIssue(ValidationIssueSeverity.Error, $"Analysis failed: {ex.Message}");
        }

        // Second pass: Walk the tree again to collect types for each node
        // We use a new analyzer instance for the second pass to allow the collector to intercept child visits
        try
        {
            var secondPassAnalyzer = new FhirPathAnalyzer(_schema);
            var typeCollector = new TypeCollectorVisitor(secondPassAnalyzer, nodeTypes);
            secondPassAnalyzer.SetChildVisitor(typeCollector);
            
            var contextForCollection = AnalysisContext.Create(_schema, rootTypeName);
            expression.AcceptVisitor(typeCollector, contextForCollection);
        }
        catch (Exception)
        {
            // Ignore exceptions during type collection to ensure we return partial type information
            // Validation errors are already captured in the first pass
        }
        
        // Create result with NodeTypes populated
        var result = new AnalysisResult
        {
            InferredTypes = types,
            NodeTypes = nodeTypes
        };

        // Copy issues from first pass context
        foreach (var issue in context.Issues)
        {
            result.Issues.Add(issue);
        }

        return result;
    }

    /// <summary>
    /// Analyzes a FhirPath expression string against the specified root type.
    /// </summary>
    public AnalysisResult Analyze(string expression, string rootTypeName)
    {
        ArgumentNullException.ThrowIfNull(expression);

        try
        {
            var parsed = _parser.Parse(expression);
            return Analyze(parsed, rootTypeName);
        }
        catch (FormatException ex)
        {
            return AnalysisResult.Failure($"Parse error: {ex.Message}");
        }
        catch (ArgumentException ex)
        {
            return AnalysisResult.Failure($"Parse error: {ex.Message}");
        }
    }

    /// <summary>
    /// Infers the types that a FhirPath expression can return.
    /// </summary>
    public FhirPathTypeSet InferTypes(Expression expression, string rootTypeName)
    {
        var result = Analyze(expression, rootTypeName);
        return result.InferredTypes;
    }

    /// <summary>
    /// Infers the types that a FhirPath expression string can return.
    /// </summary>
    public FhirPathTypeSet InferTypes(string expression, string rootTypeName)
    {
        var result = Analyze(expression, rootTypeName);
        return result.InferredTypes;
    }

    /// <summary>
    /// Validates a FhirPath expression against the specified root type.
    /// </summary>
    public IEnumerable<ValidationIssue> Validate(Expression expression, string rootTypeName)
    {
        var result = Analyze(expression, rootTypeName);
        return result.Issues;
    }

    /// <summary>
    /// Validates a FhirPath expression string against the specified root type.
    /// </summary>
    public IEnumerable<ValidationIssue> Validate(string expression, string rootTypeName)
    {
        var result = Analyze(expression, rootTypeName);
        return result.Issues;
    }

    public override FhirPathTypeSet VisitPropertyAccess(PropertyAccessExpression expression, AnalysisContext context)
    {
        var result = new FhirPathTypeSet();
        var visitor = _childVisitor ?? this;

        FhirPathTypeSet focusTypes;
        if (expression.Focus != null)
        {
            focusTypes = expression.Focus.AcceptVisitor(visitor, context);
        }
        else
        {
            focusTypes = context.GetCurrentType();
        }

        if (focusTypes.Types.Count == 0)
        {
            context.AddError($"Cannot access property '{expression.PropertyName}' on empty context", expression);
            return result;
        }

        var propertyFound = false;
        foreach (var focusType in focusTypes.Types)
        {
            if (focusType.IsUnknown)
            {
                result.Types.Add(focusType.WithPath(
                    $"{focusType.Path}.{expression.PropertyName}"));
                propertyFound = true;
                continue;
            }

            if (focusType.Type == null)
            {
                if (TryAddReflectionMember(result, focusType, expression.PropertyName))
                {
                    propertyFound = true;
                    continue;
                }

                if (_schema.ResourceTypeNames.Contains(expression.PropertyName))
                {
                    var resourceType = _schema.GetTypeDefinition(expression.PropertyName);
                    if (resourceType != null)
                    {
                        result.AddType(resourceType, focusType.IsCollection, expression.PropertyName);
                        propertyFound = true;
                    }
                }
                continue;
            }

            if (focusTypes.IsRoot && focusType.TypeName == expression.PropertyName)
            {
                result.Types.Add(focusType);
                propertyFound = true;
                continue;
            }

            if (focusTypes.IsRoot && _schema.ResourceTypeNames.Contains(expression.PropertyName))
            {
                var resourceType = _schema.GetTypeDefinition(expression.PropertyName);
                if (resourceType != null)
                {
                    result.AddType(resourceType, focusType.IsCollection, expression.PropertyName);
                    propertyFound = true;
                    continue;
                }
            }

            var child = FindChildByName(focusType.Type, expression.PropertyName);
            if (child != null)
            {
                AddChildTypes(result, child, focusType, expression.PropertyName);
                propertyFound = true;
            }
        }

        if (!propertyFound)
        {
            ReportUnresolvedProperty(expression.PropertyName, focusTypes, result, expression, context);
        }

        return result;
    }

    public override FhirPathTypeSet VisitChild(ChildExpression expression, AnalysisContext context)
    {
        var result = new FhirPathTypeSet();
        var visitor = _childVisitor ?? this;

        FhirPathTypeSet focusTypes;
        if (expression.Focus != null && expression.Focus is not ScopeExpression { ScopeName: "that" })
        {
            focusTypes = expression.Focus.AcceptVisitor(visitor, context);
        }
        else
        {
            focusTypes = context.GetCurrentType();
        }

        if (focusTypes.Types.Count == 0)
        {
            context.AddError($"Cannot access child '{expression.ChildName}' on empty context", expression);
            return result;
        }

        var propertyFound = false;
        foreach (var focusType in focusTypes.Types)
        {
            if (focusType.IsUnknown)
            {
                result.Types.Add(focusType.WithPath(
                    $"{focusType.Path}.{expression.ChildName}"));
                propertyFound = true;
                continue;
            }

            if (focusType.Type == null)
            {
                if (TryAddReflectionMember(result, focusType, expression.ChildName))
                {
                    propertyFound = true;
                    continue;
                }

                if (_schema.ResourceTypeNames.Contains(expression.ChildName))
                {
                    var resourceType = _schema.GetTypeDefinition(expression.ChildName);
                    if (resourceType != null)
                    {
                        result.AddType(resourceType, focusType.IsCollection, expression.ChildName);
                        propertyFound = true;
                    }
                }
                continue;
            }

            if (focusTypes.IsRoot && focusType.TypeName == expression.ChildName)
            {
                result.Types.Add(focusType);
                propertyFound = true;
                continue;
            }

            if (focusTypes.IsRoot && _schema.ResourceTypeNames.Contains(expression.ChildName))
            {
                var resourceType = _schema.GetTypeDefinition(expression.ChildName);
                if (resourceType != null)
                {
                    result.AddType(resourceType, focusType.IsCollection, expression.ChildName);
                    propertyFound = true;
                    continue;
                }
            }

            var child = FindChildByName(focusType.Type, expression.ChildName);
            if (child != null)
            {
                AddChildTypes(result, child, focusType, expression.ChildName);
                propertyFound = true;
            }
        }

        if (!propertyFound)
        {
            ReportUnresolvedProperty(expression.ChildName, focusTypes, result, expression, context);
        }

        return result;
    }

    public override FhirPathTypeSet VisitFunctionCall(FunctionCallExpression expression, AnalysisContext context)
    {
        var result = new FhirPathTypeSet();
        var functionName = expression.FunctionName;
        var visitor = _childVisitor ?? this;

        if (functionName == "builtin.children" && expression is not ChildExpression)
        {
            var focusResult = expression.Focus != null
                ? expression.Focus.AcceptVisitor(visitor, context)
                : context.GetCurrentType();
            return focusResult;
        }

        FhirPathTypeSet focusTypes;
        if (expression.Focus == null || (expression.Focus is ScopeExpression scope && scope.ScopeName == "that"))
        {
            focusTypes = context.GetCurrentType();
        }
        else
        {
            focusTypes = expression.Focus.AcceptVisitor(visitor, context);
        }

        var unorderedSource = GetUnorderedNavigationSource(expression.Focus);
        if (unorderedSource != null && IsOrderDependentFunction(functionName))
        {
            if (IsPositionalFunction(functionName))
            {
                context.AddError(
                    $"Function '{functionName}()' requires positional access on unordered output from {unorderedSource}(). Result is undefined.",
                    expression);
            }
            else
            {
                context.AddWarning(
                    $"Function '{functionName}()' on unordered output from {unorderedSource}() yields non-deterministic results.",
                    expression);
            }
        }

        var funcDef = _symbolTable.Get(functionName);

        var innerContext = context.PushTypeContext(focusTypes);

        if (funcDef?.TakesExpressionArguments == true)
        {
            var singleItemContext = focusTypes.AsSingle();
            innerContext = innerContext
                .WithFocus(singleItemContext)
                .PushExpressionContext(singleItemContext)
                .ForkVariableScope();
        }

        if (functionName.Equals("ofType", StringComparison.OrdinalIgnoreCase) ||
            functionName.Equals("as", StringComparison.OrdinalIgnoreCase))
        {
            return HandleTypeFilterFunction(expression, focusTypes, innerContext, result);
        }

        // Handle is() as a function call (equivalent to binary "is" operator)
        if (functionName.Equals("is", StringComparison.OrdinalIgnoreCase))
        {
            return HandleIsFunction(expression, focusTypes, innerContext, result);
        }

        // For scoped functions (TakesExpressionArguments=true), analyze arguments with innerContext
        // which has $this set to focus items. For non-scoped functions, analyze arguments with
        // the original context so $this remains the outer context (per FHIRPath spec).
        var argContext = funcDef?.TakesExpressionArguments == true ? innerContext : context;
        var argTypes = new List<FhirPathTypeSet>();
        foreach (var arg in expression.Arguments)
        {
            argTypes.Add(arg.AcceptVisitor(visitor, argContext));
        }
        if (funcDef != null)
        {
            var issues = new List<ValidationIssue>();

            // Validate that focus type is supported by this function
            ValidateFocusType(expression, funcDef, focusTypes, issues);

            try
            {
                foreach (var validation in funcDef.Validations)
                {
                    validation(expression, funcDef, argTypes, issues);
                }
            }
            catch (Exception ex)
            {
                issues.Add(new ValidationIssue { Severity = ValidationIssueSeverity.Warning, Message = $"Validation failed for function '{functionName}': {ex.Message}" });
            }

            foreach (var issue in issues)
            {
                context.AddIssue(issue.Severity, issue.Message, expression);
            }

            try
            {
                if (funcDef.GetReturnType != null)
                {
                    var returnTypes = funcDef.GetReturnType(funcDef, focusTypes, argTypes, issues);
                    foreach (var rt in returnTypes)
                    {
                        result.Types.Add(rt);
                    }
                }
                else
                {
                    result.CopyFrom(focusTypes);
                }

                if (result.HasUnknown && !focusTypes.HasUnknown)
                {
                    context.AddIndeterminateWarning(
                        $"The return type of function '{functionName}()' cannot be analysed statically; downstream navigation is indeterminate.",
                        expression);
                }
            }
            catch (Exception ex)
            {
                context.AddIndeterminateWarning(
                    $"Return type calculation failed for function '{functionName}()' and cannot be analysed: {ex.Message}",
                    expression);
                result.AddUnknown(focusTypes.IsCollection());
            }
        }
        else
        {
            context.AddWarning($"Unknown function '{functionName}'", expression);
            result.CopyFrom(focusTypes);
        }

        if (functionName.Equals("defineVariable", StringComparison.OrdinalIgnoreCase))
        {
            HandleDefineVariable(expression, focusTypes, argTypes, context);
        }

        return result;
    }

    public override FhirPathTypeSet VisitBinary(BinaryExpression expression, AnalysisContext context)
    {
        var result = new FhirPathTypeSet();
        var visitor = _childVisitor ?? this;

        // For union operator, fork context so defineVariable in one branch doesn't leak to sibling
        var leftContext = expression.Operator == "|" ? context.ForkVariableScope() : context;
        var rightContext = expression.Operator == "|" ? context.ForkVariableScope() : context;

        var leftResult = expression.Left?.AcceptVisitor(visitor, leftContext) ?? new FhirPathTypeSet();
        var rightResult = expression.Right?.AcceptVisitor(visitor, rightContext) ?? new FhirPathTypeSet();

        switch (expression.Operator)
        {
            case "is":
                result.AddPrimitiveType("boolean");
                ValidateIsOperator(expression, leftResult, context);
                break;

            case "as":
                HandleAsOperator(expression, leftResult, result, context);
                break;

            case "|":
                foreach (var t in leftResult.Types)
                    result.Types.Add(t);
                foreach (var t in rightResult.Types)
                {
                    // Matching on TypeName alone cannot tell "absent" from "present but default-valued":
                    // TypeName never returns null, so the index has to carry the answer.
                    var existingIndex = IndexOfTypeName(result.Types, t.TypeName);
                    if (existingIndex < 0)
                    {
                        result.Types.Add(t);
                    }
                    else if (!result.Types[existingIndex].IsCollection)
                    {
                        result.Types[existingIndex] = result.Types[existingIndex].AsCollection();
                    }
                }
                break;

            case "=" or "!=" or "~" or "!~" or "<" or ">" or "<=" or ">=" or
                 "and" or "or" or "xor" or "implies" or "in" or "contains":
                result.AddPrimitiveType("boolean");
                ValidateComparisonOperators(expression, leftResult, rightResult, context);
                break;

            case "+" or "-" or "*" or "/" or "div" or "mod":
                foreach (var t in leftResult.Types)
                    result.Types.Add(t);
                break;

            case "&":
                result.AddPrimitiveType("string");
                break;

            default:
                foreach (var t in leftResult.Types)
                    result.Types.Add(t);
                break;
        }

        return result;
    }

    public override FhirPathTypeSet VisitUnary(UnaryExpression expression, AnalysisContext context)
    {
        var visitor = _childVisitor ?? this;
        var operandResult = expression.Operand?.AcceptVisitor(visitor, context) ?? new FhirPathTypeSet();

        return expression.Operator switch
        {
            "not" => CreateBooleanTypeSet(),
            "+" or "-" => operandResult,
            _ => operandResult
        };
    }

    public override FhirPathTypeSet VisitConstant(ConstantExpression expression, AnalysisContext context)
    {
        var result = new FhirPathTypeSet();

        result.AddPrimitiveType(SystemTypeConstructionAnalyzer.GetConstantTypeName(expression));
        return result;
    }

    public override FhirPathTypeSet VisitIdentifier(IdentifierExpression expression, AnalysisContext context)
    {
        var result = new FhirPathTypeSet();
        var focusTypes = context.GetCurrentType();

        foreach (var focusType in focusTypes.Types)
        {
            if (focusType.IsUnknown)
            {
                result.Types.Add(focusType.WithPath($"{focusType.Path}.{expression.Name}"));
                continue;
            }

            if (focusType.Type == null)
            {
                if (TryAddReflectionMember(result, focusType, expression.Name))
                {
                    continue;
                }

                if (_schema.ResourceTypeNames.Contains(expression.Name))
                {
                    var resourceType = _schema.GetTypeDefinition(expression.Name);
                    if (resourceType != null)
                    {
                        result.AddType(resourceType, focusType.IsCollection, expression.Name);
                    }
                }
                continue;
            }

            if (focusTypes.IsRoot && focusType.TypeName == expression.Name)
            {
                result.Types.Add(focusType);
                continue;
            }

            if (focusTypes.IsRoot && _schema.ResourceTypeNames.Contains(expression.Name))
            {
                var resourceType = _schema.GetTypeDefinition(expression.Name);
                if (resourceType != null)
                {
                    result.AddType(resourceType, focusType.IsCollection, expression.Name);
                    continue;
                }
            }

            var child = FindChildByName(focusType.Type, expression.Name);
            if (child != null)
            {
                AddChildTypes(result, child, focusType, expression.Name);
            }
        }

        if (result.Types.Count == 0)
        {
            ReportUnresolvedProperty(expression.Name, focusTypes, result, expression, context);
        }

        return result;
    }

    public override FhirPathTypeSet VisitVariable(VariableRefExpression expression, AnalysisContext context)
    {
        var result = new FhirPathTypeSet();
        var name = expression.Name;

        if (name.StartsWith("builtin.", StringComparison.Ordinal))
        {
            var axisName = name["builtin.".Length..];
            var resolved = context.ResolveScope(axisName);
            if (resolved != null)
            {
                result.CopyFrom(resolved);
            }
            return result;
        }

        var varProps = context.ResolveVariable(name);
        if (varProps != null)
        {
            result.CopyFrom(varProps);
        }
        else
        {
            context.AddError($"Variable '%{name}' not found", expression);
        }

        return result;
    }

    public override FhirPathTypeSet VisitScope(ScopeExpression expression, AnalysisContext context)
    {
        var result = new FhirPathTypeSet();
        var resolved = context.ResolveScope(expression.ScopeName);

        if (resolved != null)
        {
            result.CopyFrom(resolved);
        }
        else if (expression.ScopeName != "that")
        {
            context.AddWarning($"Scope '${expression.ScopeName}' could not be resolved", expression);
        }

        return result;
    }

    public override FhirPathTypeSet VisitIndexer(IndexerExpression expression, AnalysisContext context)
    {
        var visitor = _childVisitor ?? this;
        var collectionResult = expression.Collection?.AcceptVisitor(visitor, context) ?? new FhirPathTypeSet();
        expression.Index?.AcceptVisitor(visitor, context);

        var unorderedSource = GetUnorderedNavigationSource(expression.Collection);
        if (unorderedSource != null)
        {
            context.AddError(
                $"Indexer access on unordered output from {unorderedSource}(). Result is undefined.",
                expression);
        }

        return collectionResult.AsSingle();
    }

    public override FhirPathTypeSet VisitParenthesized(ParenthesizedExpression expression, AnalysisContext context)
    {
        var visitor = _childVisitor ?? this;
        return expression.InnerExpression?.AcceptVisitor(visitor, context) ?? new FhirPathTypeSet();
    }

    public override FhirPathTypeSet VisitQuantity(QuantityExpression expression, AnalysisContext context)
    {
        var result = new FhirPathTypeSet();
        result.AddPrimitiveType("Quantity");
        return result;
    }

    public override FhirPathTypeSet VisitInstanceSelector(InstanceSelectorExpression expression, AnalysisContext context)
    {
        var result = new FhirPathTypeSet();
        var visitor = _childVisitor ?? this;

        foreach (var element in expression.Elements)
        {
            element.ValueExpression.AcceptVisitor(visitor, context);
        }

        var typeDef = _schema.GetTypeDefinition(expression.TypeName);
        if (typeDef != null)
        {
            result.AddType(typeDef, false, expression.TypeName);
        }
        else
        {
            // The spec is silent on unknown construction types, but an expression naming a type this
            // schema cannot construct always evaluates to empty, so surfacing it at analysis time is
            // strictly more useful than a silent empty result at runtime.
            context.AddError($"Unknown type '{expression.FullTypeName}' in instance selector", expression);

            // Still contribute the name so downstream navigation reports its own problems rather
            // than cascading "empty context" errors from this one.
            result.AddPrimitiveType(expression.TypeName);
        }

        return result;
    }

    public override FhirPathTypeSet VisitEmpty(EmptyExpression expression, AnalysisContext context)
    {
        return new FhirPathTypeSet();
    }

    private FhirPathTypeSet HandleTypeFilterFunction(
        FunctionCallExpression expression,
        FhirPathTypeSet focusTypes,
        AnalysisContext context,
        FhirPathTypeSet result)
    {
        if (expression.Arguments.Count == 0)
        {
            context.AddError($"Function '{expression.FunctionName}' requires a type argument", expression);
            return result;
        }

        var typeName = ExtractTypeName(expression.Arguments[0]);
        if (typeName == null)
        {
            context.AddError($"Could not determine type argument for '{expression.FunctionName}'", expression);
            return result;
        }

        var matchingTypes = focusTypes.Types
            .Where(type => TypeMatcher.MatchesCastTypeName(
                type.TypeName,
                typeName,
                _schema,
                instanceIsSystemValue: false))
            .ToList();
        var (baseTypeName, resolvedType, targetType, isPrimitive) = ResolveCastTarget(typeName);
        var construction = _systemTypeConstructionAnalyzer.Analyze(expression.Focus);
        IReadOnlyList<string> systemTypeMatches = [];
        bool hasSystemTypeMatch;
        if (construction.MayConstructAny)
        {
            hasSystemTypeMatch =
                resolvedType is not null
                || isPrimitive
                || FhirPathType.IsPrimitiveTypeName(baseTypeName);
        }
        else
        {
            systemTypeMatches = GetSystemTypeMatches(construction, typeName);
            hasSystemTypeMatch = systemTypeMatches.Count > 0;
        }

        if (matchingTypes.Count > 0 || hasSystemTypeMatch)
        {
            foreach (var type in matchingTypes)
            {
                result.Types.Add(type);
            }

            if (construction.MayConstructAny)
            {
                AddIndeterminateCastTarget(
                    result,
                    baseTypeName,
                    targetType,
                    isPrimitive || FhirPathType.IsPrimitiveTypeName(baseTypeName),
                    focusTypes.IsCollection());
            }
            else
            {
                AddSystemTypeMatches(result, systemTypeMatches, focusTypes.IsCollection());
            }
        }
        else
        {
            if (resolvedType is not null && targetType is null)
            {
                if (!focusTypes.HasUnknown)
                {
                    context.AddAlwaysEmptyWarning(
                        $"Type filter '{typeName}' will always be empty. Focus types: {focusTypes.TypeNames()}",
                        expression);
                }
            }
            else if (targetType == null && !isPrimitive)
            {
                context.AddError($"Type '{typeName}' is not a valid FHIR type", expression);
            }
            else if (!focusTypes.HasUnknown && !focusTypes.CanBeOfType(baseTypeName))
            {
                // An indeterminate focus can hold anything at runtime, so "always empty" is not decidable.
                context.AddAlwaysEmptyWarning(
                    $"Type filter '{typeName}' will always be empty. Focus types: {focusTypes.TypeNames()}",
                    expression);
            }

            if (targetType != null)
            {
                result.AddType(targetType, focusTypes.IsCollection());
            }
            else if (isPrimitive)
            {
                result.AddPrimitiveType(baseTypeName, focusTypes.IsCollection());
            }
        }

        return result;
    }

    /// <summary>
    /// Resolves a cast target name to the schema type the evaluator would match a value against.
    /// </summary>
    /// <remarks>
    /// The <c>System.</c> and <c>FHIR.</c> prefixes are FHIRPath type syntax rather than part of the type
    /// name, and <see cref="TypeMatcher.ParseTypeName"/> strips them before matching. The schema lookup has
    /// to strip them too: leaving them on makes a qualified target unresolvable, so it is reported as an
    /// invalid FHIR type instead of reaching the always-empty contract an unqualified target of the same
    /// type gets. <paramref name="typeName"/> keeps its prefix for matching, because the prefix is part of
    /// what the evaluator was asked for.
    /// </remarks>
    private (string BaseTypeName, IType? ResolvedType, IType? TargetType, bool IsPrimitive) ResolveCastTarget(string typeName)
    {
        var (baseTypeName, _, _) = TypeMatcher.ParseTypeName(typeName);
        var resolvedType = _schema.GetTypeDefinition(baseTypeName);
        var targetType = resolvedType is not null
            && TypeMatcher.MatchesCastTypeName(resolvedType.Info.Name, typeName, _schema, instanceIsSystemValue: false)
                ? resolvedType
                : null;
        var isPrimitive = resolvedType is null && FhirPathType.IsPrimitiveTypeName(baseTypeName);

        return (baseTypeName, resolvedType, targetType, isPrimitive);
    }

    /// <summary>
    /// Records the variable a <c>defineVariable</c> call introduces, and reports the two ways the call can
    /// be rejected at runtime.
    /// </summary>
    /// <remarks>
    /// Both diagnostics mirror <c>FhirPathEvaluator.EvaluateDefineVariable</c> exactly, via the shared
    /// <see cref="DefineVariableRules"/>, because an analyzer that stays silent about an expression the
    /// evaluator throws on is worse than no analyzer: it certifies the expression. The redefinition check
    /// is the same lexical walk up the invocation chain the evaluator performs, which is why it can be
    /// applied here at all - it reads the AST, not the runtime variable store.
    /// </remarks>
    private static void HandleDefineVariable(
        FunctionCallExpression expression,
        FhirPathTypeSet focusTypes,
        List<FhirPathTypeSet> argTypes,
        AnalysisContext context)
    {
        // Argument count is already validated from the [FhirPathFunction] metadata; only the two rules the
        // evaluator adds on top of it are handled here.
        if (expression.Arguments.Count < 1 || expression.Arguments[0] is not ConstantExpression nameExpr)
        {
            return;
        }

        var varName = nameExpr.Value?.ToString();
        if (string.IsNullOrEmpty(varName))
        {
            return;
        }

        if (DefineVariableRules.ReservedVariableNames.Contains(varName))
        {
            context.AddError($"defineVariable cannot redefine the system variable '%{varName}'", expression);
            return;
        }

        if (DefineVariableRules.IsAlreadyDefinedEarlierInSameChain(expression, varName))
        {
            context.AddError($"Variable '%{varName}' is already defined", expression);
            return;
        }

        var varType = argTypes.Count >= 2 ? argTypes[1] : focusTypes;
        context.WithDefinedVariable(varName, varType);
    }

    /// <summary>
    /// Handles the is() function call (equivalent to binary 'is' operator).
    /// Returns boolean type and validates the type check.
    /// </summary>
    private FhirPathTypeSet HandleIsFunction(
        FunctionCallExpression expression,
        FhirPathTypeSet focusTypes,
        AnalysisContext context,
        FhirPathTypeSet result)
    {
        result.AddPrimitiveType("boolean");

        if (expression.Arguments.Count == 0)
        {
            context.AddError("Function 'is' requires a type argument", expression);
            return result;
        }

        var typeName = ExtractTypeName(expression.Arguments[0]);
        if (typeName != null && !focusTypes.HasUnknown && !focusTypes.CanBeOfType(typeName))
        {
            // An indeterminate focus can hold anything at runtime, so "always false" is not decidable.
            context.AddWarning(
                $"Type check 'is({typeName})' will always be false. Possible types: {focusTypes.TypeNames()}",
                expression);
        }

        if (focusTypes.IsCollection())
        {
            context.AddWarning("Function 'is' applied to collection - only first item will be checked", expression);
        }

        return result;
    }

    private void ValidateIsOperator(BinaryExpression expression, FhirPathTypeSet leftResult, AnalysisContext context)
    {
        if (expression.Right is ConstantExpression typeExpr)
        {
            var typeName = typeExpr.Value?.ToString();
            if (typeName != null && !leftResult.HasUnknown && !leftResult.CanBeOfType(typeName))
            {
                // An indeterminate operand can hold anything at runtime, so "always false" is not decidable.
                context.AddWarning(
                    $"Type check 'is {typeName}' will always be false. Possible types: {leftResult.TypeNames()}",
                    expression);
            }
        }

        if (leftResult.IsCollection())
        {
            context.AddWarning("Operator 'is' applied to collection - only first item will be checked", expression);
        }
    }

    private void HandleAsOperator(
        BinaryExpression expression,
        FhirPathTypeSet leftResult,
        FhirPathTypeSet result,
        AnalysisContext context)
    {
        if (expression.Right is ConstantExpression typeExpr)
        {
            var typeName = typeExpr.Value?.ToString();
            if (typeName != null)
            {
                var (baseTypeName, resolvedType, targetType, isPrimitive) = ResolveCastTarget(typeName);

                if (!leftResult.CanBeOfType(baseTypeName))
                {
                    context.AddWarning(
                        $"Cast 'as {typeName}' may return empty. Possible types: {leftResult.TypeNames()}",
                        expression);
                }

                var matchingTypes = leftResult.Types
                    .Where(type => TypeMatcher.MatchesCastTypeName(
                        type.TypeName,
                        typeName,
                        _schema,
                        instanceIsSystemValue: false))
                    .ToList();
                var construction = _systemTypeConstructionAnalyzer.Analyze(expression.Left);
                IReadOnlyList<string> systemTypeMatches = [];
                bool hasSystemTypeMatch;
                if (construction.MayConstructAny)
                {
                    hasSystemTypeMatch =
                        resolvedType is not null
                        || isPrimitive
                        || FhirPathType.IsPrimitiveTypeName(baseTypeName);
                }
                else
                {
                    systemTypeMatches = GetSystemTypeMatches(construction, typeName);
                    hasSystemTypeMatch = systemTypeMatches.Count > 0;
                }

                foreach (var t in matchingTypes)
                {
                    result.Types.Add(t);
                }

                if (construction.MayConstructAny)
                {
                    AddIndeterminateCastTarget(
                        result,
                        baseTypeName,
                        targetType,
                        isPrimitive || FhirPathType.IsPrimitiveTypeName(baseTypeName),
                        leftResult.IsCollection());
                }
                else
                {
                    AddSystemTypeMatches(result, systemTypeMatches, leftResult.IsCollection());
                }

                if (matchingTypes.Count == 0 && !hasSystemTypeMatch)
                {
                    if (resolvedType is not null && targetType is null)
                    {
                        if (!leftResult.HasUnknown)
                        {
                            context.AddAlwaysEmptyWarning(
                                $"Cast 'as {typeName}' will always be empty. Possible types: {leftResult.TypeNames()}",
                                expression);
                        }
                    }

                    else if (targetType != null)
                    {
                        result.AddType(targetType);
                    }
                    else if (isPrimitive)
                    {
                        result.AddPrimitiveType(baseTypeName);
                    }
                }
            }
        }

        if (leftResult.IsCollection())
        {
            context.AddWarning("Operator 'as' applied to collection - the evaluator throws unless the input is a single item", expression);
        }
    }

    private IReadOnlyList<string> GetSystemTypeMatches(
        SystemTypeConstruction construction,
        string requestedTypeName) =>
        construction.TypeNames
            .Where(typeName => TypeMatcher.MatchesCastTypeName(
                typeName,
                requestedTypeName,
                _schema,
                instanceIsSystemValue: true))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static void AddIndeterminateCastTarget(
        FhirPathTypeSet result,
        string baseTypeName,
        IType? targetType,
        bool isPrimitive,
        bool isCollection)
    {
        if (targetType is not null)
        {
            result.AddType(targetType, isCollection);
        }
        else if (isPrimitive)
        {
            result.AddPrimitiveType(baseTypeName, isCollection);
        }
    }

    private static void AddSystemTypeMatches(
        FhirPathTypeSet result,
        IEnumerable<string> systemTypeMatches,
        bool isCollection)
    {
        foreach (string typeName in systemTypeMatches)
        {
            if (!result.Types.Any(type => type.TypeName.Equals(typeName, StringComparison.Ordinal)))
            {
                result.AddPrimitiveType(typeName, isCollection);
            }
        }
    }

    private static void ValidateComparisonOperators(
        BinaryExpression expression,
        FhirPathTypeSet leftResult,
        FhirPathTypeSet rightResult,
        AnalysisContext context)
    {
        var nonCollectionOps = new[] { "=", "!=", "~", "!~", "<", "<=", ">", ">=", "as", "is", "or", "xor", "implies", "and" };
        if (nonCollectionOps.Contains(expression.Operator))
        {
            if (leftResult.IsCollection() || rightResult.IsCollection())
            {
                context.AddWarning(
                    $"Operator '{expression.Operator}' applied to collection - singleton expected",
                    expression);
            }
        }

        if (expression.Operator == "in" && leftResult.IsCollection())
        {
            context.AddError("Operator 'in' left argument must be a single item", expression);
        }
    }

    private IType? FindChildByName(IType type, string name)
    {
        var exactMatch = type.Children.FirstOrDefault(c =>
            c.Info.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (exactMatch != null)
        {
            return exactMatch;
        }

        return type.Children.FirstOrDefault(c =>
            c.Info.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase));
    }

    private void AddChildTypes(FhirPathTypeSet result, IType child, FhirPathType focusType, string propertyName)
    {
        var path = string.IsNullOrEmpty(focusType.Path) ? propertyName : $"{focusType.Path}.{propertyName}";
        var isCollection = focusType.IsCollection || child.IsCollection;

        if (child is ITypeExtended { ContentReference: { Length: > 1 } contentReference })
        {
            var referencePath = contentReference[(contentReference.IndexOf("#", StringComparison.Ordinal) + 1)..];
            var referencedType = _schema.GetTypeDefinition(referencePath);
            if (referencedType != null)
            {
                result.AddType(referencedType, isCollection, path);
                return;
            }
        }

        if (child is ITypeExtended extended && extended.Types?.Count > 0)
        {
            foreach (var typeRef in extended.Types)
            {
                // Check if this is a BackboneElement or Element (with children) that needs specialized type resolution
                // BackboneElement is used for inline complex types in resources
                // Element is used for inline complex types in complex types (like ElementDefinition.constraint)
                if ((typeRef.Code.Equals("BackboneElement", StringComparison.OrdinalIgnoreCase) ||
                     typeRef.Code.Equals("Element", StringComparison.OrdinalIgnoreCase)) && focusType.Type != null)
                {
                    var specializedTypeName = BuildBackboneElementTypeName(focusType, propertyName);

                    if (!string.IsNullOrEmpty(specializedTypeName))
                    {
                        var specializedType = _schema.GetTypeDefinition(specializedTypeName);

                        if (specializedType != null)
                        {
                            result.AddType(specializedType, isCollection, path);
                            continue;
                        }
                    }
                    // If specialized type not found, fall through to use the base type
                }

                var choiceType = _schema.GetTypeDefinition(typeRef.Code);
                if (choiceType != null)
                {
                    result.AddType(choiceType, isCollection, path);
                }
                else
                {
                    result.AddPrimitiveType(typeRef.Code, isCollection);
                }
            }
        }
        else
        {
            // Check if this is a BackboneElement or Element that needs specialized type resolution
            var childTypeName = child.Info.Name;
            if (childTypeName != null &&
                (childTypeName.Equals("BackboneElement", StringComparison.OrdinalIgnoreCase) ||
                 childTypeName.Equals("Element", StringComparison.OrdinalIgnoreCase)) &&
                focusType.Type != null)
            {
                var specializedTypeName = BuildBackboneElementTypeName(focusType, propertyName);

                if (!string.IsNullOrEmpty(specializedTypeName))
                {
                    var specializedType = _schema.GetTypeDefinition(specializedTypeName);

                    if (specializedType != null)
                    {
                        result.AddType(specializedType, isCollection, path);
                        return;
                    }
                }
                // If specialized type not found, fall through to use the base type
            }

            result.AddType(child, isCollection, path);
        }
    }

    /// <summary>
    /// Builds the specialized BackboneElement type name from the parent type and property name.
    /// </summary>
    /// <param name="parentType">The parent type containing the BackboneElement</param>
    /// <param name="propertyName">The property name (will be converted to TitleCase)</param>
    /// <returns>The specialized type name (e.g., "Bundle.Entry") or null if not applicable</returns>
    private string? BuildBackboneElementTypeName(FhirPathType parentType, string propertyName)
    {
        if (parentType.Type == null)
            return null;

        var rootTypeName = GetRootTypeName(parentType);
        var titleCasePropertyName = TitleCase(propertyName);

        // For nested BackboneElements, append to existing path
        if (parentType.TypeName.Contains('.', StringComparison.Ordinal))
        {
            // e.g., "Bundle.Entry" + "search" → "Bundle.Entry.Search"
            return $"{parentType.TypeName}.{titleCasePropertyName}";
        }
        else
        {
            // e.g., "Bundle" + "entry" → "Bundle.Entry"
            return $"{rootTypeName}.{titleCasePropertyName}";
        }
    }

    /// <summary>
    /// Gets the root type name from a FhirPathType (handles nested types).
    /// </summary>
    /// <param name="type">The FhirPath type</param>
    /// <returns>The root type name (e.g., "Bundle" from "Bundle.Entry")</returns>
    private static string GetRootTypeName(FhirPathType type)
    {
        if (type.Type == null)
            return type.TypeName;

        var typeName = type.TypeName;

        // If already a nested type (e.g., "Bundle.Entry"), extract root
        var dotIndex = typeName.IndexOf('.', StringComparison.Ordinal);
        if (dotIndex > 0)
            return typeName.Substring(0, dotIndex);

        return typeName;
    }

    /// <summary>
    /// Converts a property name to TitleCase for BackboneElement type name construction.
    /// </summary>
    /// <param name="propertyName">The property name in camelCase</param>
    /// <returns>The property name in TitleCase (first letter uppercase)</returns>
    private static string TitleCase(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
            return propertyName;

        return char.ToUpperInvariant(propertyName[0]) + propertyName.Substring(1);
    }

    private static string? ExtractTypeName(Expression expression)
    {
        return expression switch
        {
            ConstantExpression constant when constant.Value is string typeName => typeName,
            IdentifierExpression identifier => identifier.Name,
            FunctionCallExpression func when func.Focus is ScopeExpression { ScopeName: "that" } && func.Arguments.Count == 0
                => func.FunctionName,
            PropertyAccessExpression prop when prop.Focus == null => prop.PropertyName,
            _ => null
        };
    }

    private static bool TryAddReflectionMember(
        FhirPathTypeSet result,
        FhirPathType focusType,
        string propertyName)
    {
        if ((focusType.TypeName.Equals("ClassInfo", StringComparison.OrdinalIgnoreCase) ||
             focusType.TypeName.Equals("SimpleTypeInfo", StringComparison.OrdinalIgnoreCase)) &&
            (propertyName.Equals("name", StringComparison.OrdinalIgnoreCase) ||
             propertyName.Equals("namespace", StringComparison.OrdinalIgnoreCase) ||
             propertyName.Equals("baseType", StringComparison.OrdinalIgnoreCase)))
        {
            result.AddPrimitiveType("string", focusType.IsCollection);
            return true;
        }

        return false;
    }

    private bool IsPropertyOnAnotherRootType(string propertyName)
    {
        return _rootPropertyNames.Value.Contains(propertyName);
    }

    private static int IndexOfTypeName(IList<FhirPathType> types, string typeName)
    {
        for (var i = 0; i < types.Count; i++)
        {
            if (string.Equals(types[i].TypeName, typeName, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Reports a property that no focus type declares, choosing between the three answers static analysis
    /// can actually give: always empty, indeterminate, or invalid.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A root-relative name that some other resource declares is <em>decidable</em> when the root type is
    /// concrete: the analyzer knows the root type and knows it has no such element, so the navigation is
    /// always empty rather than unanalysable. Reporting it as indeterminate would be factually wrong and
    /// would silently downgrade any typo landing in the union of top-level element names across the whole
    /// specification (<c>status</c>, <c>date</c>, <c>code</c>, <c>subject</c>, ...). Only an abstract
    /// <em>resource</em> root (<c>Resource</c>, <c>DomainResource</c>) leaves the runtime type genuinely
    /// unknown, so only that case propagates an indeterminate type. <c>IsAbstract</c> alone is not the
    /// test: <c>Element</c>, <c>BackboneElement</c>, <c>DataType</c>, <c>BackboneType</c> and
    /// <c>PrimitiveType</c> are abstract too, and a runtime <c>Element</c> is never an <c>Appointment</c>,
    /// so a root-property miss on one of those is exactly as decidable as it is on <c>Patient</c>.
    /// </para>
    /// <para>
    /// The always-empty outcome is reached only for a bare, root-relative name. The identical fact
    /// reported against any qualified focus stays an error: the expression <c>status</c> analysed at root
    /// <c>Patient</c> warns, while <c>Patient.status</c>, <c>$this.status</c>, <c>%resource.status</c> and
    /// <c>Patient.where(status='active')</c> all report "Property 'status' not found" — including the
    /// resource-qualified form most authors would call the same expression. That asymmetry is
    /// pre-existing: the classifier keys on <see cref="FhirPathTypeSet.IsRoot"/>, not on decidability.
    /// Reconciling it would reclassify every "property not found" error the analyzer raises, which is its
    /// principal typo signal, so it is recorded here rather than changed.
    /// </para>
    /// </remarks>
    private void ReportUnresolvedProperty(
        string propertyName,
        FhirPathTypeSet focusTypes,
        FhirPathTypeSet result,
        Expression expression,
        AnalysisContext context)
    {
        if (!focusTypes.IsRoot || !IsPropertyOnAnotherRootType(propertyName))
        {
            context.AddError(
                $"Property '{propertyName}' not found on type '{focusTypes.TypeNames()}'",
                expression);
            return;
        }

        if (focusTypes.Types.Any(focusType => focusType.Type?.Info is { IsAbstract: true, IsResource: true }))
        {
            result.AddUnknown(path: propertyName);
            context.AddIndeterminateWarning(
                $"Property '{propertyName}' is not present on abstract root type '{context.RootType}', so the runtime type cannot be analysed for this root.",
                expression);
            return;
        }

        context.AddAlwaysEmptyWarning(
            $"Property '{propertyName}' will always be empty on root type '{context.RootType}'. It is declared by another resource type, but not by this one.",
            expression);
    }

    /// <summary>
    /// Validates that the focus type is supported by the function.
    /// Reports an error if the function doesn't support the given context type.
    /// </summary>
    private static void ValidateFocusType(
        FunctionCallExpression expression,
        FunctionDefinition funcDef,
        FhirPathTypeSet focusTypes,
        ICollection<ValidationIssue> issues)
    {
        // Skip validation if function accepts any type
        if (funcDef.SupportedContexts.Count == 0 ||
            funcDef.SupportedContexts.Any(c => c.ContextType.Equals("any", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        // Check each focus type against supported contexts
        foreach (var focusType in focusTypes.Types)
        {
            if (focusType.IsUnknown)
            {
                continue;
            }

            var typeName = focusType.TypeName;
            if (string.IsNullOrEmpty(typeName))
            {
                continue;
            }

            // Check if this type or a compatible type is supported
            var isSupported = funcDef.SupportedContexts.Any(c =>
                c.ContextType.Equals(typeName, StringComparison.OrdinalIgnoreCase) ||
                IsCompatibleType(typeName, c.ContextType));

            if (!isSupported)
            {
                var supportedTypes = string.Join(", ", funcDef.SupportedContexts.Select(c => c.ContextType));
                issues.Add(new ValidationIssue
                {
                    Severity = ValidationIssueSeverity.Error,
                    Message = $"Function '{funcDef.Name}' is not supported on context type '{typeName}'. Supported types: {supportedTypes}",
                    Location = expression.Location?.ToString(),
                    Expression = expression.ToString()
                });
            }
        }
    }

    private static bool IsOrderDependentFunction(string functionName) =>
        UnorderedCollectionDetection.IsOrderDependentFunction(functionName);

    private static bool IsPositionalFunction(string functionName) =>
        UnorderedCollectionDetection.IsPositionalFunction(functionName);

    private static string? GetUnorderedNavigationSource(Expression? focus) =>
        UnorderedCollectionDetection.GetUnorderedNavigationSource(focus);

    /// <summary>
    /// Checks if a type is compatible with a supported context type.
    /// Handles type hierarchies and aliases (e.g., "number" matches "integer" and "decimal").
    /// </summary>
    private static bool IsCompatibleType(string actualType, string supportedType)
    {
        // Handle "number" which includes integer, decimal, and long
        if (supportedType.Equals("number", StringComparison.OrdinalIgnoreCase))
        {
            return NumericTypes.Contains(actualType);
        }

        // Handle primitive type aliases
        if (supportedType.Equals("string", StringComparison.OrdinalIgnoreCase))
        {
            return StringSubtypes.Contains(actualType);
        }

        return false;
    }

    private static FhirPathTypeSet CreateBooleanTypeSet()
    {
        var result = new FhirPathTypeSet();
        result.AddPrimitiveType("boolean");
        return result;
    }

    /// <summary>
    /// Internal visitor that wraps the analyzer to collect type information for each node.
    /// Does NOT pre-visit children - lets the analyzer handle that with proper context.
    /// </summary>
    private sealed class TypeCollectorVisitor : IFhirPathExpressionVisitor<AnalysisContext, FhirPathTypeSet>
    {
        private readonly FhirPathAnalyzer _analyzer;
        private readonly Dictionary<Expression, FhirPathTypeSet> _nodeTypes;

        public TypeCollectorVisitor(FhirPathAnalyzer analyzer, Dictionary<Expression, FhirPathTypeSet> nodeTypes)
        {
            _analyzer = analyzer;
            _nodeTypes = nodeTypes;
        }

        private FhirPathTypeSet VisitAndCollect(Expression expression, AnalysisContext context, 
            Func<AnalysisContext, FhirPathTypeSet> visitFunc)
        {
            var result = visitFunc(context);
            _nodeTypes[expression] = result;
            return result;
        }

        public FhirPathTypeSet VisitBinary(BinaryExpression expression, AnalysisContext context)
        {
            return VisitAndCollect(expression, context, ctx => _analyzer.VisitBinary(expression, ctx));
        }

        public FhirPathTypeSet VisitChild(ChildExpression expression, AnalysisContext context)
        {
            return VisitAndCollect(expression, context, ctx => _analyzer.VisitChild(expression, ctx));
        }

        public FhirPathTypeSet VisitConstant(ConstantExpression expression, AnalysisContext context)
        {
            return VisitAndCollect(expression, context, ctx => _analyzer.VisitConstant(expression, ctx));
        }

        public FhirPathTypeSet VisitEmpty(EmptyExpression expression, AnalysisContext context)
        {
            return VisitAndCollect(expression, context, ctx => _analyzer.VisitEmpty(expression, ctx));
        }

        public FhirPathTypeSet VisitFunctionCall(FunctionCallExpression expression, AnalysisContext context)
        {
            return VisitAndCollect(expression, context, ctx => _analyzer.VisitFunctionCall(expression, ctx));
        }

        public FhirPathTypeSet VisitIdentifier(IdentifierExpression expression, AnalysisContext context)
        {
            return VisitAndCollect(expression, context, ctx => _analyzer.VisitIdentifier(expression, ctx));
        }

        public FhirPathTypeSet VisitIndexer(IndexerExpression expression, AnalysisContext context)
        {
            return VisitAndCollect(expression, context, ctx => _analyzer.VisitIndexer(expression, ctx));
        }

        public FhirPathTypeSet VisitInstanceSelector(InstanceSelectorExpression expression, AnalysisContext context)
        {
            return VisitAndCollect(expression, context, ctx => _analyzer.VisitInstanceSelector(expression, ctx));
        }

        public FhirPathTypeSet VisitParenthesized(ParenthesizedExpression expression, AnalysisContext context)
        {
            return VisitAndCollect(expression, context, ctx => _analyzer.VisitParenthesized(expression, ctx));
        }

        public FhirPathTypeSet VisitPropertyAccess(PropertyAccessExpression expression, AnalysisContext context)
        {
            return VisitAndCollect(expression, context, ctx => _analyzer.VisitPropertyAccess(expression, ctx));
        }

        public FhirPathTypeSet VisitQuantity(QuantityExpression expression, AnalysisContext context)
        {
            return VisitAndCollect(expression, context, ctx => _analyzer.VisitQuantity(expression, ctx));
        }

        public FhirPathTypeSet VisitScope(ScopeExpression expression, AnalysisContext context)
        {
            return VisitAndCollect(expression, context, ctx => _analyzer.VisitScope(expression, ctx));
        }

        public FhirPathTypeSet VisitUnary(UnaryExpression expression, AnalysisContext context)
        {
            return VisitAndCollect(expression, context, ctx => _analyzer.VisitUnary(expression, ctx));
        }

        public FhirPathTypeSet VisitVariable(VariableRefExpression expression, AnalysisContext context)
        {
            return VisitAndCollect(expression, context, ctx => _analyzer.VisitVariable(expression, ctx));
        }
    }
}
