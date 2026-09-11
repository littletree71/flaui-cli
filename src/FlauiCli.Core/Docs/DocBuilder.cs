using System.Drawing;
using FlauiCli.Core.Imaging;

namespace FlauiCli.Core.Docs;

/// <summary>One step of a document.</summary>
public sealed record DocStep(int Number, string Text, string? ImagePath, string? Command, DateTime Time);

/// <summary>
/// Collects steps (description + annotated screenshot) and exports them as Markdown (with images/)
/// and a single-file HTML page.
/// </summary>
public sealed class DocBuilder
{
    public const string DefaultTitle = "Instructions";

    private readonly List<DocStep> _steps = [];

    public DocBuilder(string title, string workDir)
    {
        Title = string.IsNullOrWhiteSpace(title) ? DefaultTitle : title;
        WorkDir = workDir;
        Directory.CreateDirectory(workDir);
    }

    public string Title { get; }

    public string WorkDir { get; }

    public DateTime CreatedAt { get; } = DateTime.Now;

    public IReadOnlyList<DocStep> Steps => _steps;

    public int NextNumber => _steps.Count + 1;

    public DocStep AddStep(string text, Bitmap? image, string? command)
    {
        var number = NextNumber;
        string? imagePath = null;
        if (image is not null)
        {
            imagePath = Path.Combine(WorkDir, $"step-{number:D2}.png");
            Screenshotter.SavePng(image, imagePath);
        }
        var step = new DocStep(number, text, imagePath, command, DateTime.Now);
        _steps.Add(step);
        return step;
    }

    /// <summary>Exports the document and returns the paths of the generated files.</summary>
    public IReadOnlyList<string> Export(string outDir, IEnumerable<string> formats)
    {
        Directory.CreateDirectory(outDir);
        var files = new List<string>();
        var fmts = formats.Select(f => f.Trim().ToLowerInvariant()).Where(f => f.Length > 0).ToHashSet();
        if (fmts.Count == 0) fmts = ["md", "html"];

        foreach (var f in fmts.Where(f => f is not ("md" or "markdown" or "html")))
            throw new CliException($"Unsupported document format: {f} (use md or html)");

        if (fmts.Contains("md") || fmts.Contains("markdown"))
        {
            var imagesDir = Path.Combine(outDir, "images");
            Directory.CreateDirectory(imagesDir);
            foreach (var s in _steps.Where(s => s.ImagePath is not null))
                File.Copy(s.ImagePath!, Path.Combine(imagesDir, Path.GetFileName(s.ImagePath!)), overwrite: true);

            var md = Path.Combine(outDir, "index.md");
            File.WriteAllText(md, MarkdownRenderer.Render(this, s => "images/" + Path.GetFileName(s.ImagePath!)));
            files.Add(md);
        }

        if (fmts.Contains("html"))
        {
            var html = Path.Combine(outDir, "index.html");
            File.WriteAllText(html, HtmlRenderer.Render(this));
            files.Add(html);
        }

        return files;
    }
}
