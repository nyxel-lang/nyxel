using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Nyxel.Compiler.Diagnostics;
using Nyxel.Compiler.Tests.Syntax;

namespace Nyxel.Compiler.Tests;

/// <summary>Diagnostic ids are a public contract (ADR-0019): well formed, unique, documented.</summary>
public partial class DiagnosticCatalogTests
{
    private static List<DiagnosticDescriptor> All() =>
        [.. typeof(DiagnosticDescriptors).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (DiagnosticDescriptor)f.GetValue(null)!)];

    [GeneratedRegex(@"^NYX\d{4}$")]
    private static partial Regex IdPattern();

    [Fact]
    public void IdsAreWellFormedAndUnique()
    {
        var ids = All().Select(d => d.Id).ToList();
        Assert.All(ids, id => Assert.Matches(IdPattern(), id));
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void EveryMessageFormats()
    {
        foreach (var descriptor in All())
        {
            var message = string.Format(CultureInfo.InvariantCulture, descriptor.MessageFormat, "x", "y", "z");
            Assert.True(message.EndsWith('.') || message.EndsWith('?'), message);
        }
    }

    [Fact]
    public void EveryIdIsDocumented()
    {
        var doc = File.ReadAllText(Path.Combine(SyntaxTestHelpers.RepoRoot, "docs", "design", "diagnostics.md"));
        Assert.All(All(), d => Assert.Contains($"`{d.Id}`", doc, StringComparison.Ordinal));
    }
}
