using System.Text;

namespace FlauiCli.Core.Docs;

internal static class MarkdownRenderer
{
    public static string Render(DocBuilder doc, Func<DocStep, string> imageUrl)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {doc.Title}");
        sb.AppendLine();
        sb.AppendLine($"> 產生時間：{doc.CreatedAt:yyyy-MM-dd HH:mm} · 共 {doc.Steps.Count} 個步驟");
        sb.AppendLine();

        foreach (var s in doc.Steps)
        {
            sb.AppendLine($"## 步驟 {s.Number}：{EscapeHeading(s.Text)}");
            sb.AppendLine();
            if (s.ImagePath is not null)
            {
                sb.AppendLine($"![步驟 {s.Number}]({imageUrl(s)})");
                sb.AppendLine();
            }
            if (!string.IsNullOrEmpty(s.Command))
            {
                sb.AppendLine($"<sub>`{s.Command.Replace("`", "'")}`</sub>");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static string EscapeHeading(string s) => s.Replace("\r", "").Replace("\n", " ");
}
