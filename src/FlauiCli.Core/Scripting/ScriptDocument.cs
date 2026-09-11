using FlauiCli.Core.Protocol;

namespace FlauiCli.Core.Scripting;

/// <summary>YAML 測試腳本。</summary>
public sealed class ScriptDocument
{
    public string? Name { get; set; }

    public AppSpec? App { get; set; }

    /// <summary>此腳本所有步驟的預設逾時（毫秒）。</summary>
    public int? Timeout { get; set; }

    public List<CommandCall> Steps { get; set; } = [];

    /// <summary>載入來源檔案路徑。</summary>
    public string? SourcePath { get; set; }

    public string DisplayName =>
        Name ?? (SourcePath is null ? "未命名腳本" : Path.GetFileNameWithoutExtension(SourcePath));
}

/// <summary>腳本開始前要開啟 / 附加的應用程式。</summary>
public sealed class AppSpec
{
    public string? Launch { get; set; }

    public string? Args { get; set; }

    public string? Window { get; set; }

    /// <summary>附加到執行中的程序（PID 或名稱）。</summary>
    public string? Attach { get; set; }

    /// <summary>腳本結束後是否關閉（預設：launch 為 true、attach 為 false）。</summary>
    public bool? Close { get; set; }
}
