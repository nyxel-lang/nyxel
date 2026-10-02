using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Nyxel.Compiler.Tests;

/// <summary>
/// Pins down the back-end assumptions of ADR-0004: generated C# carrying enhanced <c>#line</c> span directives,
/// compiled in-process by Roslyn, yields a portable PDB, stack traces and diagnostics that point at the .nyxel source.
/// The generated C# is hand-written here; once lowering exists these tests keep guarding the Roslyn / runtime side.
/// </summary>
public class RoslynLineMappingTests
{
    private const string NyxelPath = "Slime.nyxel";

    /// <summary>Path of the generated C# file; only hidden (unmapped) code is attributed to it.</summary>
    private const string GeneratedPath = "Slime.nyxel.g.cs";

    // The fake source file. The syntax only imitates ADR-0005 / ADR-0006 style; what matters is line and column positions.
    private const string NyxelSource = """
        class Slime {
            var hp: int = 10

            public func Split(parts: int) -> int {
                let share = hp / parts
                hp = hp - share
                return share
            }
        }

        """;

    // Hand-written imitation of what lowering Slime.nyxel would produce.
    //
    // Enhanced #line directive (C# 10+): #line (startLine, startChar) - (endLine, endChar) charOffset "file"
    // - Lines and characters are 1-based and the end character is INCLUSIVE (Roslyn adds 1 internally):
    //   `let share = hp / parts` occupies columns 9..30, so it is written (5, 9) - (5, 30).
    // - charOffset is the length of a generated prefix on the single line after the directive. A sequence point or
    //   diagnostic that starts inside the prefix maps to the whole directive span; text after the prefix maps column
    //   by column starting at startChar; later lines keep their generated column. Without charOffset the prefix is
    //   empty, so the generated indentation gets added to every mapped column.
    // - Rule used here: one mapped statement per line, charOffset = the 1-based column of its first character. The
    //   statement then starts inside the "prefix" and its sequence point is exactly the Nyxel statement's span.
    // - #line hidden marks scaffolding without a Nyxel counterpart: its sequence points are hidden (line 0xFEEFEE),
    //   so debuggers step over it. The ThrowIfNegative call stands in for such compiler-inserted code.
    private const string GeneratedCSharp = """
        #line hidden
        namespace Nyxel.Generated
        {
            public class Slime
            {
        #line (2, 5) - (2, 20) 9 "Slime.nyxel"
                private int hp = 10;
        #line hidden

                public int Split(int parts)
        #line (4, 42) - (4, 42) 9 "Slime.nyxel"
                {
        #line hidden
                    global::System.ArgumentOutOfRangeException.ThrowIfNegative(parts);
        #line (5, 9) - (5, 30) 13 "Slime.nyxel"
                    int share = hp / parts;
        #line (6, 9) - (6, 23) 13 "Slime.nyxel"
                    hp = hp - share;
        #line (7, 9) - (7, 20) 13 "Slime.nyxel"
                    return share;
        #line (8, 5) - (8, 5) 9 "Slime.nyxel"
                }
        #line hidden
            }
        }
        """;

    // Well-known GUIDs from the Portable PDB specification.
    private static readonly Guid Sha256HashAlgorithm = new("8829d00f-11b8-4213-878b-770e8597ac16");
    private static readonly Guid EmbeddedSourceKind = new("0e8a571b-6926-466e-b4ad-8ab04611f5fe");

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    // Compile against exactly the framework the tests run on. Every assembly the host resolves by default is listed here.
    private static readonly ImmutableArray<MetadataReference> References =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path)),
    ];

    [Fact]
    public void GeneratedCSharpCompilesToDllAndPortablePdb()
    {
        var compilation = Compile(GeneratedCSharp);

        Assert.DoesNotContain(compilation.GetDiagnostics(), d => d.Severity >= DiagnosticSeverity.Warning);
        var (pe, _) = EmitWithPdb(compilation);
        using var peReader = new PEReader(new MemoryStream(pe));
        // The CodeView debug directory entry is how debuggers find the matching PDB; "portable" means the new format.
        Assert.Contains(peReader.ReadDebugDirectory(), e => e.Type == DebugDirectoryEntryType.CodeView && e.IsPortableCodeView);
    }

    [Theory]
    [InlineData(2, 5, 20, 9, "var hp: int = 10")]
    [InlineData(4, 42, 42, 9, "{")]
    [InlineData(5, 9, 30, 13, "let share = hp / parts")]
    [InlineData(6, 9, 23, 13, "hp = hp - share")]
    [InlineData(7, 9, 20, 13, "return share")]
    [InlineData(8, 5, 5, 9, "}")]
    public void DirectivesFollowTheMappingRule(int line, int startChar, int endChar, int charOffset, string nyxelText)
    {
        // The span is 1-based with an inclusive end.
        var nyxelLine = NyxelSource.Split('\n')[line - 1].TrimEnd('\r');
        Assert.Equal(nyxelText, nyxelLine[(startChar - 1)..endChar]);

        // charOffset is the 1-based column of the first character of the generated line after the directive.
        var generatedLines = GeneratedCSharp.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var directive = $"#line ({line}, {startChar}) - ({line}, {endChar}) {charOffset} \"{NyxelPath}\"";
        var index = Array.IndexOf(generatedLines, directive);
        Assert.True(index >= 0, $"Missing directive: {directive}");
        var mappedLine = generatedLines[index + 1];
        Assert.Equal(charOffset, mappedLine.Length - mappedLine.TrimStart().Length + 1);
    }

    [Fact]
    public void SequencePointsMapToNyxelSpans()
    {
        var (pe, pdb) = EmitWithPdb(Compile(GeneratedCSharp));

        using var peReader = new PEReader(new MemoryStream(pe));
        using var pdbProvider = MetadataReaderProvider.FromPortablePdbStream(new MemoryStream(pdb));
        var assembly = peReader.GetMetadataReader();
        var symbols = pdbProvider.GetMetadataReader();

        // The Document table lists both files: Slime.nyxel for mapped code, the generated file for hidden code.
        // The path is stored exactly as written in the directive.
        Assert.Contains(NyxelPath, ReadDocumentNames(symbols));
        Assert.Contains(GeneratedPath, ReadDocumentNames(symbols));

        // PDB sequence points are 1-based with an EXCLUSIVE end column, so directive (5, 9) - (5, 30) becomes (5,9)-(5,31).
        string[] expected =
        [
            "Slime.nyxel (4,42)-(4,43)",
            "hidden",
            "Slime.nyxel (5,9)-(5,31)",
            "Slime.nyxel (6,9)-(6,24)",
            "Slime.nyxel (7,9)-(7,21)",
            "Slime.nyxel (8,5)-(8,6)",
        ];
        Assert.Equal(expected, DescribeSequencePoints(symbols, FindMethod(assembly, "Slime", "Split")));

        // A field initializer runs in the constructor but keeps the field's mapping.
        string[] expectedInConstructor = ["Slime.nyxel (2,5)-(2,21)"];
        var constructorPoints = DescribeSequencePoints(symbols, FindMethod(assembly, "Slime", ".ctor"));
        Assert.Equal(expectedInConstructor, constructorPoints.Where(p => p != "hidden"));

        // Without #pragma checksum or embedded source, Roslyn cannot hash a file it never read: no checksum.
        Assert.Empty(symbols.GetBlobBytes(symbols.GetDocument(FindDocument(symbols, NyxelPath)).Hash));
    }

    [Fact]
    public void ExceptionStackTraceShowsNyxelLine()
    {
        var (pe, pdb) = EmitWithPdb(Compile(GeneratedCSharp));

        var site = CallSplitAndCatch(pe, pdb, parts: 0);

        Assert.Equal(typeof(DivideByZeroException), site.ExceptionType);
        Assert.Contains($" in {NyxelPath}:line 5", site.StackTrace);
        Assert.Equal<(string?, int, int)>((NyxelPath, 5, 9), (site.FileName, site.Line, site.Column));
    }

    [Fact]
    public void ExceptionInHiddenScaffoldingIsAttributedToPrecedingNyxelLine()
    {
        var (pe, pdb) = EmitWithPdb(Compile(GeneratedCSharp));

        // ThrowIfNegative runs under #line hidden. The runtime skips hidden sequence points and reports the closest
        // visible one before the throwing IL offset: here the method's opening brace on line 4.
        var site = CallSplitAndCatch(pe, pdb, parts: -1);

        Assert.Equal(typeof(ArgumentOutOfRangeException), site.ExceptionType);
        Assert.Equal<(string?, int, int)>((NyxelPath, 4, 42), (site.FileName, site.Line, site.Column));
    }

    [Fact]
    public void RoslynDiagnosticMapsToNyxelLine()
    {
        // A type error the Nyxel front end would normally have caught (int minus string), in the middle of line 6.
        var error = SingleError(ReplaceOnce(GeneratedCSharp, "hp = hp - share;", "hp = hp - \"share\";"));

        Assert.Equal("CS0019", error.Id);
        // GetMappedLineSpan applies #line; GetLineSpan would report the generated file. Positions are 0-based.
        var mapped = error.Location.GetMappedLineSpan();
        Assert.True(mapped.HasMappedPath);
        Assert.Equal(NyxelPath, mapped.Path);
        Assert.Equal(6 - 1, mapped.StartLinePosition.Line);
        // The error starts after the "prefix", so its column is only shifted by its offset in the generated text.
        // Generated C# and Nyxel text do not line up character by character: the column is approximate, the line exact.
        Assert.InRange(mapped.StartLinePosition.Character + 1, 9, 23);
    }

    [Fact]
    public void DiagnosticAtStatementStartCoversWholeNyxelStatement()
    {
        // An unknown name at the very start of the statement: it starts inside the prefix, so it maps to the whole span.
        var error = SingleError(ReplaceOnce(GeneratedCSharp, "hp = hp - share;", "hq = hp - share;"));

        Assert.Equal("CS0103", error.Id);
        var mapped = error.Location.GetMappedLineSpan();
        Assert.Equal(NyxelPath, mapped.Path);
        // 0-based and end-exclusive: Nyxel columns 9..23 on line 6.
        Assert.Equal(new LinePositionSpan(new LinePosition(5, 8), new LinePosition(5, 23)), mapped.Span);
    }

    [Fact]
    public void LineDirectivePathIsCopiedVerbatim()
    {
        // The #line file name is not an escaped string literal: backslashes are kept as they are, nothing is normalized
        // (CSharpCompilationOptions.SourceReferenceResolver is null by default; with a SourceFileResolver, relative paths
        // would be resolved against the generated file's directory instead).
        const string absolutePath = @"C:\game\scripts\Slime.nyxel";
        var generated = GeneratedCSharp.Replace($"\"{NyxelPath}\"", $"\"{absolutePath}\"");
        var (pe, pdb) = EmitWithPdb(Compile(generated));

        using (var pdbProvider = MetadataReaderProvider.FromPortablePdbStream(new MemoryStream(pdb)))
        {
            Assert.Contains(absolutePath, ReadDocumentNames(pdbProvider.GetMetadataReader()));
        }
        var site = CallSplitAndCatch(pe, pdb, parts: 0);
        Assert.Equal(absolutePath, site.FileName);
        Assert.Contains($" in {absolutePath}:line 5", site.StackTrace);
    }

    [Fact]
    public void NyxelSourceCanBeEmbeddedInPdb()
    {
        // Embed the file's exact bytes under the same path string the #line directives use, otherwise Roslyn adds a
        // second document. Raw bytes also keep the checksum equal to the file on disk (a SourceText built with
        // Encoding.UTF8 would hash a BOM the file does not have).
        var nyxelBytes = Utf8NoBom.GetBytes(NyxelSource);
        var embedded = EmbeddedText.FromBytes(NyxelPath, new ArraySegment<byte>(nyxelBytes), SourceHashAlgorithm.Sha256);
        var (_, pdb) = EmitWithPdb(Compile(GeneratedCSharp), [embedded]);

        using var pdbProvider = MetadataReaderProvider.FromPortablePdbStream(new MemoryStream(pdb));
        var symbols = pdbProvider.GetMetadataReader();
        var documentHandle = FindDocument(symbols, NyxelPath);
        Assert.Equal(NyxelSource, Utf8NoBom.GetString(ReadEmbeddedSource(symbols, documentHandle)));

        // Embedding also gives the document a checksum, which debuggers compare with the file they open.
        var document = symbols.GetDocument(documentHandle);
        Assert.Equal(Sha256HashAlgorithm, symbols.GetGuid(document.HashAlgorithm));
        Assert.Equal(SHA256.HashData(nyxelBytes), symbols.GetBlobBytes(document.Hash));
    }

    [Fact]
    public void PragmaChecksumRecordsNyxelFileHash()
    {
        // The alternative to embedding when only the checksum is wanted (Razor does this).
        var hash = SHA256.HashData(Utf8NoBom.GetBytes(NyxelSource));
        var pragma = $"#pragma checksum \"{NyxelPath}\" \"{Sha256HashAlgorithm:B}\" \"{Convert.ToHexStringLower(hash)}\"\n";
        var (_, pdb) = EmitWithPdb(Compile(pragma + GeneratedCSharp));

        using var pdbProvider = MetadataReaderProvider.FromPortablePdbStream(new MemoryStream(pdb));
        var symbols = pdbProvider.GetMetadataReader();
        var document = symbols.GetDocument(FindDocument(symbols, NyxelPath));
        Assert.Equal(Sha256HashAlgorithm, symbols.GetGuid(document.HashAlgorithm));
        Assert.Equal(hash, symbols.GetBlobBytes(document.Hash));
    }

    private static CSharpCompilation Compile(string generatedCSharp)
    {
        var tree = CSharpSyntaxTree.ParseText(
            generatedCSharp,
            new CSharpParseOptions(LanguageVersion.CSharp14),
            path: GeneratedPath,
            encoding: Encoding.UTF8);
        return CSharpCompilation.Create(
            assemblyName: "Slime",
            syntaxTrees: [tree],
            references: References,
            // Debug: no IL optimizations, so every statement keeps its own sequence point.
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Debug));
    }

    private static (byte[] Pe, byte[] Pdb) EmitWithPdb(CSharpCompilation compilation, IEnumerable<EmbeddedText>? embeddedTexts = null)
    {
        using var pe = new MemoryStream();
        using var pdb = new MemoryStream();
        var result = compilation.Emit(
            pe,
            pdb,
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb),
            embeddedTexts: embeddedTexts);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return (pe.ToArray(), pdb.ToArray());
    }

    private static Diagnostic SingleError(string generatedCSharp) =>
        Assert.Single(Compile(generatedCSharp).GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);

    private static string ReplaceOnce(string text, string oldValue, string newValue)
    {
        Assert.Contains(oldValue, text);
        return text.Replace(oldValue, newValue);
    }

    private static List<string> ReadDocumentNames(MetadataReader symbols) =>
        [.. symbols.Documents.Select(h => symbols.GetString(symbols.GetDocument(h).Name))];

    private static DocumentHandle FindDocument(MetadataReader symbols, string path) =>
        symbols.Documents.Single(h => symbols.GetString(symbols.GetDocument(h).Name) == path);

    /// <summary>Finds a method in the assembly's metadata. Its handle also indexes the PDB's MethodDebugInformation table.</summary>
    private static MethodDefinitionHandle FindMethod(MetadataReader assembly, string typeName, string methodName)
    {
        foreach (var typeHandle in assembly.TypeDefinitions)
        {
            var type = assembly.GetTypeDefinition(typeHandle);
            if (!assembly.StringComparer.Equals(type.Name, typeName))
            {
                continue;
            }
            foreach (var methodHandle in type.GetMethods())
            {
                if (assembly.StringComparer.Equals(assembly.GetMethodDefinition(methodHandle).Name, methodName))
                {
                    return methodHandle;
                }
            }
        }
        throw new InvalidOperationException($"Method {typeName}.{methodName} not found.");
    }

    private static List<string> DescribeSequencePoints(MetadataReader symbols, MethodDefinitionHandle method)
    {
        var debugInfo = symbols.GetMethodDebugInformation(method.ToDebugInformationHandle());
        return
        [
            .. debugInfo.GetSequencePoints().Select(p => p.IsHidden
                ? "hidden"
                : $"{symbols.GetString(symbols.GetDocument(p.Document).Name)} ({p.StartLine},{p.StartColumn})-({p.EndLine},{p.EndColumn})"),
        ];
    }

    /// <summary>Reads the embedded-source custom debug information attached to a PDB document.</summary>
    private static byte[] ReadEmbeddedSource(MetadataReader symbols, DocumentHandle document)
    {
        foreach (var handle in symbols.GetCustomDebugInformation(document))
        {
            var info = symbols.GetCustomDebugInformation(handle);
            if (symbols.GetGuid(info.Kind) != EmbeddedSourceKind)
            {
                continue;
            }
            // Blob layout (Portable PDB spec): int32 format, then the content. Format 0 means the bytes are stored
            // as is; a positive value is the uncompressed size of deflate-compressed bytes (Roslyn compresses large files).
            var blob = symbols.GetBlobReader(info.Value);
            var format = blob.ReadInt32();
            var content = blob.ReadBytes(blob.RemainingBytes);
            if (format == 0)
            {
                return content;
            }
            using var inflated = new MemoryStream(format);
            using (var deflate = new DeflateStream(new MemoryStream(content), CompressionMode.Decompress))
            {
                deflate.CopyTo(inflated);
            }
            return inflated.ToArray();
        }
        throw new InvalidOperationException("The document has no embedded source.");
    }

    private sealed record ThrowSite(Type ExceptionType, string StackTrace, string? FileName, int Line, int Column);

    /// <summary>
    /// Loads the assembly and its PDB into a collectible AssemblyLoadContext (what hot reload in Nyxel.Host will do),
    /// calls <c>Slime.Split(parts)</c> and reports where the runtime says the exception was thrown.
    /// </summary>
    private static ThrowSite CallSplitAndCatch(byte[] pe, byte[] pdb, int parts)
    {
        var context = new AssemblyLoadContext("nyxel-line-mapping-test", isCollectible: true);
        try
        {
            // Handing the PDB to the loader lets stack traces resolve source lines without any .pdb file on disk.
            var assembly = context.LoadFromStream(new MemoryStream(pe), new MemoryStream(pdb));
            var slimeType = assembly.GetType("Nyxel.Generated.Slime", throwOnError: true)!;
            var slime = Activator.CreateInstance(slimeType)!;
            var split = slimeType.GetMethod("Split")!;

            // DoNotWrapExceptions: get the script's exception itself instead of a TargetInvocationException.
            var exception = Assert.ThrowsAny<Exception>(
                () => split.Invoke(slime, BindingFlags.DoNotWrapExceptions, binder: null, [parts], culture: null));

            // Read everything before unloading: the frames refer to methods of the collectible assembly.
            var frame = new StackTrace(exception, fNeedFileInfo: true).GetFrames().First(f => f.GetMethod()?.Name == "Split");
            return new ThrowSite(
                exception.GetType(),
                exception.StackTrace ?? "",
                frame.GetFileName(),
                frame.GetFileLineNumber(),
                frame.GetFileColumnNumber());
        }
        finally
        {
            context.Unload();
        }
    }
}
