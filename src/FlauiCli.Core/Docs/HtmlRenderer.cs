using System.Net;
using System.Text;

namespace FlauiCli.Core.Docs;

/// <summary>單檔 HTML（圖片以 base64 內嵌），方便直接分享。</summary>
internal static class HtmlRenderer
{
    private const string Css = """
        :root { --bg:#f6f7f9; --card:#fff; --fg:#1f2328; --muted:#636c76; --accent:#e53935; --border:#d8dee4; }
        @media (prefers-color-scheme: dark) { :root { --bg:#0d1117; --card:#161b22; --fg:#e6edf3; --muted:#8d96a0; --border:#30363d; } }
        * { box-sizing: border-box; }
        body { margin:0; padding:32px 16px; background:var(--bg); color:var(--fg);
               font-family:"Segoe UI","Microsoft JhengHei",system-ui,sans-serif; line-height:1.6; }
        main { max-width: 960px; margin: 0 auto; }
        header h1 { margin:0 0 4px; font-size:28px; }
        header p { margin:0 0 24px; color:var(--muted); }
        nav { background:var(--card); border:1px solid var(--border); border-radius:10px; padding:12px 20px; margin-bottom:24px; }
        nav ol { margin:0; padding-left:20px; }
        nav a { color:inherit; text-decoration:none; } nav a:hover { color:var(--accent); }
        section { background:var(--card); border:1px solid var(--border); border-radius:10px; padding:20px; margin-bottom:20px; }
        section h2 { display:flex; gap:12px; align-items:center; margin:0 0 14px; font-size:18px; }
        .num { flex:none; width:30px; height:30px; border-radius:50%; background:var(--accent); color:#fff;
               display:inline-flex; align-items:center; justify-content:center; font-size:15px; }
        img { max-width:100%; height:auto; border:1px solid var(--border); border-radius:6px; display:block; }
        code { display:inline-block; margin-top:10px; color:var(--muted); font-size:12px; font-family:Consolas,monospace; overflow-wrap:anywhere; }
        """;

    public static string Render(DocBuilder doc)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html lang=\"zh-Hant\"><head><meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine($"<title>{H(doc.Title)}</title>");
        sb.AppendLine($"<style>{Css}</style></head><body><main>");
        sb.AppendLine($"<header><h1>{H(doc.Title)}</h1><p>產生時間：{doc.CreatedAt:yyyy-MM-dd HH:mm} · 共 {doc.Steps.Count} 個步驟</p></header>");

        if (doc.Steps.Count > 1)
        {
            sb.AppendLine("<nav><ol>");
            foreach (var s in doc.Steps) sb.AppendLine($"<li><a href=\"#step-{s.Number}\">{H(s.Text)}</a></li>");
            sb.AppendLine("</ol></nav>");
        }

        foreach (var s in doc.Steps)
        {
            sb.AppendLine($"<section id=\"step-{s.Number}\">");
            sb.AppendLine($"<h2><span class=\"num\">{s.Number}</span><span>{H(s.Text)}</span></h2>");
            if (s.ImagePath is not null && File.Exists(s.ImagePath))
            {
                var b64 = Convert.ToBase64String(File.ReadAllBytes(s.ImagePath));
                sb.AppendLine($"<img alt=\"步驟 {s.Number}\" src=\"data:image/png;base64,{b64}\">");
            }
            if (!string.IsNullOrEmpty(s.Command)) sb.AppendLine($"<code>{H(s.Command)}</code>");
            sb.AppendLine("</section>");
        }

        sb.AppendLine("</main></body></html>");
        return sb.ToString();
    }

    private static string H(string s) => WebUtility.HtmlEncode(s);
}
