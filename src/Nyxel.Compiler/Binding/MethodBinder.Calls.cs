using System.Collections.Immutable;
using System.Numerics;
using Nyxel.Compiler.Diagnostics;
using Nyxel.Compiler.Symbols;
using Nyxel.Compiler.Syntax;
using SpecialType = Microsoft.CodeAnalysis.SpecialType;

namespace Nyxel.Compiler.Binding;

internal sealed partial class MethodBinder
{
    /// <summary>
    /// An argument before overload resolution. A number-only argument is bound with its natural type for now and
    /// again with the chosen parameter's type afterwards, since the parameter decides its type (ADR-0013).
    /// </summary>
    private sealed record PendingArgument(ArgumentSyntax Syntax, string? Name, BoundExpression Value, bool IsLiteral);

    private ImmutableArray<PendingArgument> BindArguments(ArgumentListSyntax syntax)
    {
        var arguments = ImmutableArray.CreateBuilder<PendingArgument>();
        foreach (var argument in syntax.Arguments)
        {
            if (argument.RefKindKeyword is { } refKind)
            {
                arguments.Add(new PendingArgument(argument, null, NotSupported(refKind, $"'{refKind.Text}' arguments"), false));
                continue;
            }
            var name = argument.NameEquals?.Name.Identifier is { IsMissing: false } identifier ? identifier.Text : null;
            var isLiteral = IsLiteralOnly(argument.Expression);
            var value = isLiteral ? BindLiteralOnly(argument.Expression, null) : BindValue(argument.Expression);
            arguments.Add(new PendingArgument(argument, name, value, isLiteral));
        }
        return arguments.ToImmutable();
    }

    private BoundExpression BindInvocation(InvocationExpressionSyntax syntax)
    {
        var target = BindExpression(syntax.Expression, null);
        var arguments = BindArguments(syntax.ArgumentList);
        switch (target)
        {
            case BoundMethodGroup group:
                return BindCall(syntax, group, arguments);
            case BoundTypeExpression { Type.IsError: false } type:
                Report(DiagnosticDescriptors.CreateWithNew, syntax.Expression, SymbolDisplay.FormatType(type.Type));
                return Bad(syntax);
            case BoundNamespaceExpression:
                Report(DiagnosticDescriptors.NotCallable, syntax.Expression);
                return Bad(syntax);
            case { HasErrors: true }:
                return Bad(syntax, target);
            default:
                Report(DiagnosticDescriptors.NotCallable, syntax.Expression);
                return Bad(syntax, target);
        }
    }

    private BoundExpression BindCall(InvocationExpressionSyntax syntax, BoundMethodGroup group, ImmutableArray<PendingArgument> arguments)
    {
        if (arguments.Any(a => a.Value.HasErrors))
        {
            return Bad(syntax);
        }
        var resolved = Resolve(syntax, group.Name, group.Methods.CastArray<Symbol>(), arguments);
        if (resolved is not { Member: MethodSymbol method } result)
        {
            return Bad(syntax);
        }
        var name = (syntax.Expression as MemberAccessExpressionSyntax)?.Name ?? (SyntaxNode)syntax.Expression;
        if (!CheckAccessible(method, name))
        {
            return Bad(syntax);
        }
        switch (group.Receiver)
        {
            case BoundTypeExpression type when !method.IsStatic:
                Report(DiagnosticDescriptors.InstanceMemberOnType, name, group.Name, SymbolDisplay.FormatType(type.Type));
                return Bad(syntax);
            case BoundExpression receiver when receiver is not BoundTypeExpression && method.IsStatic:
                Report(DiagnosticDescriptors.StaticMemberOnValue, name, group.Name, SymbolDisplay.FormatType(method.ContainingType));
                return Bad(syntax);
        }
        var instance = method.IsStatic ? null : group.Receiver;
        return new BoundCall(syntax, instance, method, result.Arguments);
    }

    private BoundExpression BindObjectCreation(ObjectCreationExpressionSyntax syntax)
    {
        if (syntax.WithClause is { } with)
        {
            return NotSupported(with.WithKeyword, "'with'");
        }
        var type = _scope.BindType(syntax.Type);
        var arguments = BindArguments(syntax.ArgumentList);
        if (type.IsError || arguments.Any(a => a.Value.HasErrors))
        {
            return Bad(syntax);
        }

        if (type is ArrayTypeSymbol array)
        {
            if (arguments.Length != array.Rank)
            {
                Report(DiagnosticDescriptors.IndexCount, syntax.ArgumentList, SymbolDisplay.FormatType(array), array.Rank, arguments.Length);
                return Bad(syntax);
            }
            var lengths = arguments.Select(a => Convert(a.IsLiteral ? BindLiteralOnly(a.Syntax.Expression, _context.Int) : a.Value, _context.Int)).ToImmutableArray();
            return new BoundArrayCreation(syntax, array, lengths);
        }

        var named = (NamedTypeSymbol)type;
        var reason = named.IsStatic ? "it is a static class"
            : named.TypeKind == TypeKind.Interface ? "it is an interface"
            : named.TypeKind == TypeKind.Enum ? "it is an enum"
            : named.TypeKind == TypeKind.Delegate ? "it is a function type"
            : named.IsAbstract ? "it is abstract"
            : null;
        if (reason is not null)
        {
            Report(DiagnosticDescriptors.CannotCreate, syntax.Type, SymbolDisplay.FormatType(named), reason);
            return Bad(syntax);
        }
        var constructors = named.GetMembers(MethodSymbol.ConstructorName).OfType<MethodSymbol>().Where(c => !c.IsStatic).ToImmutableArray();
        if (named.IsValueType && arguments.Length == 0 && !constructors.Any(c => c.Parameters.Length == 0))
        {
            return new BoundObjectCreation(syntax, named, null, []);
        }
        var resolved = Resolve(syntax, $"init {SymbolDisplay.FormatType(named)}", constructors.CastArray<Symbol>(), arguments);
        if (resolved is not { Member: MethodSymbol constructor } result || !CheckAccessible(constructor, syntax.Type))
        {
            return Bad(syntax);
        }
        return new BoundObjectCreation(syntax, named, constructor, result.Arguments);
    }

    private BoundExpression BindElementAccess(ElementAccessExpressionSyntax syntax)
    {
        var receiver = BindValue(syntax.Expression);
        if (syntax.ArgumentList.Arguments.FirstOrDefault(a => a.Expression is RangeExpressionSyntax) is { } slice)
        {
            return NotSupported(slice, "A slice");
        }
        var arguments = BindArguments(syntax.ArgumentList);
        if (receiver.HasErrors || arguments.Any(a => a.Value.HasErrors))
        {
            return Bad(syntax, receiver);
        }

        if (receiver.Type is ArrayTypeSymbol array)
        {
            if (arguments.Length != array.Rank)
            {
                Report(DiagnosticDescriptors.IndexCount, syntax.ArgumentList, SymbolDisplay.FormatType(array), array.Rank, arguments.Length);
                return Bad(syntax, receiver);
            }
            // As in C#, an index can be any of int, uint, long, ulong; the smaller types become int.
            var indices = arguments.Select(a =>
                a.IsLiteral ? BindLiteralOnly(a.Syntax.Expression, _context.Int)
                : a.Value.Type.SpecialType is SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 ? a.Value
                : Convert(a.Value, _context.Int)).ToImmutableArray();
            return new BoundArrayAccess(syntax, receiver, indices, array.ElementType);
        }

        var indexers = LookupMembers(receiver.Type, PropertySymbol.IndexerName).OfType<PropertySymbol>().ToImmutableArray();
        if (indexers.Length == 0)
        {
            Report(DiagnosticDescriptors.NotIndexable, syntax.Expression, SymbolDisplay.FormatType(receiver.Type));
            return Bad(syntax, receiver);
        }
        var resolved = Resolve(syntax, $"{SymbolDisplay.FormatType(receiver.Type)}[]", indexers.CastArray<Symbol>(), arguments);
        if (resolved is not { Member: PropertySymbol indexer } result || !CheckAccessible(indexer, syntax.ArgumentList))
        {
            return Bad(syntax, receiver);
        }
        return new BoundIndexerAccess(syntax, receiver, indexer, result.Arguments);
    }

    // Overload resolution ----------------------------------------------------------------------------------------
    //
    // C#'s rules (the "overload resolution" section of the C# specification), as far as the first slice of M2 needs
    // them: arguments matched by position and by name, optional parameters, implicit conversions, and the better
    // function member by better conversion from each argument. Generic functions, 'params' in expanded form beyond
    // zero arguments and lambdas come later. Tests compare the choice with Roslyn's (OverloadResolutionTests).

    private sealed record Resolution(Symbol Member, ImmutableArray<BoundArgument> Arguments);

    private sealed class Candidate(Symbol member, ImmutableArray<ParameterSymbol> parameters)
    {
        public Symbol Member => member;

        public ImmutableArray<ParameterSymbol> Parameters => parameters;

        /// <summary>For each argument, the index of its parameter.</summary>
        public int[] ParameterOf { get; set; } = [];

        public bool UsesDefaults { get; set; }

        public TypeSymbol ParameterType(int argument) => parameters[ParameterOf[argument]].Type;
    }

    private Resolution? Resolve(SyntaxNode syntax, string name, ImmutableArray<Symbol> members, ImmutableArray<PendingArgument> arguments)
    {
        // First with C#'s conversions, so that a call C# accepts picks what C# picks: in Math.Max(1, 2.5) the 2.5 is a
        // double. Only when nothing applies may a decimal literal become a float or decimal parameter, as in
        // new Vector3(0, 1.5, 0) (ADR-0013).
        var skippedGeneric = members.Any(m => m is MethodSymbol { IsGeneric: true });
        var candidates = members.Where(m => m is not MethodSymbol { IsGeneric: true }).ToList();
        var applicable = Applicable(candidates, arguments, decimalLiterals: false);
        if (applicable.Count == 0)
        {
            applicable = Applicable(candidates, arguments, decimalLiterals: true);
        }

        if (applicable.Count == 0)
        {
            ReportNotApplicable(syntax, name, members, arguments, skippedGeneric);
            return null;
        }
        var best = applicable.FirstOrDefault(c => applicable.All(other => ReferenceEquals(other, c) || Better(c, other, arguments) > 0));
        if (best is null)
        {
            var (first, second) = (applicable[0], applicable[1]);
            Report(DiagnosticDescriptors.AmbiguousCall, syntax, SymbolDisplay.Format(first.Member), SymbolDisplay.Format(second.Member));
            return null;
        }

        var bound = ImmutableArray.CreateBuilder<BoundArgument>();
        for (var i = 0; i < arguments.Length; i++)
        {
            var parameter = best.Parameters[best.ParameterOf[i]];
            var value = arguments[i].IsLiteral ? BindLiteralOnly(arguments[i].Syntax.Expression, parameter.Type) : arguments[i].Value;
            bound.Add(new BoundArgument(parameter, Convert(value, parameter.Type, arguments[i].Syntax.Expression), arguments[i].Name));
        }
        return new Resolution(best.Member, bound.ToImmutable());
    }

    private static List<Candidate> Applicable(List<Symbol> members, ImmutableArray<PendingArgument> arguments, bool decimalLiterals)
    {
        var applicable = new List<Candidate>();
        foreach (var member in members)
        {
            var candidate = new Candidate(member, ParametersOf(member));
            if (IsApplicable(candidate, arguments, decimalLiterals))
            {
                applicable.Add(candidate);
            }
        }
        return applicable;
    }

    private static ImmutableArray<ParameterSymbol> ParametersOf(Symbol member) => member switch
    {
        MethodSymbol method => method.Parameters,
        PropertySymbol property => property.Parameters,
        _ => [],
    };

    private static bool IsApplicable(Candidate candidate, ImmutableArray<PendingArgument> arguments, bool decimalLiterals)
    {
        var parameters = candidate.Parameters;
        var parameterOf = new int[arguments.Length];
        var used = new bool[parameters.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            var index = arguments[i].Name is { } name ? IndexOfParameter(parameters, name) : i;
            if (index < 0 || index >= parameters.Length || used[index])
            {
                return false;
            }
            used[index] = true;
            parameterOf[i] = index;
            var parameter = parameters[index];
            if (parameter.RefKind != RefKind.None || ArgumentConversion(arguments[i], parameter.Type, decimalLiterals) is null)
            {
                return false;
            }
        }
        // A parameter without an argument must be optional; an unused 'params' one gets an empty array, as in C#.
        for (var p = 0; p < parameters.Length; p++)
        {
            if (!used[p] && !parameters[p].HasDefaultValue && !parameters[p].IsParams)
            {
                return false;
            }
        }
        candidate.ParameterOf = parameterOf;
        candidate.UsesDefaults = used.Any(u => !u);
        return true;
    }

    private static int IndexOfParameter(ImmutableArray<ParameterSymbol> parameters, string name)
    {
        for (var i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].Name == name)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// How an argument converts to a parameter type. A whole-number literal converts to any integer type that holds
    /// it, as a C# constant does; with <paramref name="decimalLiterals"/>, a decimal literal converts to <c>float</c>
    /// and <c>decimal</c> too.
    /// </summary>
    private static ConversionKind? ArgumentConversion(PendingArgument argument, TypeSymbol parameterType, bool decimalLiterals)
    {
        if (Conversions.ClassifyImplicit(argument.Value.Type, parameterType) is { } conversion)
        {
            return conversion;
        }
        if (!argument.IsLiteral || !IsLiteralTarget(parameterType.SpecialType))
        {
            return null;
        }
        return argument.Value.ConstantValue switch
        {
            float or double => decimalLiterals && !Conversions.IsIntegral(parameterType.SpecialType) ? ConversionKind.ImplicitNumeric : null,
            { } integer when Conversions.IsIntegral(parameterType.SpecialType) =>
                FitsIntegral(new BigInteger(System.Convert.ToDecimal(integer, System.Globalization.CultureInfo.InvariantCulture)), parameterType.SpecialType)
                    ? ConversionKind.ImplicitNumeric
                    : null,
            _ => ConversionKind.ImplicitNumeric,
        };
    }

    /// <summary>Positive when <paramref name="p"/> is the better function member, negative when <paramref name="q"/> is.</summary>
    private static int Better(Candidate p, Candidate q, ImmutableArray<PendingArgument> arguments)
    {
        var pBetter = false;
        var qBetter = false;
        for (var i = 0; i < arguments.Length; i++)
        {
            var comparison = BetterConversion(arguments[i].Value.Type, p.ParameterType(i), q.ParameterType(i));
            pBetter |= comparison > 0;
            qBetter |= comparison < 0;
        }
        if (pBetter != qBetter)
        {
            return pBetter ? 1 : -1;
        }
        if (!pBetter && p.UsesDefaults != q.UsesDefaults)
        {
            // Without a difference in the arguments, the one that needs no default values is better.
            return q.UsesDefaults ? 1 : -1;
        }
        return 0;
    }

    /// <summary>C#'s better conversion from an argument of type <paramref name="source"/> to one of two parameter types.</summary>
    private static int BetterConversion(TypeSymbol source, TypeSymbol first, TypeSymbol second)
    {
        if (ReferenceEquals(first, second))
        {
            return 0;
        }
        if (ReferenceEquals(source, first))
        {
            return 1;
        }
        if (ReferenceEquals(source, second))
        {
            return -1;
        }
        var firstToSecond = Conversions.ClassifyImplicit(first, second) is not null;
        var secondToFirst = Conversions.ClassifyImplicit(second, first) is not null;
        if (firstToSecond != secondToFirst)
        {
            return firstToSecond ? 1 : -1;
        }
        if (SignedBetter(first.SpecialType, second.SpecialType))
        {
            return 1;
        }
        return SignedBetter(second.SpecialType, first.SpecialType) ? -1 : 0;

        // A signed integer type is better than an unsigned one at least as wide.
        static bool SignedBetter(SpecialType signed, SpecialType unsigned) => (signed, unsigned) switch
        {
            (SpecialType.System_SByte, SpecialType.System_Byte or SpecialType.System_UInt16 or SpecialType.System_UInt32 or SpecialType.System_UInt64) => true,
            (SpecialType.System_Int16, SpecialType.System_UInt16 or SpecialType.System_UInt32 or SpecialType.System_UInt64) => true,
            (SpecialType.System_Int32, SpecialType.System_UInt32 or SpecialType.System_UInt64) => true,
            (SpecialType.System_Int64, SpecialType.System_UInt64) => true,
            _ => false,
        };
    }

    private void ReportNotApplicable(SyntaxNode syntax, string name, ImmutableArray<Symbol> members, ImmutableArray<PendingArgument> arguments, bool skippedGeneric)
    {
        if (skippedGeneric && members.All(m => m is MethodSymbol { IsGeneric: true }))
        {
            Report(DiagnosticDescriptors.NotSupportedYet, syntax, "Calling a generic function");
            return;
        }
        // With a single candidate, say which argument is wrong.
        if (members.Length == 1)
        {
            var parameters = ParametersOf(members[0]);
            if (arguments.FirstOrDefault(a => a.Name is { } n && parameters.All(p => p.Name != n)) is { } unknown)
            {
                Report(DiagnosticDescriptors.NoSuchParameter, unknown.Syntax.NameEquals!.Name, SymbolDisplay.Format(members[0]), unknown.Name!);
                return;
            }
            if (arguments.Length <= parameters.Length && arguments.All(a => a.Name is null))
            {
                for (var i = 0; i < arguments.Length; i++)
                {
                    if (ArgumentConversion(arguments[i], parameters[i].Type, decimalLiterals: true) is null)
                    {
                        Report(DiagnosticDescriptors.CannotConvert, arguments[i].Syntax.Expression,
                            SymbolDisplay.FormatType(arguments[i].Value.Type), SymbolDisplay.FormatType(parameters[i].Type));
                        return;
                    }
                }
            }
        }
        Report(DiagnosticDescriptors.NoMatchingOverload, syntax, name, string.Join(", ", arguments.Select(a => SymbolDisplay.FormatType(a.Value.Type))));
    }
}
