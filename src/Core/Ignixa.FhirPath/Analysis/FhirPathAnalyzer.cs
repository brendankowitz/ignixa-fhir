// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
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
    private readonly IFhirSchemaProvider _schema;
    private readonly SymbolTable _symbolTable;
    private readonly FhirPathParser _parser;

    /// <summary>
    /// Creates a new FhirPath analyzer with the specified schema provider.
    /// </summary>
    public FhirPathAnalyzer(IFhirSchemaProvider schema)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _symbolTable = new SymbolTable(schema);
        _parser = new FhirPathParser();
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
        var types = expression.AcceptVisitor(this, context);

        // Second pass: Walk the tree again to collect types for each node
        var typeCollector = new TypeCollectorVisitor(this, nodeTypes);
        var contextForCollection = AnalysisContext.Create(_schema, rootTypeName);
        expression.AcceptVisitor(typeCollector, contextForCollection);

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

        FhirPathTypeSet focusTypes;
        if (expression.Focus != null)
        {
            focusTypes = expression.Focus.AcceptVisitor(this, context);
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
            if (focusType.Type == null)
            {
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
            context.AddError(
                $"Property '{expression.PropertyName}' not found on type '{focusTypes.TypeNames()}'",
                expression);
        }

        return result;
    }

    public override FhirPathTypeSet VisitChild(ChildExpression expression, AnalysisContext context)
    {
        var result = new FhirPathTypeSet();

        FhirPathTypeSet focusTypes;
        if (expression.Focus != null && expression.Focus is not ScopeExpression { ScopeName: "that" })
        {
            focusTypes = expression.Focus.AcceptVisitor(this, context);
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
            if (focusType.Type == null)
            {
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
            context.AddError(
                $"Property '{expression.ChildName}' not found on type '{focusTypes.TypeNames()}'",
                expression);
        }

        return result;
    }

    public override FhirPathTypeSet VisitFunctionCall(FunctionCallExpression expression, AnalysisContext context)
    {
        var result = new FhirPathTypeSet();
        var functionName = expression.FunctionName;

        if (functionName == "builtin.children" && expression is not ChildExpression)
        {
            var focusResult = expression.Focus != null
                ? expression.Focus.AcceptVisitor(this, context)
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
            focusTypes = expression.Focus.AcceptVisitor(this, context);
        }

        var funcDef = _symbolTable.Get(functionName);

        var innerContext = context.PushTypeContext(focusTypes);

        if (funcDef?.TakesExpressionArguments == true)
        {
            var singleItemContext = focusTypes.AsSingle();
            innerContext = innerContext.WithFocus(singleItemContext).PushExpressionContext(singleItemContext);
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

        var argTypes = new List<FhirPathTypeSet>();
        foreach (var arg in expression.Arguments)
        {
            argTypes.Add(arg.AcceptVisitor(this, innerContext));
        }
        if (funcDef != null)
        {
            var issues = new List<ValidationIssue>();

            foreach (var validation in funcDef.Validations)
            {
                validation(expression, funcDef, argTypes, issues);
            }

            foreach (var issue in issues)
            {
                context.AddIssue(issue.Severity, issue.Message, expression);
            }

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
        var leftResult = expression.Left?.AcceptVisitor(this, context) ?? new FhirPathTypeSet();
        var rightResult = expression.Right?.AcceptVisitor(this, context) ?? new FhirPathTypeSet();

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
                    var existing = result.Types.FirstOrDefault(x => x.TypeName == t.TypeName);
                    if (existing.TypeName != null && !existing.IsCollection)
                    {
                        result.Types.Remove(existing);
                        result.Types.Add(existing.AsCollection());
                    }
                    else if (existing.TypeName == null)
                    {
                        result.Types.Add(t);
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
        var operandResult = expression.Operand?.AcceptVisitor(this, context) ?? new FhirPathTypeSet();

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

        var typeName = expression.Value switch
        {
            null => "empty",
            bool => "boolean",
            int or long => "integer",
            decimal or double or float => "decimal",
            string => "string",
            DateTime or DateTimeOffset => "dateTime",
            _ => "string"
        };

        result.AddPrimitiveType(typeName);
        return result;
    }

    public override FhirPathTypeSet VisitIdentifier(IdentifierExpression expression, AnalysisContext context)
    {
        var result = new FhirPathTypeSet();
        var focusTypes = context.GetCurrentType();

        foreach (var focusType in focusTypes.Types)
        {
            if (focusType.Type == null)
            {
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
            context.AddError($"Property '{expression.Name}' not found on type '{focusTypes.TypeNames()}'", expression);
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
        var collectionResult = expression.Collection?.AcceptVisitor(this, context) ?? new FhirPathTypeSet();
        expression.Index?.AcceptVisitor(this, context);

        return collectionResult.AsSingle();
    }

    public override FhirPathTypeSet VisitParenthesized(ParenthesizedExpression expression, AnalysisContext context)
    {
        return expression.InnerExpression?.AcceptVisitor(this, context) ?? new FhirPathTypeSet();
    }

    public override FhirPathTypeSet VisitQuantity(QuantityExpression expression, AnalysisContext context)
    {
        var result = new FhirPathTypeSet();
        result.AddPrimitiveType("Quantity");
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

        var matchingTypes = focusTypes.Types.Where(t =>
            t.TypeName.Equals(typeName, StringComparison.OrdinalIgnoreCase)).ToList();

        if (matchingTypes.Count > 0)
        {
            foreach (var type in matchingTypes)
            {
                result.Types.Add(type);
            }
        }
        else
        {
            var targetType = _schema.GetTypeDefinition(typeName);
            var isPrimitive = FhirPathType.IsPrimitiveTypeName(typeName);

            if (targetType == null && !isPrimitive)
            {
                context.AddError($"Type '{typeName}' is not a valid FHIR type", expression);
            }
            else if (!focusTypes.CanBeOfType(typeName))
            {
                context.AddWarning(
                    $"Type filter '{typeName}' will always be empty. Focus types: {focusTypes.TypeNames()}",
                    expression);
            }

            if (targetType != null)
            {
                result.AddType(targetType, focusTypes.IsCollection());
            }
            else if (isPrimitive)
            {
                result.AddPrimitiveType(typeName, focusTypes.IsCollection());
            }
        }

        return result;
    }

    private static void HandleDefineVariable(
        FunctionCallExpression expression,
        FhirPathTypeSet focusTypes,
        List<FhirPathTypeSet> argTypes,
        AnalysisContext context)
    {
        if (expression.Arguments.Count >= 1 && expression.Arguments[0] is ConstantExpression nameExpr)
        {
            var varName = nameExpr.Value?.ToString();
            if (!string.IsNullOrEmpty(varName))
            {
                var varType = argTypes.Count >= 2 ? argTypes[1] : focusTypes;
                context.WithDefinedVariable(varName, varType);
            }
        }
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
        if (typeName != null && !focusTypes.CanBeOfType(typeName))
        {
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
            if (typeName != null && !leftResult.CanBeOfType(typeName))
            {
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
                if (!leftResult.CanBeOfType(typeName))
                {
                    context.AddWarning(
                        $"Cast 'as {typeName}' may return empty. Possible types: {leftResult.TypeNames()}",
                        expression);
                }

                var matchingTypes = leftResult.Types.Where(t =>
                    t.TypeName.Equals(typeName, StringComparison.OrdinalIgnoreCase)).ToList();

                foreach (var t in matchingTypes)
                {
                    result.Types.Add(t);
                }

                if (matchingTypes.Count == 0)
                {
                    var targetType = _schema.GetTypeDefinition(typeName);
                    if (targetType != null)
                    {
                        result.AddType(targetType);
                    }
                    else if (FhirPathType.IsPrimitiveTypeName(typeName))
                    {
                        result.AddPrimitiveType(typeName);
                    }
                }
            }
        }

        if (leftResult.IsCollection())
        {
            context.AddWarning("Operator 'as' applied to collection - only first item will be cast", expression);
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
