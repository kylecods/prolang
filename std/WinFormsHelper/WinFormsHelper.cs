using System.Drawing;
using System.Windows.Forms;

namespace WinFormsHelper;

/// <summary>
/// Static helper library that wraps Windows Forms behind a handle-based API
/// compatible with prolang's type system.
///
/// One-to-one type mappings used throughout:
///   prolang int   <-> .NET int   (control handle IDs, return values, enum values)
///   prolang string <-> .NET string (text, labels, dialog results)
///   prolang bool  <-> .NET bool  (enabled, visible flags)
///   prolang int   <-> System.Drawing.Color (ARGB packed int via Color.FromArgb / Color.ToArgb)
/// </summary>
public static partial class WinFormsHelper
{
    // ── Registry ──────────────────────────────────────────────────────────────
    private static readonly Dictionary<int, Control> _controls = [];
    private static int _nextId = 1;

    private static int Register(Control c)
    {
        int id = _nextId++;
        _controls[id] = c;
        return id;
    }

    private static T Get<T>(int id) where T : Control
        => (T)_controls[id];

    // ── Click-event polling ───────────────────────────────────────────────────
    private static readonly Dictionary<int, int> _lastClickTag = new();

    // ── Form lifecycle ────────────────────────────────────────────────────────

    /// <summary>Creates a new Form. Returns an int handle (prolang int ↔ .NET int, one-to-one).</summary>
    /// <remarks>
    /// <para>
    /// <see cref="AutoScaleMode.None"/> is deliberate. The process is DPI aware (see
    /// <see cref="InitApplication"/>), and a prolang program scales its own sizes from
    /// <see cref="GetDpiScalePercent"/>; letting Windows Forms apply a second factor on top would
    /// scale everything twice.
    /// </para>
    /// <para>
    /// It also keeps the sizes exact. Automatic scaling multiplies every coordinate by a
    /// fractional factor and rounds, which is fine for a form of labels and unacceptable for one
    /// holding a canvas that maps an image pixel to a whole number of screen pixels.
    /// </para>
    /// </remarks>
    public static int CreateForm(string title, int width, int height)
    {
        var form = new Form
        {
            Text = title,
            AutoScaleMode = AutoScaleMode.None,
            ClientSize = new Size(width, height),
            StartPosition = FormStartPosition.CenterScreen,
        };
        return Register(form);
    }

    /// <summary>Runs the application message loop with the given form as the main window.</summary>
    public static void RunApplication(int formId)
    {
        var form = Get<Form>(formId);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(form);
    }

    /// <summary>Shows the form as a modal dialog (blocks until closed).</summary>
    public static void ShowDialog(int formId)
        => Get<Form>(formId).ShowDialog();

    /// <summary>
    /// Shows the form non-modally (non-blocking).
    /// Use WaitForClick / DoEvents to drive the message loop from prolang.
    /// </summary>
    public static void Show(int formId)
        => Get<Form>(formId).Show();

    /// <summary>Pumps pending Windows messages without blocking. Call in a loop for event-driven UIs.</summary>
    public static void DoEvents()
        => Application.DoEvents();

    /// <summary>Returns true if the form is still visible (not closed).</summary>
    public static bool IsVisible(int formId)
        => _controls.TryGetValue(formId, out var c) && c.Visible;

    /// <summary>Closes and disposes the form.</summary>
    public static void CloseForm(int formId)
        => Get<Form>(formId).Close();

    // ── Control creation ──────────────────────────────────────────────────────

    /// <summary>Creates a Button. Returns a handle (prolang int ↔ .NET int, one-to-one).</summary>
    public static int CreateButton(string text, int x, int y, int width, int height)
    {
        var btn = new Button
        {
            Text = text,
            Left = x,
            Top = y,
            Width = width,
            Height = height,
        };
        return Register(btn);
    }

    /// <summary>Creates a Label. Returns a handle.</summary>
    public static int CreateLabel(string text, int x, int y, int width, int height)
    {
        var lbl = new Label
        {
            Text = text,
            Left = x,
            Top = y,
            Width = width,
            Height = height,
            AutoSize = false,
        };
        return Register(lbl);
    }

    /// <summary>Creates a TextBox. Returns a handle.</summary>
    public static int CreateTextBox(int x, int y, int width, int height)
    {
        var tb = new TextBox
        {
            Left = x,
            Top = y,
            Width = width,
            Height = height,
        };
        return Register(tb);
    }

    /// <summary>Creates a Panel. Returns a handle.</summary>
    public static int CreatePanel(int x, int y, int width, int height)
    {
        var panel = new Panel
        {
            Left = x,
            Top = y,
            Width = width,
            Height = height,
        };
        return Register(panel);
    }

    // ── Control wiring ────────────────────────────────────────────────────────

    /// <summary>Adds a child control to a parent form or panel.</summary>
    public static void AddControl(int parentId, int childId)
        => _controls[parentId].Controls.Add(_controls[childId]);

    // ── Properties ───────────────────────────────────────────────────────────

    /// <summary>Sets the Text property of a control (prolang string ↔ .NET string, one-to-one).</summary>
    public static void SetText(int id, string text)
        => _controls[id].Text = text;

    /// <summary>Gets the Text property of a control (prolang string ↔ .NET string, one-to-one).</summary>
    public static string GetText(int id)
        => _controls[id].Text;

    public static void SetEnabled(int id, bool enabled)
        => _controls[id].Enabled = enabled;

    public static void SetVisible(int id, bool visible)
        => _controls[id].Visible = visible;

    public static void SetLocation(int id, int x, int y)
        => _controls[id].Location = new Point(x, y);

    public static void SetSize(int id, int width, int height)
        => _controls[id].Size = new Size(width, height);

    /// <summary>Moves and resizes a control in one call.</summary>
    /// <remarks>
    /// Not just shorthand for <see cref="SetLocation"/> followed by <see cref="SetSize"/>. Those
    /// are two changes, and a control laid out again while its window is being dragged is seen in
    /// the intermediate state — moved but not yet resized — which reads as a flicker along the
    /// edge being dragged. <see cref="Control.SetBounds(int, int, int, int)"/> is one change.
    /// </remarks>
    public static void SetBounds(int id, int x, int y, int width, int height)
        => _controls[id].SetBounds(x, y, Math.Max(0, width), Math.Max(0, height));

    /// <summary>
    /// Sets a window's <em>client</em> size — the area a layout actually gets.
    /// </summary>
    /// <remarks>
    /// <see cref="CreateForm"/> sets the whole window, frame and title bar included, so a form
    /// created 480 wide has perhaps 464 to draw in. A layout written against the size that was
    /// asked for then overruns the bottom and right edges by exactly the frame — which looks like
    /// the last row of a screen having been forgotten rather than like a sizing mistake.
    /// </remarks>
    public static void SetClientSize(int formId, int width, int height)
        => Get<Form>(formId).ClientSize = new Size(Math.Max(0, width), Math.Max(0, height));

    /// <summary>The width of a control's client area — for a form, its size less the frame.</summary>
    public static int GetClientWidth(int id) => _controls[id].ClientSize.Width;

    /// <summary>The height of a control's client area.</summary>
    public static int GetClientHeight(int id) => _controls[id].ClientSize.Height;

    /// <summary>
    /// Sets the smallest <em>client</em> area the window may be resized to.
    /// </summary>
    /// <remarks>
    /// Windows Forms states a minimum in whole-window terms, but a layout is written against the
    /// client area — so the frame is measured here rather than guessed at the call site. Without a
    /// minimum, a window dragged small enough gives the layout negative room, and every control
    /// positioned from what is left lands on top of the ones before it.
    /// </remarks>
    public static void SetMinimumClientSize(int formId, int width, int height)
    {
        var form = Get<Form>(formId);
        var frame = form.Size - form.ClientSize;

        form.MinimumSize = new Size(width + frame.Width, height + frame.Height);
    }

    /// <summary>
    /// Sets the back-color from a packed ARGB int.
    /// prolang int ↔ System.Drawing.Color is one-to-one via Color.FromArgb / Color.ToArgb.
    /// Example: 0xFF0000FF = opaque blue.
    /// </summary>
    public static void SetBackColor(int id, int argb)
        => _controls[id].BackColor = Color.FromArgb(argb);

    /// <summary>Sets the fore-color from a packed ARGB int (same one-to-one mapping as SetBackColor).</summary>
    public static void SetForeColor(int id, int argb)
        => _controls[id].ForeColor = Color.FromArgb(argb);

    /// <summary>
    /// Sets the back-color from separate R, G, B components (0-255 each).
    /// Convenient from prolang since all three fit comfortably in a prolang int.
    /// prolang int ↔ .NET int — one-to-one for each channel.
    /// </summary>
    public static void SetBackColorRGB(int id, int r, int g, int b)
        => _controls[id].BackColor = Color.FromArgb(r, g, b);

    /// <summary>Sets the fore-color from separate R, G, B components (0-255 each).</summary>
    public static void SetForeColorRGB(int id, int r, int g, int b)
        => _controls[id].ForeColor = Color.FromArgb(r, g, b);

    // ── Event polling ─────────────────────────────────────────────────────────

    /// <summary>
    /// Wires a click handler to a control. When clicked, stores the tag under
    /// the form's ID so WaitForClick/GetLastClickTag can retrieve it.
    /// No delegates are needed from prolang — the int tag is the only exchange point.
    /// </summary>
    public static void RegisterClickTag(int controlId, int tag)
    {
        _controls[controlId].Click += (_, _) =>
        {
            var parentForm = _controls[controlId].FindForm();
            if (parentForm != null)
            {
                foreach (var kvp in _controls)
                {
                    if (kvp.Value == parentForm)
                    {
                        _lastClickTag[kvp.Key] = tag;
                        break;
                    }
                }
            }
        };
    }

    /// <summary>
    /// Pumps the message loop until a registered button click is detected on this form.
    /// Returns the tag (prolang int ↔ .NET int, one-to-one). Returns -1 if form closes.
    /// </summary>
    public static int WaitForClick(int formId)
    {
        if (!_controls.TryGetValue(formId, out var ctrl) || ctrl is not Form form)
            return -1;

        _lastClickTag.Remove(formId);

        while (form.Visible)
        {
            Application.DoEvents();
            if (_lastClickTag.TryGetValue(formId, out var tag))
                return tag;
            System.Threading.Thread.Sleep(10);
        }

        return -1;
    }

    /// <summary>Returns the tag of the last clicked button on this form, or -1 if none.</summary>
    public static int GetLastClickTag(int formId)
        => _lastClickTag.TryGetValue(formId, out var tag) ? tag : -1;

    /// <summary>Resets the last-click tag for the form.</summary>
    public static void ResetClickTag(int formId)
        => _lastClickTag.Remove(formId);

    // ── Bitmap & Graphics registry ────────────────────────────────────────────
    private static readonly Dictionary<int, Bitmap>   _bitmaps  = [];
    private static readonly Dictionary<int, Graphics> _graphics = [];

    // ── Color helper ──────────────────────────────────────────────────────────

    /// <summary>
    /// Packs four 0–255 channel values into a single ARGB int.
    /// Returned value may be negative (signed 32-bit) — that is correct:
    /// Color.FromArgb(int) reads all 32 bits unsigned, so the sign bit is just
    /// the high bit of the alpha channel.
    /// prolang int ↔ .NET int — one-to-one; no arithmetic overflow in prolang needed.
    /// </summary>
    public static int MakeColor(int a, int r, int g, int b)
        => Color.FromArgb(a, r, g, b).ToArgb();

    // ── Bitmap creation / disposal ────────────────────────────────────────────

    /// <summary>Creates a Bitmap of the given size. Returns an int handle.</summary>
    public static int CreateBitmap(int width, int height)
    {
        int id = _nextId++;
        _bitmaps[id] = new Bitmap(width, height);
        return id;
    }

    /// <summary>Disposes and removes a Bitmap by handle.</summary>
    public static void DisposeBitmap(int id)
    {
        if (_bitmaps.Remove(id, out var bmp))
            bmp.Dispose();
    }

    // ── Graphics creation / disposal ──────────────────────────────────────────

    /// <summary>
    /// Creates a Graphics context from a Bitmap handle. Returns a Graphics handle.
    /// The Graphics object draws directly into the Bitmap's pixel buffer.
    /// </summary>
    public static int CreateGraphics(int bitmapId)
    {
        int id = _nextId++;
        _graphics[id] = Graphics.FromImage(_bitmaps[bitmapId]);
        return id;
    }

    /// <summary>Flushes and disposes a Graphics context by handle.</summary>
    public static void DisposeGraphics(int id)
    {
        if (_graphics.Remove(id, out var g))
            g.Dispose();
    }

    // ── Drawing operations ────────────────────────────────────────────────────
    // Colors: packed ARGB int from MakeColor() — prolang int ↔ Color.FromArgb, one-to-one.
    // Coordinates / sizes: prolang int ↔ .NET int, one-to-one.
    // penWidth / fontSize: prolang int, widened to float inside C# as needed.

    /// <summary>Clears the entire surface with a solid color.</summary>
    public static void GClear(int gId, int argb)
        => _graphics[gId].Clear(Color.FromArgb(argb));

    /// <summary>Draws a line between two points with the given stroke width (pixels).</summary>
    public static void GDrawLine(int gId, int argb, int penWidth, int x1, int y1, int x2, int y2)
    {
        using var pen = new Pen(Color.FromArgb(argb), penWidth);
        _graphics[gId].DrawLine(pen, x1, y1, x2, y2);
    }

    /// <summary>Draws a rectangle outline.</summary>
    public static void GDrawRectangle(int gId, int argb, int penWidth, int x, int y, int w, int h)
    {
        using var pen = new Pen(Color.FromArgb(argb), penWidth);
        _graphics[gId].DrawRectangle(pen, x, y, w, h);
    }

    /// <summary>Fills a rectangle with a solid color.</summary>
    public static void GFillRectangle(int gId, int argb, int x, int y, int w, int h)
    {
        using var brush = new SolidBrush(Color.FromArgb(argb));
        _graphics[gId].FillRectangle(brush, x, y, w, h);
    }

    /// <summary>Draws an ellipse (or circle) outline.</summary>
    public static void GDrawEllipse(int gId, int argb, int penWidth, int x, int y, int w, int h)
    {
        using var pen = new Pen(Color.FromArgb(argb), penWidth);
        _graphics[gId].DrawEllipse(pen, x, y, w, h);
    }

    /// <summary>Fills an ellipse (or circle) with a solid color.</summary>
    public static void GFillEllipse(int gId, int argb, int x, int y, int w, int h)
    {
        using var brush = new SolidBrush(Color.FromArgb(argb));
        _graphics[gId].FillEllipse(brush, x, y, w, h);
    }

    /// <summary>
    /// Draws a string at (x, y) using the given color and font size in points.
    /// fontSize: prolang int, converted to float for the Font constructor.
    /// </summary>
    public static void GDrawString(int gId, string text, int argb, int fontSize, int x, int y)
    {
        using var brush = new SolidBrush(Color.FromArgb(argb));
        using var font  = new Font("Arial", (float)fontSize);
        _graphics[gId].DrawString(text, font, brush, x, y);
    }

    // ── Direct pixel access ───────────────────────────────────────────────────

    /// <summary>
    /// Sets a single pixel in a Bitmap.
    /// argb: packed int from MakeColor() — prolang int ↔ Color.FromArgb, one-to-one.
    /// </summary>
    public static void SetPixel(int bitmapId, int x, int y, int argb)
        => _bitmaps[bitmapId].SetPixel(x, y, Color.FromArgb(argb));

    /// <summary>
    /// Reads a single pixel from a Bitmap as a packed ARGB int.
    /// Return value: prolang int ↔ Color.ToArgb(), one-to-one (may be negative — correct).
    /// </summary>
    public static int GetPixel(int bitmapId, int x, int y)
        => _bitmaps[bitmapId].GetPixel(x, y).ToArgb();

    // ── PictureBox ────────────────────────────────────────────────────────────

    /// <summary>Creates a PictureBox at the given bounds. Returns a handle.</summary>
    public static int CreatePictureBox(int x, int y, int width, int height)
    {
        var pb = new PictureBox
        {
            Left     = x,
            Top      = y,
            Width    = width,
            Height   = height,
            SizeMode = PictureBoxSizeMode.Normal,
        };
        return Register(pb);
    }

    /// <summary>
    /// Assigns a Bitmap to a PictureBox so it is displayed immediately.
    /// Both IDs are prolang int handles — one-to-one with their respective registries.
    /// </summary>
    public static void SetImage(int pictureBoxId, int bitmapId)
        => Get<PictureBox>(pictureBoxId).Image = _bitmaps[bitmapId];

    /// <summary>Forces an immediate repaint of a control (Invalidate + Update).</summary>
    public static void RefreshControl(int id)
    {
        _controls[id].Invalidate();
        _controls[id].Update();
    }

    // ── Static dialogs ────────────────────────────────────────────────────────

    /// <summary>Shows a MessageBox with OK button.</summary>
    public static void MessageBoxShow(string text, string title)
        => ShowMessage(text, title, MessageBoxButtons.OK);

    /// <summary>
    /// Shows a Yes/No MessageBox.
    /// Returns 1 for Yes, 0 for No (prolang int ↔ DialogResult, one-to-one mapping).
    /// </summary>
    public static int MessageBoxYesNo(string text, string title)
        => ShowMessage(text, title, MessageBoxButtons.YesNo) == DialogResult.Yes ? 1 : 0;

    /// <summary>
    /// Shows a simple input dialog. Returns the entered string, or "" if cancelled.
    /// prolang string ↔ .NET string — one-to-one.
    /// </summary>
    public static string InputDialog(string prompt, string title)
    {
        using var form = new Form
        {
            Text = title,
            Width = 360,
            Height = 150,
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
        };

        var label = new Label { Left = 10, Top = 15, Width = 330, Text = prompt };
        var input = new TextBox { Left = 10, Top = 40, Width = 330 };
        var ok = new Button { Text = "OK", Left = 190, Top = 70, Width = 75, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 275, Top = 70, Width = 75, DialogResult = DialogResult.Cancel };

        form.Controls.AddRange([label, input, ok, cancel]);
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        return form.ShowDialog() == DialogResult.OK ? input.Text : "";
    }
}
