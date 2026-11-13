using SelectTranslate.Core.Placement;

namespace SelectTranslate.Core.Tests;

public sealed class CardPlacementTests
{
    private static readonly WorkArea MainScreen = new(0, 0, 1920, 1040);

    [Fact]
    public void Compute_PlacesCardBelowRightOfCursor()
    {
        var (x, y) = CardPlacement.Compute(400, 300, 320, 140, MainScreen);

        Assert.Equal(416, x);
        Assert.Equal(316, y);
    }

    [Fact]
    public void Compute_FlipsLeft_WhenNearRightEdge()
    {
        var (x, _) = CardPlacement.Compute(1900, 300, 320, 140, MainScreen);

        Assert.Equal(1900 - 320 - 16, x);
    }

    [Fact]
    public void Compute_FlipsUp_WhenNearBottomEdge()
    {
        var (_, y) = CardPlacement.Compute(400, 1020, 320, 140, MainScreen);

        Assert.Equal(1020 - 140 - 16, y);
    }

    [Fact]
    public void Compute_FlipsBoth_AtBottomRightCorner()
    {
        var (x, y) = CardPlacement.Compute(1919, 1039, 320, 140, MainScreen);

        Assert.Equal(1919 - 320 - 16, x);
        Assert.Equal(1039 - 140 - 16, y);
    }

    [Fact]
    public void Compute_ClampsIntoEdgeMargin_WhenFlippedCardWouldLeaveWorkArea()
    {
        // 光标贴左上角，翻转后左边距不足，应夹到 8px。
        var (x, y) = CardPlacement.Compute(0, 0, 320, 140, MainScreen);

        Assert.Equal(16, x);
        Assert.Equal(16, y);
    }

    [Fact]
    public void Compute_KeepsCardInsideSecondaryMonitor()
    {
        var secondary = new WorkArea(-1920, 0, 0, 1080);

        var (x, y) = CardPlacement.Compute(-1800, 500, 320, 140, secondary);

        Assert.Equal(-1784, x);
        Assert.Equal(516, y);
    }

    [Fact]
    public void Compute_ClampsToTopLeft_WhenCardBiggerThanWorkArea()
    {
        var small = new WorkArea(0, 0, 300, 120);

        var (x, y) = CardPlacement.Compute(290, 110, 320, 140, small);

        Assert.Equal(8, x);
        Assert.Equal(8, y);
    }
}