using SelectTranslate.Core.Input;

namespace SelectTranslate.Core.Tests;

public sealed class HotkeyParserTests
{
    [Theory]
    [InlineData("Ctrl+Alt+T", true, true, false, 0x54)]
    [InlineData("ctrl+shift+q", true, false, true, 0x51)]
    [InlineData("Alt+Space", false, true, false, 0x20)]
    [InlineData("Win+F9", false, false, false, 0x78)]
    public void TryParse_AcceptsModifierCombos(string text, bool ctrl, bool alt, bool shift, int vk)
    {
        Assert.True(HotkeyParser.TryParse(text, out var hotkey));
        Assert.Equal(ctrl, hotkey.Ctrl);
        Assert.Equal(alt, hotkey.Alt);
        Assert.Equal(shift, hotkey.Shift);
        Assert.Equal(vk, hotkey.VirtualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("T")]              // 没有修饰键
    [InlineData("Ctrl+Alt")]       // 没有主键
    [InlineData("Ctrl+")]          // 残缺
    [InlineData("Ctrl+Alt+Unknown")]
    public void TryParse_RejectsInvalid(string text)
    {
        Assert.False(HotkeyParser.TryParse(text, out _));
    }

    [Fact]
    public void Matches_RequiresExactModifiers()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+Alt+T", out var hotkey));

        Assert.True(HotkeyParser.Matches(hotkey, 0x54, ctrl: true, alt: true, shift: false, win: false));
        Assert.False(HotkeyParser.Matches(hotkey, 0x54, ctrl: true, alt: true, shift: true, win: false));
        Assert.False(HotkeyParser.Matches(hotkey, 0x54, ctrl: true, alt: false, shift: false, win: false));
        Assert.False(HotkeyParser.Matches(hotkey, 0x51, ctrl: true, alt: true, shift: false, win: false));
    }

    [Fact]
    public void Format_RoundTrips()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+Alt+T", out var hotkey));
        Assert.Equal("Ctrl+Alt+T", hotkey.ToString());

        Assert.True(HotkeyParser.TryParse(hotkey.ToString(), out var again));
        Assert.Equal(hotkey, again);
    }

    [Theory]
    [InlineData("F1", 0x70)]
    [InlineData("F12", 0x7B)]
    [InlineData("Space", 0x20)]
    [InlineData("Esc", 0x1B)]
    public void GetKeyName_MapsCommonKeys(string expected, int vk)
    {
        Assert.Equal(expected, HotkeyParser.GetKeyName(vk));
    }
}