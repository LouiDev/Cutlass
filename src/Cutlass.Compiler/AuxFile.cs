namespace Cutlass.Compiler;

/// <summary>
/// A small text file a graph refers to by bare name, for example the <c>t_&lt;sha8&gt;.txt</c> text of a title. The
/// caller writes it into the work directory (UTF-8 without BOM, content exactly as given) before running ffmpeg.
/// </summary>
/// <param name="FileName">The bare file name (<c>^[A-Za-z0-9_.-]+$</c>).</param>
/// <param name="Utf8Content">The file content.</param>
public sealed record AuxFile(string FileName, string Utf8Content);
