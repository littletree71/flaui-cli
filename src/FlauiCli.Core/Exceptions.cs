namespace FlauiCli.Core;

/// <summary>可預期的使用錯誤（參數錯誤、找不到元素等），訊息會直接顯示給使用者。</summary>
public class CliException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>找不到目標元素。</summary>
public sealed class ElementNotFoundException(string target, string? detail = null)
    : CliException($"找不到元素：{target}" + (detail is null ? "" : $"（{detail}）"));

/// <summary>斷言失敗（結束碼 1）。</summary>
public sealed class AssertionFailedException(string message) : CliException(message);
