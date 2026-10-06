using System.Text.Json;
using System.Text.RegularExpressions;
using Nyxel.Compiler.Syntax;

namespace Nyxel.Compiler.Tests.Syntax;

/// <summary>
/// The VSCode TextMate grammar (tools/vscode-nyxel) and the lexer must agree on what is a keyword, or the editor
/// highlights words the compiler treats as names and the other way round.
/// </summary>
public partial class GrammarConsistencyTests
{
    // Word alternatives inside \b...\b in the grammar's regexes: \b(if|else)\b, \bnew\b, \b(array|array2d)\b.
    [GeneratedRegex(@"\\b\(?((?:[a-z][a-z0-9]*\|)*[a-z][a-z0-9]*)\)?\\b")]
    private static partial Regex WordGroup();

    [GeneratedRegex(@"\((?:\?:)?\(?((?:[a-z][a-z0-9]*\|)*[a-z][a-z0-9]*)\)")]
    private static partial Regex Group();

    private static HashSet<string> GrammarWords()
    {
        var path = Path.Combine(SyntaxTestHelpers.RepoRoot, "tools", "vscode-nyxel", "syntaxes", "nyxel.tmLanguage.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var match in FindMatches(document.RootElement))
        {
            foreach (var regex in new[] { WordGroup(), Group() })
            {
                foreach (Match m in regex.Matches(match))
                {
                    words.UnionWith(m.Groups[1].Value.Split('|'));
                }
            }
        }
        return words;
    }

    private static IEnumerable<string> FindMatches(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name is "match" or "begin" && property.Value.ValueKind == JsonValueKind.String)
                    {
                        yield return property.Value.GetString()!;
                    }
                    foreach (var nested in FindMatches(property.Value))
                    {
                        yield return nested;
                    }
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in FindMatches(item))
                    {
                        yield return nested;
                    }
                }
                break;
        }
    }

    [Fact]
    public void EveryKeywordIsHighlighted()
    {
        var words = GrammarWords();
        Assert.All(SyntaxFacts.GetKeywordTexts(), keyword => Assert.Contains(keyword, words));
    }

    [Fact]
    public void EveryHighlightedWordIsAKeyword()
    {
        var keywords = SyntaxFacts.GetKeywordTexts()
            .Append(SyntaxFacts.GetContextualKeyword)
            .Append(SyntaxFacts.SetContextualKeyword)
            .ToHashSet(StringComparer.Ordinal);
        Assert.All(GrammarWords(), word => Assert.Contains(word, keywords));
    }
}
