namespace Nyxel.Compiler.Symbols;

public enum SymbolKind
{
    Namespace,
    NamedType,
    ArrayType,
    TypeParameter,
    ErrorType,
    Method,
    Field,
    Property,
    Parameter,
    Local,
}

public enum Accessibility
{
    Private,
    Protected,
    Internal,
    Public,
}

/// <summary>
/// Something with a name that code can refer to (docs/design/semantic-analysis.md). Nyxel declarations and the types
/// read from referenced assemblies share these classes; binding doesn't care where a symbol comes from.
/// </summary>
public abstract class Symbol
{
    public abstract SymbolKind Kind { get; }

    public abstract string Name { get; }

    public virtual Symbol? ContainingSymbol => null;

    public virtual Accessibility Accessibility => Accessibility.Public;

    public override string ToString() => SymbolDisplay.Format(this);
}
