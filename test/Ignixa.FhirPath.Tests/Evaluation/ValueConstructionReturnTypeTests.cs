/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * The defect this file exists for is opt-in metadata. ReturnType defaults to "any", so a function that
 * builds a value and declares "context" is silently misclassified, and CRITICAL 1 of PR #427 was exactly
 * that: abs, round, sum, lowBoundary and highBoundary declared "context", the analyzer inherited the
 * focus's FHIR provenance, and a valid cast was reported as provably always empty. Moving the rule into
 * metadata fixed the five known cases and left the sixth - the one written next - unguarded.
 */

using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Attributes;
using Ignixa.FhirPath.Evaluation.Functions;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Requires that no FHIRPath function whose implementation can construct a System value declares
/// <c>ReturnType = "context"</c>.
/// </summary>
/// <remarks>
/// <para>
/// "context" is the only return-type rule that makes the analyzer inherit the focus's namespace
/// provenance rather than deciding the function's own, so it is the only one a value-constructing
/// function must not use. A function that constructs and declares a concrete type, or one of the
/// "constructs" rules, is classified from its own metadata and is unaffected.
/// </para>
/// <para>
/// The discriminator is construction of an <see cref="ISystemValueElement"/>, not a call to a
/// <c>FunctionHelpers.Create*</c> factory. The factories are themselves only wrappers over that
/// construction, a function can bypass them because the wrapper types are public, and a rule keyed on a
/// method-name prefix is the same "list that can be incomplete" shape this guard exists to close.
/// </para>
/// <para>
/// Reachability is transitive across the Ignixa assemblies rather than a scan of the function body alone.
/// <c>lowBoundary</c> reaches its construction through a private helper, so a one-level scan would have a
/// hole in precisely the place the shipped defect sat.
/// </para>
/// <para>
/// This over-approximates: a function is reported if any element-yielding method it can reach constructs,
/// whether or not that path can be taken. That is the safe direction here - the cost is a function forced
/// to declare its own return type, and the alternative is a guard that misses the case it was written for.
/// </para>
/// </remarks>
public class ValueConstructionReturnTypeTests
{
    private const BindingFlags DeclaredMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private static readonly Dictionary<MethodBase, IReadOnlyList<MethodBase>> CallsCache = [];

    private static readonly Dictionary<MethodBase, bool> ConstructsCache = [];

    /// <summary>
    /// Maps the first byte (or, for two-byte opcodes, the 0xFE-prefixed value) to its
    /// <see cref="OpCode"/>, so the IL walk can skip each instruction's operand and stay in sync.
    /// </summary>
    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opCode => opCode.Value);

    [Fact]
    public void GivenTheFunctionLibrary_WhenBodiesAreScanned_ThenNoConstructingFunctionDeclaresContext()
    {
        // Arrange
        var assembly = typeof(FunctionHelpers).Assembly;

        // Act
        var offenders = ConstructingFunctionsDeclaringContext(DeclaredFunctions(assembly));

        // Assert
        offenders.ShouldBeEmpty(
            "ReturnType = \"context\" tells the analyzer the result carries the focus's namespace "
            + "provenance, which is only true of a function that hands the focus element back. A function "
            + "that builds a value must declare \"constructsFromContext\", \"boundaryOfContext\" or its own "
            + "concrete type; leaving it as \"context\" makes the analyzer report a valid cast over its "
            + "result as provably always empty, which is the wrong direction for this analysis to be wrong in.");
    }

    /// <summary>
    /// Proves the guard fires, using fixtures in this assembly because the production functions are all
    /// correctly declared and a census that has never failed proves nothing.
    /// </summary>
    /// <remarks>
    /// The positive fixtures are the three shapes that matter: constructing in the function body, through
    /// a helper, and inside an iterator. The last two are the shapes <c>lowBoundary</c> has, and each was
    /// missed by an earlier version of this walk.
    /// </remarks>
    [Fact]
    public void GivenConstructingFixturesDeclaringContext_WhenScanned_ThenEachIsReported()
    {
        // Arrange
        var fixtures = DeclaredFunctions(typeof(ValueConstructionReturnTypeTests).Assembly);

        // Act
        var offenders = ConstructingFunctionsDeclaringContext(fixtures);

        // Assert
        offenders.ShouldContain(
            offender => offender.Contains(nameof(ConstructionFixtures.ConstructsInBody), StringComparison.Ordinal),
            "a function constructing a System value directly in its body must be reported.");
        offenders.ShouldContain(
            offender => offender.Contains(nameof(ConstructionFixtures.ConstructsThroughHelper), StringComparison.Ordinal),
            "a function constructing through a helper must be reported too, or the guard has a hole exactly "
            + "where lowBoundary sits.");
        offenders.ShouldContain(
            offender => offender.Contains(nameof(ConstructionFixtures.ConstructsInIterator), StringComparison.Ordinal),
            "a function constructing inside an iterator body must be reported; the compiler moves that body "
            + "into a generated state machine, and following calls alone missed lowBoundary for this reason.");
        offenders.ShouldNotContain(
            offender => offender.Contains(nameof(ConstructionFixtures.SelectsFromFocus), StringComparison.Ordinal),
            "a genuine selector must not be reported, or the guard would force every function to stop "
            + "declaring the rule that is correct for it.");
        offenders.ShouldNotContain(
            offender => offender.Contains(nameof(ConstructionFixtures.ConstructsIntoScope), StringComparison.Ordinal),
            "a value built for a scope variable rather than the result must not be reported; this is the "
            + "shape trace has, and widening the walk to catch it would force trace to misdeclare itself.");
    }

    private static IReadOnlyList<string> ConstructingFunctionsDeclaringContext(
        IEnumerable<(MethodBase Method, FhirPathFunctionAttribute Attribute)> functions) =>
        functions
            .Where(function => function.Attribute.ReturnType.Equals("context", StringComparison.OrdinalIgnoreCase))
            .Select(function => (function.Attribute.Name, function.Method, Path: ConstructionPath(function.Method)))
            .Where(function => function.Path is not null)
            .Select(function => $"{function.Name} ({function.Method.DeclaringType?.Name}.{function.Method.Name}) constructs via {function.Path}")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    private static IEnumerable<(MethodBase Method, FhirPathFunctionAttribute Attribute)> DeclaredFunctions(Assembly assembly) =>
        assembly.GetTypes()
            .SelectMany(type => type.GetMethods(DeclaredMembers))
            .Select(method => (Method: (MethodBase)method, Attribute: method.GetCustomAttribute<FhirPathFunctionAttribute>()))
            .Where(candidate => candidate.Attribute is not null)
            .Select(candidate => (candidate.Method, candidate.Attribute!));

    /// <summary>
    /// The call path from <paramref name="root"/> to a method constructing an
    /// <see cref="ISystemValueElement"/>, or <see langword="null"/> when it cannot reach one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reachability is collected first and tested second, so a cycle in the call graph terminates on the
    /// visited set instead of needing a fixed point over truth values.
    /// </para>
    /// <para>
    /// The path is reported rather than a bare yes, because the answer a maintainer needs from a failure
    /// is which call to change, and a breadth-first search reaches the construction by its shortest route.
    /// </para>
    /// </remarks>
    private static string? ConstructionPath(MethodBase root)
    {
        var callers = new Dictionary<MethodBase, MethodBase?> { [root] = null };
        var pending = new Queue<MethodBase>([root]);

        while (pending.Count > 0)
        {
            var method = pending.Dequeue();
            if (ConstructsSystemValueDirectly(method))
            {
                return DescribePath(method, callers);
            }

            foreach (var callee in Successors(method))
            {
                if (callers.TryAdd(callee, method))
                {
                    pending.Enqueue(callee);
                }
            }
        }

        return null;
    }

    private static string DescribePath(MethodBase construction, IReadOnlyDictionary<MethodBase, MethodBase?> callers)
    {
        var steps = new List<string>();
        for (MethodBase? step = construction; step is not null; step = callers[step])
        {
            steps.Add($"{step.DeclaringType?.Name}.{step.Name}");
        }

        steps.Reverse();

        return string.Join(" -> ", steps);
    }

    private static bool ConstructsSystemValueDirectly(MethodBase method)
    {
        if (ConstructsCache.TryGetValue(method, out var cached))
        {
            return cached;
        }

        var constructs = CalledMethods(method)
            .Any(callee => callee is ConstructorInfo constructor
                && constructor.DeclaringType is not null
                && typeof(ISystemValueElement).IsAssignableFrom(constructor.DeclaringType));

        ConstructsCache[method] = constructs;

        return constructs;
    }

    /// <summary>
    /// The methods the walk continues into from <paramref name="method"/>.
    /// </summary>
    /// <remarks>
    /// A <c>yield return</c> body, an <c>async</c> body and a lambda are all compiled into a generated
    /// type that the declaring method merely constructs, so the code that does the work is reached through
    /// the type rather than through a call. Following calls alone missed <c>lowBoundary</c> - an iterator -
    /// which is precisely the function the shipped defect was found on.
    /// </remarks>
    private static IEnumerable<MethodBase> Successors(MethodBase method)
    {
        foreach (var callee in CalledMethods(method))
        {
            if (!IsIgnixaMethod(callee) || callee.DeclaringType is not { } owner)
            {
                continue;
            }

            if (IsCompilerGenerated(owner))
            {
                foreach (var generated in owner.GetMethods(DeclaredMembers))
                {
                    yield return generated;
                }

                continue;
            }

            if (CanCarryElement(callee))
            {
                yield return callee;
            }
        }
    }

    private static bool IsCompilerGenerated(Type type) =>
        type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);

    private static bool IsIgnixaMethod(MethodBase method) =>
        method.DeclaringType?.Assembly.GetName().Name?.StartsWith("Ignixa", StringComparison.Ordinal) == true;

    /// <summary>
    /// Whether a construction inside <paramref name="method"/> could reach the caller's result, which is
    /// true only when the method hands back an element.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without this the walk reports <c>trace</c>, which constructs nothing of its own but calls
    /// <c>EvaluationContext.PushIndex</c> to bind <c>$index</c>. That construction becomes a scope
    /// variable and can never be the function's result, and <c>trace</c> is a pure selector that must
    /// keep declaring "context" - so reporting it is the guard being wrong, not an acceptable false alarm.
    /// </para>
    /// <para>
    /// The test is the return type rather than a list of methods to skip, so it holds for any future
    /// helper that builds a System value for somewhere other than the result.
    /// </para>
    /// </remarks>
    private static bool CanCarryElement(MethodBase method) =>
        method is MethodInfo info && YieldsElement(info.ReturnType);

    private static bool YieldsElement(Type type)
    {
        // An open generic could be closed over an element by its caller, so it stays in the walk.
        if (type.ContainsGenericParameters || typeof(IElement).IsAssignableFrom(type))
        {
            return true;
        }

        return type.GetInterfaces()
            .Prepend(type)
            .Where(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            .Any(candidate => typeof(IElement).IsAssignableFrom(candidate.GetGenericArguments()[0]));
    }

    /// <summary>
    /// The methods <paramref name="method"/> names in its IL, including the constructors it invokes with
    /// <c>newobj</c> and the methods it takes a function pointer to.
    /// </summary>
    private static IReadOnlyList<MethodBase> CalledMethods(MethodBase method)
    {
        if (CallsCache.TryGetValue(method, out var cached))
        {
            return cached;
        }

        var called = ReadCalledMethods(method).ToList();
        CallsCache[method] = called;

        return called;
    }

    private static IEnumerable<MethodBase> ReadCalledMethods(MethodBase method)
    {
        byte[]? il;
        try
        {
            il = method.GetMethodBody()?.GetILAsByteArray();
        }
        catch (Exception)
        {
            yield break;
        }

        if (il is null)
        {
            yield break;
        }

        var typeArguments = GenericArgumentsOrNull(method.DeclaringType);
        var methodArguments = method is MethodInfo { IsGenericMethodDefinition: false, IsGenericMethod: true } generic
            ? generic.GetGenericArguments()
            : null;

        var position = 0;
        while (position < il.Length)
        {
            short value = il[position++];
            if (value == 0xFE)
            {
                if (position >= il.Length)
                {
                    yield break;
                }

                value = unchecked((short)(0xFE00 | il[position++]));
            }

            if (!OpCodesByValue.TryGetValue(value, out var opCode))
            {
                yield break;
            }

            if (opCode.OperandType == OperandType.InlineMethod && position + 4 <= il.Length)
            {
                var resolved = ResolveMethodOrNull(method.Module, BitConverter.ToInt32(il, position), typeArguments, methodArguments);
                if (resolved is not null)
                {
                    yield return NormalizeGenerics(resolved);
                }
            }

            var operandSize = OperandSize(opCode, il, position);
            if (operandSize < 0)
            {
                yield break;
            }

            position += operandSize;
        }
    }

    private static Type[]? GenericArgumentsOrNull(Type? type) =>
        type?.IsGenericType == true ? type.GetGenericArguments() : null;

    private static MethodBase NormalizeGenerics(MethodBase method) =>
        method is MethodInfo { IsGenericMethod: true, IsGenericMethodDefinition: false } generic
            ? generic.GetGenericMethodDefinition()
            : method;

    private static MethodBase? ResolveMethodOrNull(Module module, int token, Type[]? typeArguments, Type[]? methodArguments)
    {
        try
        {
            return module.ResolveMethod(token, typeArguments, methodArguments);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The operand width of <paramref name="opCode"/> in bytes, or -1 when the switch table runs past the
    /// end of the body and the walk can no longer stay in sync.
    /// </summary>
    private static int OperandSize(OpCode opCode, byte[] il, int position)
    {
        switch (opCode.OperandType)
        {
            case OperandType.InlineNone:
                return 0;
            case OperandType.ShortInlineBrTarget:
            case OperandType.ShortInlineI:
            case OperandType.ShortInlineVar:
                return 1;
            case OperandType.InlineVar:
                return 2;
            case OperandType.InlineI8:
            case OperandType.InlineR:
                return 8;
            case OperandType.InlineSwitch:
                if (position + 4 > il.Length)
                {
                    return -1;
                }

                var count = BitConverter.ToInt32(il, position);

                return count < 0 ? -1 : 4 + (count * 4);
            default:
                return 4;
        }
    }

    /// <summary>
    /// Functions that exist only to be scanned, standing in for the mistake the production library no
    /// longer makes.
    /// </summary>
    /// <remarks>
    /// The attribute is inert here: the source generator is referenced by Ignixa.FhirPath as an analyzer,
    /// and analyzers do not flow across a project reference, so nothing registers these.
    /// </remarks>
    private static class ConstructionFixtures
    {
        [FhirPathFunction("fixtureConstructsInBody", ReturnType = "context")]
        public static IEnumerable<IElement> ConstructsInBody(IEnumerable<IElement> focus) =>
            focus.Any() ? [new FunctionHelpers.PrimitiveElement(1, "integer")] : [];

        [FhirPathFunction("fixtureConstructsThroughHelper", ReturnType = "context")]
        public static IEnumerable<IElement> ConstructsThroughHelper(IEnumerable<IElement> focus) =>
            focus.Any() ? [BuildValue()] : [];

        [FhirPathFunction("fixtureSelectsFromFocus", ReturnType = "context")]
        public static IEnumerable<IElement> SelectsFromFocus(IEnumerable<IElement> focus) =>
            focus.Take(1);

        /// <summary>
        /// Constructs from inside an iterator body, which the compiler moves into a generated state
        /// machine. This is the shape <c>lowBoundary</c> has, and a walk that follows only calls misses it.
        /// </summary>
        [FhirPathFunction("fixtureConstructsInIterator", ReturnType = "context")]
        public static IEnumerable<IElement> ConstructsInIterator(IEnumerable<IElement> focus)
        {
            foreach (var element in focus)
            {
                yield return new FunctionHelpers.PrimitiveElement(element.Value ?? 0, "integer");
            }
        }

        /// <summary>
        /// Constructs a System value that goes somewhere other than the result, as <c>trace</c> does when
        /// it binds <c>$index</c>.
        /// </summary>
        [FhirPathFunction("fixtureConstructsIntoScope", ReturnType = "context")]
        public static IEnumerable<IElement> ConstructsIntoScope(IEnumerable<IElement> focus) =>
            DescribeFocus().Count > 0 ? focus : focus;

        private static IElement BuildValue() => new FunctionHelpers.PrimitiveElement(1, "integer");

        private static List<string> DescribeFocus() => [new FunctionHelpers.PrimitiveElement(1, "integer").InstanceType];
    }
}
