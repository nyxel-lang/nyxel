using Nyxel.Compiler.Diagnostics;
using Nyxel.Compiler.Symbols;
using Nyxel.Compiler.Syntax;

namespace Nyxel.Compiler.Binding;

internal sealed partial class MethodBinder
{
    /// <summary>
    /// Why a struct value can't be changed here, or null when it can (ADR-0032 decision 1, ADR-0044). Changing means
    /// setting one of its fields or properties, or calling one of its <c>var self</c> functions. <c>Root</c> is the
    /// part of the expression that decides: a <c>let</c> local, a parameter, <c>self</c>, a <c>let</c> field, or the
    /// property, indexer or call that gave a copy.
    /// </summary>
    private sealed record Unchangeable(BoundExpression Root, bool IsCopy, string? Reason);

    private Unchangeable? WhyCantChange(BoundExpression value)
    {
        switch (value)
        {
            case BoundLocalAccess or BoundParameterAccess when Narrowable(value) is var (_, declared) && declared.IsNullable && declared.IsValueType:
                // A checked S? is read as the S inside the Nullable<S>: a copy, which C# can't change in place either.
                return new(value, true, null);
            case BoundLocalAccess { Local.IsReadOnly: true } local:
                return new(local, false, local.Local.Identifier.Parent is ForStatementSyntax
                    ? "is the variable of a 'for' loop and can't change; copy it into a 'var' first"
                    : "is declared with 'let'; declare it with 'var'");
            case BoundLocalAccess or BoundArrayAccess:
                return null;
            case BoundParameterAccess parameter:
                return new(parameter, false, "is a parameter, which can't change; copy it into a 'var' first");
            case BoundSelf self:
                return CanChangeSelf ? null
                    : new(self, false, _method is { MethodKind: MethodKind.PropertyGet }
                        ? "is 'self' in a 'get', which can't change the value"
                        : "is 'self' in a function without 'var self'; write 'var self' to let the function change the value");
            case BoundFieldAccess field:
                if (field.Field.IsReadOnly && !IsLetFieldInOwnInit(field))
                {
                    return new(field, false, field.Field is SourceFieldSymbol ? "is a 'let' field" : "is a read-only field");
                }
                // A field of an object is in the object; a field of a struct is in the struct.
                return field.Receiver is { Type.IsValueType: true } receiver ? WhyCantChange(receiver) : null;
            case BoundPropertyAccess { Property.ReturnRefKind: RefKind.Ref } or BoundIndexerAccess { Indexer.ReturnRefKind: RefKind.Ref }
                or BoundCall { Method.ReturnRefKind: RefKind.Ref }:
                // Span<T> elements and functions that return 'ref' point to the storage itself.
                return null;
            case BoundPropertyAccess { Property.ReturnRefKind: RefKind.In } or BoundIndexerAccess { Indexer.ReturnRefKind: RefKind.In }
                or BoundCall { Method.ReturnRefKind: RefKind.In }:
                return new(value, false, "is read-only storage ('ref readonly', such as an element of a 'ReadOnlySpan')");
            default:
                // Properties, ordinary indexers (List<T>, dictionaries), calls and the rest give a copy.
                return new(value, true, null);
        }
    }

    /// <summary>
    /// <c>self</c> of a struct can change in an <c>init</c>, a <c>set</c> and a <c>var self</c> function; not in a
    /// <c>self</c> function or a <c>get</c> (ADR-0008, ADR-0044).
    /// </summary>
    private bool CanChangeSelf => _method is { IsReadOnly: false };

    /// <summary>A <c>let</c> field is set where it is declared and in an <c>init</c> of its type, through <c>self</c> (ADR-0006).</summary>
    private bool IsLetFieldInOwnInit(BoundFieldAccess field) =>
        _method is { MethodKind: MethodKind.Constructor } && ReferenceEquals(field.Field.ContainingType, _containingType)
        && field.Receiver is BoundSelf && field.Field is SourceFieldSymbol;

    /// <summary>
    /// Reports when <paramref name="action"/> (setting a field or property, calling a <c>var self</c> function)
    /// would change a struct that can't change here. Values of classes always can: the change is in the object.
    /// </summary>
    private bool CheckCanChange(BoundExpression receiver, string action)
    {
        if (receiver.HasErrors || !receiver.Type.IsValueType || receiver.Type.IsNullable || WhyCantChange(receiver) is not { } why)
        {
            return true;
        }
        var text = why.Root.Syntax.ToString();
        if (why.IsCopy)
        {
            Report(DiagnosticDescriptors.ChangesStructCopy, why.Root.Syntax, action, text);
        }
        else
        {
            Report(DiagnosticDescriptors.ChangesReadOnlyStruct, why.Root.Syntax, action, text, why.Reason!);
        }
        return false;
    }

    /// <summary>A Nyxel <c>var self</c> function changes the struct it is called on (ADR-0008).</summary>
    private bool CheckVarSelfCall(BoundCall call) =>
        call is not { Method: SourceMethodSymbol { HasVarSelf: true } method, Receiver: { } receiver }
        || CheckCanChange(receiver, $"Calling '{method.Name}', a 'var self' function,");

    /// <summary>
    /// A C# struct method that isn't <c>readonly</c>, called for no value: that can only be for changing the struct,
    /// so it must be called where the struct can change (ADR-0032 decision 3). With its value used, it is called on a
    /// copy as in C#.
    /// </summary>
    private void CheckUnusedCall(BoundCall call)
    {
        if (call is { Method: not SourceMethodSymbol and { IsStatic: false, IsReadOnly: false } method, Receiver: { } receiver }
            && method.ContainingType.TypeKind == TypeKind.Struct)
        {
            CheckCanChange(receiver, $"Calling '{method.Name}' without using its value (C# doesn't mark it 'readonly', so it may change the struct)");
        }
    }
}
