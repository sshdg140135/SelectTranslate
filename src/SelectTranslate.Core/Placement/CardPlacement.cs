namespace SelectTranslate.Core.Placement;

/// <summary>屏幕工作区（物理像素）。</summary>
public readonly record struct WorkArea(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;
}

/// <summary>
/// 卡片定位：锚点右下偏移，越界翻转，最后夹进工作区（design §6）。
/// 纯函数，方便对多屏/边角场景做单测。
/// </summary>
public static class CardPlacement
{
    public const int Offset = 16;

    public const int EdgeMargin = 8;

    public static (int X, int Y) Compute(
        int anchorX,
        int anchorY,
        int cardWidth,
        int cardHeight,
        WorkArea workArea)
    {
        var x = anchorX + Offset;
        var y = anchorY + Offset;

        if (x + cardWidth > workArea.Right - EdgeMargin)
        {
            x = anchorX - cardWidth - Offset;
        }

        if (y + cardHeight > workArea.Bottom - EdgeMargin)
        {
            y = anchorY - cardHeight - Offset;
        }

        // 卡片比工作区还大时优先保证左上角可见。
        var maxX = Math.Max(workArea.Left + EdgeMargin, workArea.Right - cardWidth - EdgeMargin);
        var maxY = Math.Max(workArea.Top + EdgeMargin, workArea.Bottom - cardHeight - EdgeMargin);

        x = Math.Clamp(x, workArea.Left + EdgeMargin, maxX);
        y = Math.Clamp(y, workArea.Top + EdgeMargin, maxY);

        return (x, y);
    }
}