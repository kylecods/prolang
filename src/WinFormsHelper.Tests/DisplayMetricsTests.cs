using System.Windows.Forms;

using Helper = WinFormsHelper.WinFormsHelper;

namespace WinFormsHelperTests;

/// <summary>
/// Covers the display metrics the editor sizes itself from.
/// </summary>
/// <remarks>
/// <para>
/// These cannot assert a particular scaling factor or screen size: the values are whatever the
/// machine running the tests is configured for, and a test that expected 100% would fail on any
/// developer's scaled laptop. What they can assert is that the numbers are usable — a caller
/// multiplying by them must not get zero, a negative, or something absurd — and that they do not
/// change from one call to the next, which the layout depends on because it reads them once.
/// </para>
/// <para>
/// They also run without a window, on the test host's own thread, which is the point of keeping
/// them free of <see cref="Form"/>.
/// </para>
/// </remarks>
[Collection("WinFormsHelper")]
public sealed class DisplayMetricsTests
{
    [Fact]
    public void GetDpiScalePercent_IsAPercentageInTheRangeWindowsOffers()
    {
        var scale = Helper.GetDpiScalePercent();

        // 100% is the floor because the layout scales *up* from sizes authored at 96 dpi; below
        // that the editor would be asked to shrink itself, which it has no design for.
        Assert.InRange(scale, 100, 400);
    }

    [Fact]
    public void GetDpiScalePercent_IsStable()
    {
        // The layout reads this once and builds a window from it. If two calls could disagree,
        // the toolbar and the canvas could be sized for different displays.
        Assert.Equal(Helper.GetDpiScalePercent(), Helper.GetDpiScalePercent());
    }

    [Fact]
    public void UsableClientArea_IsPositiveAndPlausible()
    {
        var width = Helper.GetUsableClientWidth();
        var height = Helper.GetUsableClientHeight();

        // The floors are the ones the helper itself guarantees when there is no display to ask,
        // which is the case in a headless run.
        Assert.True(width >= 320, $"usable width was {width}");
        Assert.True(height >= 240, $"usable height was {height}");
    }

    [Fact]
    public void UsableClientArea_LeavesRoomForTheWindowFrame()
    {
        var screen = Screen.PrimaryScreen;

        if (screen is null)
        {
            // Headless: the helper falls back to a fixed guess, and there is no frame to subtract.
            return;
        }

        // A window is larger than its client area by its border and title bar, so a client size
        // equal to the work area would not fit. This is the check that the subtraction happened:
        // without it the editor opens with its status bar under the taskbar.
        Assert.True(
            Helper.GetUsableClientHeight() < screen.WorkingArea.Height,
            "the usable client height must exclude the title bar and border");

        Assert.True(
            Helper.GetUsableClientWidth() <= screen.WorkingArea.Width,
            "the usable client width must not exceed the work area");
    }
}
