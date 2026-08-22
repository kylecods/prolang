using System.Drawing;
using System.Windows.Forms;

namespace WinFormsHelper;

/// <summary>
/// Appearance: flat buttons, images on buttons, fonts, borders, padding and tooltips.
/// </summary>
/// <remarks>
/// Everything here is a property a prolang program cannot set for itself. Interop can call a
/// static method and read a property through call syntax, but it has no way to *assign* to a
/// property of a .NET object — <c>button.FlatStyle = FlatStyle.Flat</c> cannot be bound — so every
/// styling decision has to arrive through a static method taking int handles and packed colours.
/// </remarks>
public static partial class WinFormsHelper
{
    /// <summary>
    /// One shared tooltip provider. Windows Forms attaches tooltips through a component rather
    /// than a control property, so there has to be an instance somewhere; one for the process is
    /// enough and keeps the delay settings consistent.
    /// </summary>
    private static readonly ToolTip _toolTip = new()
    {
        InitialDelay = 400,
        ReshowDelay = 200,
        AutoPopDelay = 8000,
    };

    // ── Buttons ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Turns a button into a flat one with explicit border, hover and pressed colours.
    /// </summary>
    /// <param name="controlId">Handle from <see cref="CreateButton"/>.</param>
    /// <param name="borderArgb">Border colour; a border size of zero is used when it is transparent.</param>
    /// <param name="hoverArgb">Background while the pointer is over it.</param>
    /// <param name="pressedArgb">Background while it is held down.</param>
    /// <remarks>
    /// The default Windows button draws its own gradient and border, which ignores
    /// <see cref="Control.BackColor"/> almost entirely — a themed toolbar built from stock buttons
    /// comes out looking like an unthemed one. <see cref="FlatStyle.Flat"/> is what makes the
    /// colours below actually take effect.
    /// </remarks>
    public static void SetFlatStyle(int controlId, int borderArgb, int hoverArgb, int pressedArgb)
    {
        if (_controls[controlId] is not Button button)
        {
            return;
        }

        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = (borderArgb >> 24 & 0xFF) == 0 ? 0 : 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(borderArgb);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(hoverArgb);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(pressedArgb);
        button.UseVisualStyleBackColor = false;

        // A button that keeps focus draws a dotted rectangle inside itself after every click,
        // which on a 32-pixel icon button covers most of the icon.
        button.TabStop = false;
    }

    /// <summary>Sets a button's border colour and width without changing anything else.</summary>
    /// <remarks>Used to mark the selected tool, which is a border change on every click.</remarks>
    public static void SetFlatBorder(int controlId, int borderArgb, int width)
    {
        if (_controls[controlId] is not Button button)
        {
            return;
        }

        button.FlatAppearance.BorderSize = Math.Max(0, width);
        button.FlatAppearance.BorderColor = Color.FromArgb(borderArgb);
    }

    /// <summary>Displays a bitmap on a button, centred, with no text.</summary>
    public static void SetButtonImage(int controlId, int bitmapId)
    {
        if (_controls[controlId] is not Button button || !_bitmaps.TryGetValue(bitmapId, out var bitmap))
        {
            return;
        }

        button.Image = bitmap;
        button.ImageAlign = ContentAlignment.MiddleCenter;
        button.Text = "";
    }

    /// <summary>
    /// Displays a bitmap on a button with its text underneath.
    /// </summary>
    public static void SetButtonImageAndText(int controlId, int bitmapId, string text)
    {
        if (_controls[controlId] is not Button button || !_bitmaps.TryGetValue(bitmapId, out var bitmap))
        {
            return;
        }

        button.Image = bitmap;
        button.Text = text;
        button.ImageAlign = ContentAlignment.TopCenter;
        button.TextAlign = ContentAlignment.BottomCenter;
        button.TextImageRelation = TextImageRelation.ImageAboveText;
    }

    // ── Text ──────────────────────────────────────────────────────────────────

    /// <summary>Sets a control's font.</summary>
    /// <param name="family">Font family name; ignored if the system does not have it.</param>
    /// <param name="size">Point size.</param>
    /// <param name="bold">Whether to use the bold weight.</param>
    public static void SetFont(int controlId, string family, int size, bool bold)
    {
        try
        {
            _controls[controlId].Font = new Font(family, size, bold ? FontStyle.Bold : FontStyle.Regular);
        }
        catch (ArgumentException)
        {
            // An unavailable family throws rather than falling back; the existing font will do.
        }
    }

    /// <summary>Aligns a label's text. 0 left, 1 centre, 2 right — all vertically centred.</summary>
    public static void SetTextAlign(int controlId, int alignment)
    {
        if (_controls[controlId] is not Label label)
        {
            return;
        }

        label.TextAlign = alignment switch
        {
            1 => ContentAlignment.MiddleCenter,
            2 => ContentAlignment.MiddleRight,
            _ => ContentAlignment.MiddleLeft,
        };
    }

    // ── Layout and chrome ─────────────────────────────────────────────────────

    /// <summary>Sets uniform inner padding, in pixels.</summary>
    public static void SetPadding(int controlId, int padding)
        => _controls[controlId].Padding = new Padding(padding);

    /// <summary>Sets a panel's border. 0 none, 1 single line, 2 sunken 3D.</summary>
    public static void SetBorderStyle(int controlId, int style)
    {
        var borderStyle = style switch
        {
            1 => BorderStyle.FixedSingle,
            2 => BorderStyle.Fixed3D,
            _ => BorderStyle.None,
        };

        switch (_controls[controlId])
        {
            case Panel panel: panel.BorderStyle = borderStyle; break;
            case Label label: label.BorderStyle = borderStyle; break;
            case TextBox textBox: textBox.BorderStyle = borderStyle; break;
        }
    }

    /// <summary>Shows the hand cursor over a control, marking it as clickable.</summary>
    public static void SetHandCursor(int controlId, bool enabled)
        => _controls[controlId].Cursor = enabled ? Cursors.Hand : Cursors.Default;

    /// <summary>Shows the crosshair cursor, for a drawing surface.</summary>
    public static void SetCrosshairCursor(int controlId, bool enabled)
        => _controls[controlId].Cursor = enabled ? Cursors.Cross : Cursors.Default;

    /// <summary>Attaches hover text to a control.</summary>
    /// <remarks>
    /// Not decoration once the toolbar is icons: an icon button with no tooltip is a guess. This
    /// is what keeps the labels available after the text is taken off the buttons.
    /// </remarks>
    public static void SetToolTip(int controlId, string text)
        => _toolTip.SetToolTip(_controls[controlId], text);

    /// <summary>Sets the window's minimum size, so the layout cannot be squeezed past it.</summary>
    public static void SetMinimumSize(int formId, int width, int height)
        => Get<Form>(formId).MinimumSize = new Size(width, height);

    /// <summary>
    /// Suspends layout on a container while many children are added, then resumes it.
    /// </summary>
    /// <remarks>
    /// Windows Forms recomputes layout on every <c>Controls.Add</c>. Building a toolbar of thirty
    /// buttons without this is thirty layout passes and a visible flicker as the window appears.
    /// </remarks>
    public static void BeginLayout(int controlId) => _controls[controlId].SuspendLayout();

    /// <summary>Resumes layout suspended by <see cref="BeginLayout"/>.</summary>
    public static void EndLayout(int controlId) => _controls[controlId].ResumeLayout(true);
}
