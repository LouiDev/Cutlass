using System.Runtime.CompilerServices;
using System.Text;
using Cutlass.Compiler;
using Cutlass.Compiler.Export;

namespace Cutlass.Tests.Compiler;

/// <summary>
/// Golden files under <c>tests/Cutlass.Tests/Compiler/Goldens/</c>: <c>&lt;case&gt;.ffg</c> (the graph text) and
/// <c>&lt;case&gt;.inputs.txt</c> (one input's arguments per line). Compared byte for byte; set
/// <c>CUTLASS_UPDATE_GOLDEN=1</c> to rewrite them, then review the diff.
/// </summary>
internal static class Golden
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>True when goldens should be rewritten instead of compared.</summary>
    public static bool Updating => Environment.GetEnvironmentVariable("CUTLASS_UPDATE_GOLDEN") == "1";

    /// <summary>The folder holding the golden files in the source tree.</summary>
    public static string Directory => Locate();

    /// <summary>Compares (or rewrites) the graph and the inputs of a render.</summary>
    public static void Verify(string name, CompiledRender render)
    {
        Match(name + ".ffg", render.GraphText);
        Match(name + ".inputs.txt", InputsText(render));
    }

    /// <summary>The inputs of a render as text: one input's arguments per line, separated by spaces.</summary>
    public static string InputsText(CompiledRender render) =>
        string.Join('\n', render.Inputs.Select(i => string.Join(' ', FfmpegArgsBuilder.Input(i))));

    /// <summary>Compares (or rewrites) one golden file.</summary>
    public static void Match(string fileName, string actual)
    {
        var path = Path.Combine(Directory, fileName);
        var actualBytes = Utf8NoBom.GetBytes(actual);
        if (Updating)
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllBytes(path, actualBytes);
            return;
        }

        Assert.True(File.Exists(path), $"Golden file {fileName} is missing. Run with CUTLASS_UPDATE_GOLDEN=1. Actual:\n{actual}");
        var expected = File.ReadAllBytes(path);
        if (!expected.AsSpan().SequenceEqual(actualBytes))
        {
            // Show the readable diff through Assert.Equal on the decoded text.
            Assert.Equal(Utf8NoBom.GetString(expected), actual);
            Assert.Fail($"Golden file {fileName} differs in bytes that decode identically.");
        }
    }

    /// <summary>The repository root (the folder holding <c>Cutlass.slnx</c>).</summary>
    public static string RepositoryRoot([CallerFilePath] string sourceFile = "")
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Path.GetDirectoryName(sourceFile) ?? string.Empty })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Cutlass.slnx")))
                {
                    return dir.FullName;
                }
            }
        }

        throw new InvalidOperationException("The repository root (Cutlass.slnx) was not found.");
    }

    private static string Locate() => Path.Combine(RepositoryRoot(), "tests", "Cutlass.Tests", "Compiler", "Goldens");
}
