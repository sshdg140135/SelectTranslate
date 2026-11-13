using System.Text;

namespace SelectTranslate.Core.Text;

/// <summary>
/// 选区文本清洗：去首尾空白与不可见字符、统一换行、合并多余空行，保留段落结构。
/// </summary>
public static class TextSanitizer
{
    private static readonly char[] InvisibleChars =
    [
        '\u200B', // 零宽空格
        '\u200C',
        '\u200D',
        '\uFEFF', // BOM
        '\u00AD', // 软连字符
    ];

    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var builder = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (Array.IndexOf(InvisibleChars, ch) >= 0)
            {
                continue;
            }

            if (ch == '\r')
            {
                continue;
            }

            // 保留换行与制表符，其余控制字符丢弃。
            if (char.IsControl(ch) && ch != '\n' && ch != '\t')
            {
                continue;
            }

            builder.Append(ch);
        }

        var lines = builder.ToString()
            .Split('\n')
            .Select(line => line.TrimEnd())
            .ToList();

        // 连续空行合并为一行。
        var compact = new List<string>(lines.Count);
        var previousBlank = false;
        foreach (var line in lines)
        {
            var isBlank = string.IsNullOrWhiteSpace(line);
            if (isBlank && previousBlank)
            {
                continue;
            }

            compact.Add(isBlank ? string.Empty : line);
            previousBlank = isBlank;
        }

        var result = string.Join('\n', compact).Trim();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }
}