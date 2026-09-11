using FlauiCli.Core.Native;

namespace FlauiCli.Core.Engine;

/// <summary>Parses key strings (for example <c>Enter</c>, <c>Ctrl+Shift+S</c>, <c>Alt+F4</c>) into Win32 virtual-key codes.</summary>
public static class KeyParser
{
    private static readonly Dictionary<string, ushort> Named = BuildNamed();

    // When a key code has several aliases, the first one (the canonical name) is used for output
    private static readonly Dictionary<ushort, string> Names = Named
        .GroupBy(kv => kv.Value)
        .ToDictionary(g => g.Key, g => g.First().Key);

    private static Dictionary<string, ushort> BuildNamed()
    {
        var d = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ctrl"] = 0x11, ["Control"] = 0x11,
            ["Shift"] = 0x10,
            ["Alt"] = 0x12,
            ["Win"] = 0x5B, ["Meta"] = 0x5B, ["Cmd"] = 0x5B,
            ["Enter"] = 0x0D, ["Return"] = 0x0D,
            ["Tab"] = 0x09,
            ["Escape"] = 0x1B, ["Esc"] = 0x1B,
            ["Space"] = 0x20,
            ["Backspace"] = 0x08,
            ["Delete"] = 0x2E, ["Del"] = 0x2E,
            ["Insert"] = 0x2D, ["Ins"] = 0x2D,
            ["Home"] = 0x24, ["End"] = 0x23,
            ["PageUp"] = 0x21, ["PgUp"] = 0x21,
            ["PageDown"] = 0x22, ["PgDn"] = 0x22,
            ["Up"] = 0x26, ["ArrowUp"] = 0x26,
            ["Down"] = 0x28, ["ArrowDown"] = 0x28,
            ["Left"] = 0x25, ["ArrowLeft"] = 0x25,
            ["Right"] = 0x27, ["ArrowRight"] = 0x27,
            ["CapsLock"] = 0x14,
            ["PrintScreen"] = 0x2C,
            ["Apps"] = 0x5D, ["ContextMenu"] = 0x5D,
            ["Multiply"] = 0x6A, ["Add"] = 0x6B, ["Subtract"] = 0x6D, ["Decimal"] = 0x6E, ["Divide"] = 0x6F,
            ["Plus"] = 0xBB, ["Minus"] = 0xBD, ["Comma"] = 0xBC, ["Period"] = 0xBE,
        };
        for (var i = 1; i <= 24; i++) d["F" + i] = (ushort)(0x70 + i - 1);
        for (var i = 0; i <= 9; i++) d["Numpad" + i] = (ushort)(0x60 + i);
        return d;
    }

    public static bool IsModifier(ushort vk) =>
        vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;

    public static ushort[] Parse(string keys)
    {
        if (string.IsNullOrWhiteSpace(keys)) throw new CliException("Keys must not be empty");
        // "Ctrl++" means Ctrl plus the + key
        var parts = new List<string>();
        var tokens = keys.Trim().Split('+');
        for (var i = 0; i < tokens.Length; i++)
        {
            var t = tokens[i].Trim();
            if (t.Length == 0 && i < tokens.Length - 1) { parts.Add("+"); i++; continue; }
            if (t.Length > 0) parts.Add(t);
        }
        if (parts.Count == 0) throw new CliException($"Unrecognized key: {keys}");
        return [.. parts.Select(ToVk)];
    }

    private static ushort ToVk(string token)
    {
        if (Named.TryGetValue(token, out var vk)) return vk;
        if (token.Length == 1)
        {
            var ch = token[0];
            if (ch is >= 'a' and <= 'z') return char.ToUpperInvariant(ch);
            if (ch is >= 'A' and <= 'Z' or >= '0' and <= '9') return ch;
            var scan = NativeMethods.VkKeyScanW(ch);
            if (scan != -1) return (ushort)(scan & 0xFF);
        }
        throw new CliException($"Unrecognized key: {token}");
    }

    /// <summary>Formats virtual-key codes as a key string (used by the recorder).</summary>
    public static string Format(IEnumerable<ushort> vks) => string.Join("+", vks.Select(Format));

    public static string Format(ushort vk)
    {
        if (vk is >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39) return ((char)vk).ToString();
        return vk switch
        {
            0xA0 or 0xA1 => "Shift",
            0xA2 or 0xA3 => "Ctrl",
            0xA4 or 0xA5 => "Alt",
            0x5C => "Win",
            _ => Names.TryGetValue(vk, out var n) ? n : $"0x{vk:X2}",
        };
    }
}
