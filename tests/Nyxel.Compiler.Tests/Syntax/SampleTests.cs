using Nyxel.Compiler.Syntax;
using Nyxel.Compiler.Text;
using static Nyxel.Compiler.Tests.Syntax.SyntaxTestHelpers;

namespace Nyxel.Compiler.Tests.Syntax;

/// <summary>
/// The M1 acceptance check: every file in samples/ parses without diagnostics, round-trips, and prints the tree
/// recorded in Syntax/Snapshots. After an intended change to the tree, regenerate the snapshots with
/// NYXEL_UPDATE_SNAPSHOTS=1 and review the diff.
/// </summary>
public class SampleTests
{
    private static readonly string SamplesDirectory = Path.Combine(RepoRoot, "samples");

    private static readonly string SnapshotDirectory = Path.Combine(RepoRoot, "tests", "Nyxel.Compiler.Tests", "Syntax", "Snapshots");

    public static TheoryData<string> Samples()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(SamplesDirectory, "*" + LanguageInfo.SourceFileExtension, SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetRelativePath(SamplesDirectory, file).Replace('\\', '/'));
        }
        return data;
    }

    private static SyntaxTree ParseSample(string sample)
    {
        var text = File.ReadAllText(Path.Combine(SamplesDirectory, sample)).ReplaceLineEndings("\n");
        return SyntaxTree.Parse(SourceText.From(text, sample));
    }

    [Fact]
    public void AllElevenSamplesAreFound() => Assert.Equal(11, Samples().Count);

    [Theory]
    [MemberData(nameof(Samples))]
    public void SampleParsesWithoutDiagnostics(string sample)
    {
        var tree = ParseSample(sample);
        Assert.True(tree.Diagnostics.IsEmpty, string.Join("\n", tree.Diagnostics));
        Assert.DoesNotContain(tree.Root.DescendantTokens(), t => t.IsMissing);
        Assert.Equal(tree.Text.ToString(), tree.Root.ToFullString());
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void SampleTreeMatchesSnapshot(string sample)
    {
        var tree = ParseSample(sample);
        var actual = SyntaxTreePrinter.Print(tree.Root, tree.Text);
        var snapshot = Path.Combine(SnapshotDirectory, sample.Replace('/', '.') + ".tree");
        if (Environment.GetEnvironmentVariable("NYXEL_UPDATE_SNAPSHOTS") == "1")
        {
            Directory.CreateDirectory(SnapshotDirectory);
            File.WriteAllText(snapshot, actual);
            return;
        }
        Assert.True(File.Exists(snapshot), $"Missing snapshot {snapshot}; run the tests with NYXEL_UPDATE_SNAPSHOTS=1.");
        Assert.Equal(File.ReadAllText(snapshot).ReplaceLineEndings("\n"), actual);
    }

    /// <summary>
    /// The parser must never throw and must keep every character, whatever the input. Mangle the samples by
    /// deleting, duplicating and swapping characters at random (fixed seed) and check both.
    /// </summary>
    [Theory]
    [MemberData(nameof(Samples))]
    public void MangledSamplesNeverThrowAndStayLossless(string sample)
    {
        var original = File.ReadAllText(Path.Combine(SamplesDirectory, sample)).ReplaceLineEndings("\n");
        var random = new Random(sample.Length);
        for (var round = 0; round < 200; round++)
        {
            var chars = original.ToList();
            for (var edit = 0; edit < 1 + (round % 8); edit++)
            {
                var index = random.Next(chars.Count);
                switch (random.Next(3))
                {
                    case 0:
                        chars.RemoveAt(index);
                        break;
                    case 1:
                        chars.Insert(index, chars[random.Next(chars.Count)]);
                        break;
                    default:
                        (chars[index], chars[^1]) = (chars[^1], chars[index]);
                        break;
                }
            }
            var text = new string([.. chars]);
            var tree = SyntaxTree.Parse(text);
            Assert.Equal(text, tree.Root.ToFullString());
            AssertSeparatedListsAlternate(tree.Root);
        }
    }

    /// <summary>
    /// The samples have no raw strings or C# habits, so a source with them (and tuples, slices, enum errors) gets
    /// mangled too.
    /// </summary>
    [Fact]
    public void MangledLiteralsNeverThrowAndStayLossless()
    {
        const string original = """"
            [Flags]
            flags enum L : byte {
                case A = 1
                case B = L.A | 2
            }

            struct enum S {
                case X(A: int) = 1
            }

            class C {
                func F(self) {
                    if hit is Damage.Burn(var a, var s) and s is not S.X {
                    }
                    let parts = line.Split(',')
                    let quote = '\''
                    let pattern = """\d+\.\d+"""
                    let help = """
                        Usage: "spawn" {kind}
                        """
                    let json = $$"""
                        { "hp": {{self.hp}}, "name": "{{self.Name}}" }
                        """
                    let report = $"""Wave {self.wave}: {self.alive.Count:D2} alive"""
                    var hits: array<RaycastHit?> = new array<RaycastHit?>(16)
                    let a: Enemy?[] = new Enemy?[self.count]
                    let b = new int[] { 1, 2 }
                    let d = @"C:\x" + $@"{d}\y"
                    self.total +=
                        bonus
                    let (lo, hi) = self.Range()
                    let r: (Min: int, Max: int)? = (Min = lo, Max = hi)
                    for (i, item) in self.items.Index() {
                        (lo, hi) = (hi, lo)
                    }
                    let c: List<(int A, int)> = F((A: 1, 2), (x))
                    outer: for x in xs {
                        break outer
                    }
                    for i in (0..<self.items.Count).Reversed() {
                        let s = name[1...] + name[..<3] + path[..^4] + text[1..] + items[^1]
                    }
                    let xs: List<int> = [1, 2, ..ys, "a": 3]
                    let grid: int[,] = new array2d<int>(w, h)
                    grid[x, y] = new int[2, 3]
                }
            }

            """";
        string[] pieces = ["\"", "\"\"\"", "$", "$$", "{", "}", "{{", "}}", "'", "\\", "\n", " ", "\t", "@", "[", "]", "array<", ">", ",", "(", ")", ":", "..", "..<", "...", "^", "flags ", "struct ", "=", "|"];
        var random = new Random(20261006);
        for (var round = 0; round < 2000; round++)
        {
            var text = original;
            for (var edit = 0; edit < 1 + (round % 5); edit++)
            {
                var index = random.Next(text.Length);
                text = random.Next(3) switch
                {
                    0 => text.Insert(index, pieces[random.Next(pieces.Length)]),
                    1 => text.Remove(index, Math.Min(random.Next(1, 4), text.Length - index)),
                    _ => text.Insert(index, text.Substring(random.Next(text.Length - 8), random.Next(1, 8))),
                };
            }
            var tree = SyntaxTree.Parse(text);
            Assert.Equal(text, tree.Root.ToFullString());
            AssertSeparatedListsAlternate(tree.Root);
        }
    }
}
