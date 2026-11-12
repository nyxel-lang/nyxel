using System.Collections.Immutable;
using Nyxel.Compiler.Syntax;

namespace Nyxel.Compiler.Symbols;

public enum MethodKind
{
    Ordinary,
    Constructor,
    Operator,
    Conversion,

    /// <summary>The <c>get</c> of a property.</summary>
    PropertyGet,

    /// <summary>The <c>set</c> of a property.</summary>
    PropertySet,
}

public enum RefKind
{
    None,
    Ref,
    Out,
    In,
}

/// <summary>A function, an <c>init</c> (named <c>.ctor</c>, as in .NET), an operator or a conversion.</summary>
public abstract class MethodSymbol : Symbol
{
    public const string ConstructorName = ".ctor";

    public override SymbolKind Kind => SymbolKind.Method;

    public abstract MethodKind MethodKind { get; }

    public abstract NamedTypeSymbol ContainingType { get; }

    public override Symbol ContainingSymbol => ContainingType;

    /// <summary>A function without <c>self</c> (ADR-0008).</summary>
    public abstract bool IsStatic { get; }

    /// <summary><c>void</c> when the function returns nothing.</summary>
    public abstract TypeSymbol ReturnType { get; }

    public abstract ImmutableArray<ParameterSymbol> Parameters { get; }

    public virtual ImmutableArray<TypeParameterSymbol> TypeParameters => [];

    public bool IsGeneric => TypeParameters.Length > 0;

    public bool ReturnsVoid => ReturnType.IsVoid;

    public virtual bool IsAbstract => false;

    public virtual bool IsVirtual => false;

    public virtual bool IsOverride => false;

    public virtual bool IsAsync => false;

    /// <summary>
    /// A struct method that doesn't change the value it is called on: Nyxel's <c>self</c> methods and getters, C#'s
    /// <c>readonly</c> members and the methods of a <c>readonly struct</c> (ADR-0032).
    /// </summary>
    public virtual bool IsReadOnly => false;

    /// <summary><see cref="RefKind.Ref"/> or <see cref="RefKind.In"/> for a C# method that returns <c>ref</c> or <c>ref readonly</c>.</summary>
    public virtual RefKind ReturnRefKind => RefKind.None;

    /// <summary>The method this one overrides, as its definition.</summary>
    public virtual MethodSymbol? OverriddenMethod => null;

    /// <summary>For a member of a constructed type (<c>List&lt;int&gt;.Add</c>), the member of the definition.</summary>
    public virtual MethodSymbol OriginalDefinition => this;
}

public abstract class FieldSymbol : Symbol
{
    public override SymbolKind Kind => SymbolKind.Field;

    public abstract NamedTypeSymbol ContainingType { get; }

    public override Symbol ContainingSymbol => ContainingType;

    public abstract TypeSymbol Type { get; }

    public abstract bool IsStatic { get; }

    /// <summary>A <c>let</c> field, or a C# <c>readonly</c> field.</summary>
    public abstract bool IsReadOnly { get; }

    /// <summary>A C# <c>const</c> such as <c>int.MaxValue</c>: a static value known at compile time.</summary>
    public virtual bool IsConst => false;

    public virtual object? ConstantValue => null;

    public virtual FieldSymbol OriginalDefinition => this;
}

/// <summary>A property, or an indexer (named <c>this[]</c>, as in Roslyn).</summary>
public abstract class PropertySymbol : Symbol
{
    public const string IndexerName = "this[]";

    public override SymbolKind Kind => SymbolKind.Property;

    public abstract NamedTypeSymbol ContainingType { get; }

    public override Symbol ContainingSymbol => ContainingType;

    public abstract TypeSymbol Type { get; }

    public abstract bool IsStatic { get; }

    public bool IsIndexer => Name == IndexerName;

    public virtual ImmutableArray<ParameterSymbol> Parameters => [];

    public abstract bool HasGetter { get; }

    public abstract bool HasSetter { get; }

    /// <summary>Who can read the property: its own visibility, or the narrower one written on <c>get</c>.</summary>
    public virtual Accessibility GetterAccessibility => Accessibility;

    /// <summary>Who can set the property: its own visibility, or the narrower one written on <c>set</c> (<c>private set</c>).</summary>
    public virtual Accessibility SetterAccessibility => Accessibility;

    /// <summary><see cref="RefKind.Ref"/> or <see cref="RefKind.In"/> for a C# indexer that returns <c>ref</c> or <c>ref readonly</c> (<c>Span&lt;T&gt;</c>).</summary>
    public virtual RefKind ReturnRefKind => RefKind.None;

    public virtual PropertySymbol OriginalDefinition => this;
}

public abstract class ParameterSymbol : Symbol
{
    public override SymbolKind Kind => SymbolKind.Parameter;

    public abstract TypeSymbol Type { get; }

    public abstract int Ordinal { get; }

    public virtual RefKind RefKind => RefKind.None;

    /// <summary>C#'s <c>params</c>: the last parameter takes any number of arguments.</summary>
    public virtual bool IsParams => false;

    public virtual bool HasDefaultValue => false;

    public virtual object? DefaultValue => null;
}

/// <summary>A <c>let</c> or <c>var</c> inside a function, or a loop variable.</summary>
public sealed class LocalSymbol : Symbol
{
    internal LocalSymbol(string name, TypeSymbol type, bool isReadOnly, SyntaxToken identifier)
    {
        Name = name;
        Type = type;
        IsReadOnly = isReadOnly;
        Identifier = identifier;
    }

    public override SymbolKind Kind => SymbolKind.Local;

    public override string Name { get; }

    public TypeSymbol Type { get; }

    public bool IsReadOnly { get; }

    /// <summary>Where the name is declared.</summary>
    public SyntaxToken Identifier { get; }
}

/// <summary>A member of a constructed type: the definition's member with the type arguments filled in.</summary>
internal sealed class SubstitutedMethodSymbol(MethodSymbol original, NamedTypeSymbol containingType, TypeMap map) : MethodSymbol
{
    private ImmutableArray<ParameterSymbol> _parameters;

    public override string Name => original.Name;

    public override MethodKind MethodKind => original.MethodKind;

    public override NamedTypeSymbol ContainingType => containingType;

    public override Accessibility Accessibility => original.Accessibility;

    public override bool IsStatic => original.IsStatic;

    public override TypeSymbol ReturnType => map.Substitute(original.ReturnType);

    public override ImmutableArray<ParameterSymbol> Parameters
    {
        get
        {
            if (_parameters.IsDefault)
            {
                _parameters = [.. original.Parameters.Select(p => new SubstitutedParameterSymbol(p, this, map.Substitute(p.Type)))];
            }
            return _parameters;
        }
    }

    public override ImmutableArray<TypeParameterSymbol> TypeParameters => original.TypeParameters;

    public override bool IsAbstract => original.IsAbstract;

    public override bool IsVirtual => original.IsVirtual;

    public override bool IsOverride => original.IsOverride;

    public override bool IsReadOnly => original.IsReadOnly;

    public override RefKind ReturnRefKind => original.ReturnRefKind;

    public override MethodSymbol? OverriddenMethod => original.OverriddenMethod;

    public override MethodSymbol OriginalDefinition => original.OriginalDefinition;
}

internal sealed class SubstitutedFieldSymbol(FieldSymbol original, NamedTypeSymbol containingType, TypeMap map) : FieldSymbol
{
    public override string Name => original.Name;

    public override NamedTypeSymbol ContainingType => containingType;

    public override Accessibility Accessibility => original.Accessibility;

    public override TypeSymbol Type => map.Substitute(original.Type);

    public override bool IsStatic => original.IsStatic;

    public override bool IsReadOnly => original.IsReadOnly;

    public override bool IsConst => original.IsConst;

    public override object? ConstantValue => original.ConstantValue;

    public override FieldSymbol OriginalDefinition => original.OriginalDefinition;
}

internal sealed class SubstitutedPropertySymbol(PropertySymbol original, NamedTypeSymbol containingType, TypeMap map) : PropertySymbol
{
    private ImmutableArray<ParameterSymbol> _parameters;

    public override string Name => original.Name;

    public override NamedTypeSymbol ContainingType => containingType;

    public override Accessibility Accessibility => original.Accessibility;

    public override TypeSymbol Type => map.Substitute(original.Type);

    public override bool IsStatic => original.IsStatic;

    public override ImmutableArray<ParameterSymbol> Parameters
    {
        get
        {
            if (_parameters.IsDefault)
            {
                _parameters = [.. original.Parameters.Select(p => new SubstitutedParameterSymbol(p, this, map.Substitute(p.Type)))];
            }
            return _parameters;
        }
    }

    public override bool HasGetter => original.HasGetter;

    public override bool HasSetter => original.HasSetter;

    public override Accessibility GetterAccessibility => original.GetterAccessibility;

    public override Accessibility SetterAccessibility => original.SetterAccessibility;

    public override RefKind ReturnRefKind => original.ReturnRefKind;

    public override PropertySymbol OriginalDefinition => original.OriginalDefinition;
}

internal sealed class SubstitutedParameterSymbol(ParameterSymbol original, Symbol containingSymbol, TypeSymbol type) : ParameterSymbol
{
    public override string Name => original.Name;

    public override Symbol ContainingSymbol => containingSymbol;

    public override TypeSymbol Type => type;

    public override int Ordinal => original.Ordinal;

    public override RefKind RefKind => original.RefKind;

    public override bool IsParams => original.IsParams;

    public override bool HasDefaultValue => original.HasDefaultValue;

    public override object? DefaultValue => original.DefaultValue;
}
