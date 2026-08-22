using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WinFormsHelper;

/// <summary>
/// Input polling, the zoomable canvas control, and the file dialogs — everything a drawing
/// program needs that click-tag polling cannot express.
/// </summary>
/// <remarks>
/// <para>
/// The existing <see cref="WinFormsHelper.WaitForClick"/> blocks until a button is pressed, which
/// is fine for a form that only has buttons and useless for one where the mouse draws. These
/// methods invert it: Windows Forms handlers push into an <see cref="EventQueue"/>, and the
/// prolang program pulls from it whenever it is ready.
/// </para>
/// <para>
/// Every event field crosses the boundary in a caller-allocated <c>int[]</c> rather than through
/// one accessor per field. A prolang <c>array&lt;int&gt;</c> is a real CLR array, so this is one
/// interop call per event instead of eight, and there is no window in which a program can read
/// the fields of one event after another has replaced them.
/// </para>
/// </remarks>
public static partial class WinFormsHelper
{
    private static readonly EventQueue _events = new();

    /// <summary>Number of int slots <see cref="PollEventInto"/> writes.</summary>
    public const int EventFieldCount = 8;

    /// <summary>
    /// The window dialogs are owned by, set by <see cref="EnableKeyInput"/>.
    /// </summary>
    /// <remarks>
    /// A modal dialog shown with no owner picks one from whichever window happens to be active,
    /// which for a program driving its own message pump is not reliably the editor. The visible
    /// consequence is a prompt that opens *behind* the main window while disabling it, which is
    /// indistinguishable from a hang.
    /// </remarks>
    private static Form? _mainForm;

    /// <summary>Shows a dialog owned by the main window when there is one.</summary>
    private static DialogResult ShowModal(Form dialog)
        => _mainForm is { IsDisposed: false } owner ? dialog.ShowDialog(owner) : dialog.ShowDialog();

    /// <summary>Shows a common dialog owned by the main window when there is one.</summary>
    private static DialogResult ShowModal(CommonDialog dialog)
        => _mainForm is { IsDisposed: false } owner ? dialog.ShowDialog(owner) : dialog.ShowDialog();

    /// <summary>Shows a message box owned by the main window when there is one.</summary>
    internal static DialogResult ShowMessage(string text, string title, MessageBoxButtons buttons)
        => _mainForm is { IsDisposed: false } owner
            ? MessageBox.Show(owner, text, title, buttons, MessageBoxIcon.Question)
            : MessageBox.Show(text, title, buttons, MessageBoxIcon.Question);

    // ── Application lifecycle ─────────────────────────────────────────────────

    /// <summary>
    /// Prepares the process for Windows Forms. Must run before any control is created.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The process is made DPI aware, and then does its own scaling: a prolang program asks for
    /// <see cref="GetDpiScalePercent"/> and multiplies its own sizes. The alternative — declaring
    /// the process unaware and letting Windows stretch the finished window — is what makes an
    /// application look dated on a modern laptop, because a stretched bitmap is blurry at every
    /// scaling factor that is not a whole number.
    /// </para>
    /// <para>
    /// <see cref="HighDpiMode.SystemAware"/> rather than per-monitor: a per-monitor process is
    /// told its scaling factor has changed when the window is dragged to another display, and is
    /// expected to lay itself out again. That is a message this program has nowhere to deliver,
    /// since the prolang side reads its metrics once while building the window. System awareness
    /// fixes the factor for the life of the process, which is a promise this design can keep.
    /// </para>
    /// <para>
    /// Awareness is safe for the canvas because Windows Forms is never asked to scale anything:
    /// forms are created with <see cref="AutoScaleMode.None"/>, so one image pixel still maps to a
    /// whole number of screen pixels and the prolang coordinate arithmetic stays exact.
    /// </para>
    /// </remarks>
    public static void InitApplication()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
        }
        catch (InvalidOperationException)
        {
            // Already set, which happens if a form was created first. Not worth failing over.
        }
    }

    // ── Display metrics ───────────────────────────────────────────────────────

    /// <summary>
    /// The display's scaling factor as a percentage: 100 unscaled, 150 at "150%", and so on.
    /// </summary>
    /// <remarks>
    /// Reported as a percentage rather than a ratio because prolang has no floating point, and as
    /// a whole number of percent rather than the raw dots per inch because that is the number the
    /// user chose in the settings app — a program that scales by 150/100 produces the sizes the
    /// display was configured to show.
    /// </remarks>
    public static int GetDpiScalePercent()
    {
        try
        {
            using var probe = new Control();

            if (probe.DeviceDpi > 0)
            {
                return Math.Clamp(probe.DeviceDpi * 100 / 96, 100, 400);
            }
        }
        catch (InvalidOperationException)
        {
            // No display to ask, which happens in a headless test host.
        }

        return 100;
    }

    /// <summary>
    /// The widest client area a window can have and still fit on the primary display.
    /// </summary>
    /// <remarks>
    /// The <em>client</em> area, not the window: <see cref="CreateForm"/> takes a client size, and
    /// a window is larger than its client area by its border and title bar. Returning the budget
    /// the caller can actually spend means the sizing arithmetic on the prolang side has nothing
    /// to know about window frames.
    /// </remarks>
    public static int GetUsableClientWidth()
        => Math.Max(320, WorkArea().Width - (SystemInformation.FrameBorderSize.Width * 2));

    /// <summary>The tallest client area a window can have and still fit on the primary display.</summary>
    public static int GetUsableClientHeight()
        => Math.Max(240, WorkArea().Height
                         - SystemInformation.CaptionHeight
                         - (SystemInformation.FrameBorderSize.Height * 2));

    /// <summary>The primary display minus the taskbar, or a conservative guess if there is none.</summary>
    private static Rectangle WorkArea()
        => Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);

    // ── Canvas ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the drawing surface: a double-buffered, focusable control that paints a bitmap
    /// scaled by an integer zoom factor with an optional pixel grid.
    /// </summary>
    /// <remarks>
    /// A <see cref="PictureBox"/> cannot do this job. It will not take keyboard focus, it has no
    /// nearest-neighbour scaling mode, and its <c>Zoom</c> mode blurs every pixel with bilinear
    /// interpolation — which is precisely wrong when each pixel is the thing being edited.
    /// </remarks>
    public static int CreateCanvas(int x, int y, int width, int height)
    {
        var canvas = new CanvasControl
        {
            Left = x,
            Top = y,
            Width = width,
            Height = height,
        };

        return Register(canvas);
    }

    /// <summary>Points the canvas at a bitmap from the registry.</summary>
    public static void CanvasSetBitmap(int canvasId, int bitmapId)
        => Get<CanvasControl>(canvasId).Source = _bitmaps[bitmapId];

    /// <summary>
    /// Sets how the bitmap is mapped onto the control.
    /// </summary>
    /// <param name="canvasId">Handle from <see cref="CreateCanvas"/>.</param>
    /// <param name="zoom">Screen pixels per image pixel. Clamped to at least 1.</param>
    /// <param name="panX">Image-space column drawn at the control's left edge.</param>
    /// <param name="panY">Image-space row drawn at the control's top edge.</param>
    /// <param name="gridArgb">Grid line colour; the grid is hidden when the alpha byte is zero.</param>
    /// <param name="checkerOn">Non-zero to draw a checkerboard behind transparent pixels.</param>
    /// <remarks>
    /// The mapping is <c>screen = (image - pan) * zoom</c>, and the prolang side must use exactly
    /// the same formula. Any disagreement shows up as a cursor that paints a pixel next to the one
    /// under it, which is maddening to debug from either side alone.
    /// </remarks>
    public static void CanvasSetView(int canvasId, int zoom, int panX, int panY, int gridArgb, int checkerOn)
    {
        var canvas = Get<CanvasControl>(canvasId);
        canvas.Zoom = Math.Max(1, zoom);
        canvas.PanX = panX;
        canvas.PanY = panY;
        canvas.GridArgb = gridArgb;
        canvas.Checkered = checkerOn != 0;
    }

    /// <summary>Repaints the canvas.</summary>
    public static void CanvasRefresh(int canvasId)
        => Get<CanvasControl>(canvasId).Invalidate();

    /// <summary>Turns double buffering on for any control that supports it.</summary>
    public static void SetDoubleBuffered(int controlId, bool enabled)
    {
        var control = _controls[controlId];
        var property = typeof(Control).GetProperty(
            "DoubleBuffered",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        property?.SetValue(control, enabled);
    }

    // ── Input wiring ──────────────────────────────────────────────────────────

    /// <summary>
    /// Routes every mouse event on one control into the queue, tagged with its handle.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="EnableKeyInput"/> because an editor has more than one thing the
    /// mouse acts on — the drawing canvas and the palette strip — but only one place keys should
    /// come from. Each event carries its source handle so the program can tell them apart.
    /// </remarks>
    public static void EnableMouseInput(int controlId)
    {
        var control = _controls[controlId];

        control.MouseDown += (_, e) => Push(PrlEventKind.MouseDown, controlId, e);
        control.MouseMove += (_, e) => Push(PrlEventKind.MouseMove, controlId, e);
        control.MouseUp += (_, e) => Push(PrlEventKind.MouseUp, controlId, e);
        control.MouseLeave += (_, _) => _events.Enqueue(new PrlEvent(PrlEventKind.MouseLeave, Source: controlId));

        control.MouseWheel += (_, e) => _events.Enqueue(new PrlEvent(
            PrlEventKind.Wheel,
            e.X, e.Y,
            Delta: e.Delta / 120,
            Modifiers: CurrentModifiers(),
            Source: controlId));

        control.Resize += (_, _) => _events.Enqueue(new PrlEvent(
            PrlEventKind.Resize, control.Width, control.Height, Source: controlId));
    }

    /// <summary>
    /// Routes key events and the window's close into the queue.
    /// </summary>
    /// <remarks>
    /// Keys are taken from the form with <see cref="Form.KeyPreview"/> set, not from the canvas,
    /// so a shortcut still works when focus has landed on a toolbar button.
    /// </remarks>
    public static void EnableKeyInput(int formId)
    {
        var form = Get<Form>(formId);

        // Also the window every dialog will be owned by. This is the one call a GUI program
        // always makes on its main form, which makes it the natural place to record it.
        _mainForm = form;

        form.KeyPreview = true;
        form.KeyDown += (_, e) => _events.Enqueue(new PrlEvent(
            PrlEventKind.KeyDown, Key: (int)e.KeyCode, Modifiers: CurrentModifiers(), Source: formId));
        form.KeyUp += (_, e) => _events.Enqueue(new PrlEvent(
            PrlEventKind.KeyUp, Key: (int)e.KeyCode, Modifiers: CurrentModifiers(), Source: formId));

        form.FormClosed += (_, _) => _events.Enqueue(new PrlEvent(PrlEventKind.Quit, Source: formId));
    }

    /// <summary>Wires mouse input on a canvas and key input on its form, the common case.</summary>
    public static void EnableInput(int formId, int canvasId)
    {
        EnableMouseInput(canvasId);
        EnableKeyInput(formId);
    }

    /// <summary>
    /// Makes a control post a <see cref="PrlEventKind.Command"/> carrying <paramref name="tag"/>
    /// when clicked.
    /// </summary>
    /// <remarks>
    /// The replacement for <see cref="RegisterClickTag"/>, which stores into a side table that
    /// only the blocking <see cref="WaitForClick"/> reads. Routing buttons through the same queue
    /// as the mouse means the program has one loop and one ordering, instead of a drawing loop
    /// that has to stop and ask whether a button was pressed.
    /// </remarks>
    public static void EnableCommand(int controlId, int tag)
    {
        _controls[controlId].Click += (_, _) =>
            _events.Enqueue(new PrlEvent(PrlEventKind.Command, Tag: tag, Source: controlId));
    }

    // ── Polling ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Pumps pending Windows messages, then takes one event.
    /// </summary>
    /// <param name="buffer">
    /// Receives the event's fields: x, y, button, key, delta, modifiers, tag, source. Left
    /// untouched when nothing was queued.
    /// </param>
    /// <returns>The event kind as an integer, or 0 when the queue is empty.</returns>
    public static int PollEventInto(int[] buffer)
    {
        Application.DoEvents();

        if (!_events.TryDequeue(out var e))
        {
            return (int)PrlEventKind.None;
        }

        Unpack(e, buffer);
        return (int)e.Kind;
    }

    /// <summary>
    /// Like <see cref="PollEventInto"/>, but keeps pumping for up to
    /// <paramref name="timeoutMs"/> before giving up.
    /// </summary>
    /// <remarks>
    /// The sleep between pumps is what keeps an idle editor off the CPU. Spinning on
    /// <see cref="Application.DoEvents"/> alone pins a core at 100% for a program that is doing
    /// nothing but waiting for the mouse to move.
    /// </remarks>
    public static int WaitEventInto(int[] buffer, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + Math.Max(0, timeoutMs);

        while (true)
        {
            var kind = PollEventInto(buffer);

            if (kind != (int)PrlEventKind.None)
            {
                return kind;
            }

            if (Environment.TickCount64 >= deadline)
            {
                return (int)PrlEventKind.None;
            }

            System.Threading.Thread.Sleep(1);
        }
    }

    /// <summary>Flattens an event into the layout prolang reads.</summary>
    private static void Unpack(PrlEvent e, int[] buffer)
    {
        if (buffer.Length < EventFieldCount)
        {
            return;
        }

        buffer[0] = e.X;
        buffer[1] = e.Y;
        buffer[2] = e.Button;
        buffer[3] = e.Key;
        buffer[4] = e.Delta;
        buffer[5] = e.Modifiers;
        buffer[6] = e.Tag;
        buffer[7] = e.Source;
    }

    private static void Push(PrlEventKind kind, int sourceId, MouseEventArgs e)
        => _events.Enqueue(new PrlEvent(
            kind, e.X, e.Y, ToButtonFlags(e.Button), Modifiers: CurrentModifiers(), Source: sourceId));

    private static int ToButtonFlags(MouseButtons buttons)
    {
        var flags = 0;
        if ((buttons & MouseButtons.Left) != 0) { flags |= 1; }
        if ((buttons & MouseButtons.Right) != 0) { flags |= 2; }
        if ((buttons & MouseButtons.Middle) != 0) { flags |= 4; }
        return flags;
    }

    private static int CurrentModifiers()
    {
        var flags = 0;
        var modifiers = Control.ModifierKeys;
        if ((modifiers & Keys.Shift) != 0) { flags |= 1; }
        if ((modifiers & Keys.Control) != 0) { flags |= 2; }
        if ((modifiers & Keys.Alt) != 0) { flags |= 4; }
        return flags;
    }

    // ── PNG ───────────────────────────────────────────────────────────────────

    /// <summary>Writes a bitmap to disk as a PNG.</summary>
    /// <returns>False if the file could not be written.</returns>
    public static bool SaveBitmapPng(int bitmapId, string path)
    {
        try
        {
            _bitmaps[bitmapId].Save(path, ImageFormat.Png);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ExternalException)
        {
            return false;
        }
    }

    /// <summary>Loads a PNG (or any image GDI+ can read) into the bitmap registry.</summary>
    /// <returns>A bitmap handle, or -1 if the file could not be read.</returns>
    /// <remarks>
    /// The image is redrawn into a fresh 32-bit ARGB bitmap rather than used as loaded, so that
    /// the packed integers <see cref="GetPixels"/> produces mean the same thing regardless of how
    /// the file was encoded. It also releases the file handle, which
    /// <see cref="Image.FromFile(string)"/> otherwise keeps for the lifetime of the image.
    /// </remarks>
    public static int LoadBitmapPng(string path)
    {
        try
        {
            using var loaded = Image.FromFile(path);

            var copy = new Bitmap(loaded.Width, loaded.Height, CanvasFormat);

            using (var g = Graphics.FromImage(copy))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(loaded, 0, 0, loaded.Width, loaded.Height);
            }

            var id = _nextId++;
            _bitmaps[id] = copy;
            return id;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or ExternalException)
        {
            return -1;
        }
    }

    /// <summary>Width in pixels of a bitmap in the registry.</summary>
    public static int GetBitmapWidth(int bitmapId)
        => _bitmaps.TryGetValue(bitmapId, out var bmp) ? bmp.Width : 0;

    /// <summary>Height in pixels of a bitmap in the registry.</summary>
    public static int GetBitmapHeight(int bitmapId)
        => _bitmaps.TryGetValue(bitmapId, out var bmp) ? bmp.Height : 0;

    // ── Dialogs ───────────────────────────────────────────────────────────────

    /// <summary>Shows an open-file dialog.</summary>
    /// <returns>The chosen path, or an empty string if cancelled.</returns>
    /// <remarks>
    /// The common dialogs are COM objects and need the calling thread to be in a single-threaded
    /// apartment. The compiler marks the entry point <c>[STAThread]</c> for <c>--target=winexe</c>
    /// and for the Windows Desktop framework; without it this call disables the owner window and
    /// never returns, which looks exactly like a hung program.
    /// </remarks>
    public static string ShowOpenFileDialog(string title, string filter)
    {
        using var dialog = new OpenFileDialog { Title = title, Filter = filter };
        return ShowModal(dialog) == DialogResult.OK ? dialog.FileName : "";
    }

    /// <summary>Shows a save-file dialog.</summary>
    /// <returns>The chosen path, or an empty string if cancelled.</returns>
    public static string ShowSaveFileDialog(string title, string filter, string defaultName)
    {
        using var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = defaultName };
        return ShowModal(dialog) == DialogResult.OK ? dialog.FileName : "";
    }

    /// <summary>Shows a Yes/No/Cancel prompt.</summary>
    /// <returns>1 for yes, 0 for no, -1 for cancel.</returns>
    public static int MessageBoxYesNoCancel(string text, string title)
        => ShowMessage(text, title, MessageBoxButtons.YesNoCancel) switch
        {
            DialogResult.Yes => 1,
            DialogResult.No => 0,
            _ => -1,
        };

    /// <summary>
    /// Prompts for a whole number, clamped to a range.
    /// </summary>
    /// <returns><paramref name="defaultValue"/> if the dialog is cancelled or the text is not a number.</returns>
    /// <remarks>
    /// This exists because prolang has no string-to-number conversion at all: there is no rule
    /// from <c>string</c> to <c>int</c>, so <c>int(InputDialog(...))</c> does not compile. Parsing
    /// here keeps the one dialog that needs a number from requiring a hand-rolled parser at every
    /// call site.
    /// </remarks>
    public static int InputInt(string prompt, string title, int defaultValue, int min, int max)
    {
        // Pre-filled and pre-selected, so the prompt shows what it will do if you just press
        // Enter. An empty box reads as though the program is waiting for something it has not
        // said, and typing over a selected value is no more work than typing into an empty one.
        var text = InputDialogWithDefault(prompt, title, defaultValue.ToString());

        if (!int.TryParse(text, out var value))
        {
            return defaultValue;
        }

        return Math.Clamp(value, min, max);
    }

    /// <summary>
    /// A text prompt whose field starts holding <paramref name="defaultText"/>, selected.
    /// </summary>
    /// <returns>The entered text, or an empty string if cancelled.</returns>
    public static string InputDialogWithDefault(string prompt, string title, string defaultText)
    {
        using var form = new Form
        {
            Text = title,
            Width = 360,
            Height = 150,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
        };

        var label = new Label { Left = 10, Top = 15, Width = 330, Text = prompt };
        var input = new TextBox { Left = 10, Top = 40, Width = 330, Text = defaultText };
        var ok = new Button { Text = "OK", Left = 190, Top = 70, Width = 75, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 275, Top = 70, Width = 75, DialogResult = DialogResult.Cancel };

        form.Controls.AddRange([label, input, ok, cancel]);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        form.Shown += (_, _) => input.SelectAll();

        return ShowModal(form) == DialogResult.OK ? input.Text : "";
    }

    /// <summary>
    /// The canvas: paints a bitmap at integer zoom with nearest-neighbour sampling.
    /// </summary>
    private sealed class CanvasControl : Control
    {
        // Fields rather than properties: a public property on a Control is treated as designer
        // state and has to declare how it serialises (analyzer WFO1000). Nothing here is ever
        // touched by a designer — the whole control is constructed from prolang — so there is no
        // serialization story to describe.
        public Bitmap? Source;
        public int Zoom = 1;
        public int PanX;
        public int PanY;
        public int GridArgb;
        public bool Checkered;

        public CanvasControl()
        {
            // OptimizedDoubleBuffer is what stops the canvas flickering while a stroke is drawn;
            // Selectable lets it take focus so it can be clicked without the form losing keys.
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.Selectable
                | ControlStyles.ResizeRedraw,
                true);

            TabStop = true;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            base.OnMouseDown(e);
        }

        /// <summary>
        /// Claims the arrow keys, which the form would otherwise consume for focus navigation.
        /// </summary>
        protected override bool IsInputKey(Keys keyData) => keyData switch
        {
            Keys.Up or Keys.Down or Keys.Left or Keys.Right => true,
            _ => base.IsInputKey(keyData),
        };

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);

            if (Source == null)
            {
                return;
            }

            var zoom = Math.Max(1, Zoom);
            var width = Source.Width * zoom;
            var height = Source.Height * zoom;
            var originX = -PanX * zoom;
            var originY = -PanY * zoom;

            if (Checkered)
            {
                DrawCheckerboard(g, originX, originY, width, height);
            }

            // NearestNeighbor keeps pixels square; Half fixes the half-pixel offset GDI+ applies
            // to scaled images, which otherwise shifts the whole canvas and clips its last row.
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.SmoothingMode = SmoothingMode.None;
            g.DrawImage(Source, originX, originY, width, height);

            DrawGrid(g, originX, originY, zoom);
        }

        private static void DrawCheckerboard(Graphics g, int originX, int originY, int width, int height)
        {
            const int Cell = 8;
            using var light = new SolidBrush(Color.FromArgb(255, 220, 220, 220));
            using var dark = new SolidBrush(Color.FromArgb(255, 180, 180, 180));

            g.FillRectangle(light, originX, originY, width, height);

            for (var y = 0; y < height; y += Cell)
            {
                for (var x = (y / Cell % 2 == 0) ? Cell : 0; x < width; x += Cell * 2)
                {
                    g.FillRectangle(dark, originX + x, originY + y, Math.Min(Cell, width - x), Math.Min(Cell, height - y));
                }
            }
        }

        /// <summary>
        /// Draws one line per image pixel boundary.
        /// </summary>
        /// <remarks>
        /// Skipped entirely below a zoom of 4: at that size the lines occupy as much of the canvas
        /// as the pixels do, and the image becomes unreadable rather than easier to read.
        /// </remarks>
        private void DrawGrid(Graphics g, int originX, int originY, int zoom)
        {
            if (Source == null || zoom < 4 || (GridArgb >> 24 & 0xFF) == 0)
            {
                return;
            }

            using var pen = new Pen(Color.FromArgb(GridArgb));

            var right = originX + (Source.Width * zoom);
            var bottom = originY + (Source.Height * zoom);

            for (var x = 0; x <= Source.Width; x++)
            {
                var sx = originX + (x * zoom);
                g.DrawLine(pen, sx, originY, sx, bottom);
            }

            for (var y = 0; y <= Source.Height; y++)
            {
                var sy = originY + (y * zoom);
                g.DrawLine(pen, originX, sy, right, sy);
            }
        }
    }
}
