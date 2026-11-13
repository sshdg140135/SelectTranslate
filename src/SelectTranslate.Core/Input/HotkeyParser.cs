namespace SelectTranslate.Core.Input;

/// <summary>解析后的触发热键（键盘组合）。</summary>
public sealed record ParsedHotkey(bool Ctrl, bool Alt, bool Shift, bool Win, int VirtualKey)
{
    public override string ToString() => HotkeyParser.Format(VirtualKey, Ctrl, Alt, Shift, Win);
}

/// <summary>
/// 热键文本解析与匹配，例如 "Ctrl+Alt+T"、"alt+shift+space"、"F9"。
/// 纯函数，便于单测；实际按下判断由低级键盘钩子完成。
/// </summary>
public static class HotkeyParser
{
    private static readonly Dictionary<string, int> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["space"] = 0x20,
        ["spacebar"] = 0x20,
        ["enter"] = 0x0D,
        ["return"] = 0x0D,
        ["tab"] = 0x09,
        ["esc"] = 0x1B,
        ["escape"] = 0x1B,
        ["left"] = 0x25,
        ["up"] = 0x26,
        ["right"] = 0x27,
        ["down"] = 0x28,
        ["insert"] = 0x2D,
        ["delete"] = 0x2E,
        ["home"] = 0x24,
        ["end"] = 0x23,
        ["pageup"] = 0x21,
        ["pagedown"] = 0x22,
    };

    public static bool TryParse(string? text, out ParsedHotkey hotkey)
    {
        hotkey = new ParsedHotkey(false, false, false, false, 0);

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var ctrl = false;
        var alt = false;
        var shift = false;
        var win = false;
        var key = 0;

        foreach (var raw in parts)
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    ctrl = true;
                    continue;
                case "alt":
                    alt = true;
                    continue;
                case "shift":
                    shift = true;
                    continue;
                case "win":
                case "meta":
                    win = true;
                    continue;
            }

            if (key != 0 || !TryParseKey(raw, out key))
            {
                return false;
            }
        }

        if (key == 0)
        {
            return false;
        }

        // 至少一个修饰键，避免单个字母劫持正常输入。
        if (!ctrl && !alt && !shift && !win)
        {
            return false;
        }

        hotkey = new ParsedHotkey(ctrl, alt, shift, win, key);
        return true;
    }

    public static bool Matches(ParsedHotkey hotkey, int virtualKey, bool ctrl, bool alt, bool shift, bool win)
        => hotkey.VirtualKey == virtualKey
            && hotkey.Ctrl == ctrl
            && hotkey.Alt == alt
            && hotkey.Shift == shift
            && hotkey.Win == win;

    public static string Format(int virtualKey, bool ctrl, bool alt, bool shift, bool win)
    {
        var parts = new List<string>(5);
        if (ctrl)
        {
            parts.Add("Ctrl");
        }

        if (alt)
        {
            parts.Add("Alt");
        }

        if (shift)
        {
            parts.Add("Shift");
        }

        if (win)
        {
            parts.Add("Win");
        }

        parts.Add(GetKeyName(virtualKey));
        return string.Join('+', parts);
    }

    public static string GetKeyName(int virtualKey)
    {
        if (virtualKey is >= 0x41 and <= 0x5A)
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= 0x30 and <= 0x39)
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= 0x70 and <= 0x87)
        {
            return $"F{virtualKey - 0x70 + 1}";
        }

        foreach (var pair in NamedKeys)
        {
            if (pair.Value == virtualKey && pair.Key.Length > 3)
            {
                return pair.Key switch
                {
                    "spacebar" => "Space",
                    "return" => "Enter",
                    "escape" => "Esc",
                    "pageup" => "PageUp",
                    "pagedown" => "PageDown",
                    _ => char.ToUpperInvariant(pair.Key[0]) + pair.Key[1..],
                };
            }
        }

        return $"VK_{virtualKey:X2}";
    }

    private static bool TryParseKey(string raw, out int virtualKey)
    {
        virtualKey = 0;

        if (NamedKeys.TryGetValue(raw, out var named))
        {
            virtualKey = named;
            return true;
        }

        if (raw.Length == 1)
        {
            var ch = char.ToUpperInvariant(raw[0]);
            if (ch is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                virtualKey = ch;
                return true;
            }
        }

        if (raw.Length is 2 or 3 && (raw[0] is 'f' or 'F'))
        {
            if (int.TryParse(raw[1..], out var number) && number is >= 1 and <= 24)
            {
                virtualKey = 0x70 + number - 1;
                return true;
            }
        }

        return false;
    }
}