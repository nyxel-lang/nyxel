namespace Nyxel.Compiler.Symbols;

/// <summary>Turning one naming style into another, where Nyxel and C# write a name differently.</summary>
internal static class Naming
{
    /// <summary>
    /// <c>Amount</c> as <c>amount</c>, <c>HP</c> as <c>hp</c>, <c>HPMax</c> as <c>hpMax</c>: the leading capitals in lower
    /// case, except the last one when a lower-case letter follows it (the rule of .NET's <c>JsonNamingPolicy.CamelCase</c>).
    /// The parameters of a case's C# constructor are named so (ADR-0024).
    /// </summary>
    public static string CamelCase(string name)
    {
        if (name.Length == 0 || !char.IsUpper(name[0]))
        {
            return name;
        }
        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (i == 1 && !char.IsUpper(chars[i]))
            {
                break;
            }
            if (i > 0 && i + 1 < chars.Length && !char.IsUpper(chars[i + 1]))
            {
                break;
            }
            chars[i] = char.ToLowerInvariant(chars[i]);
        }
        return new string(chars);
    }
}
