using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinFormsHelper;

/// <summary>
/// The Windows Forms end of the UI toolkit's display list.
/// </summary>
/// <remarks>
/// <para>
/// A ProLang program builds a widget tree, lays it out, and flattens the result into a flat
/// <c>int[]</c> of fixed-width drawing records. This is the code that executes that buffer. It
/// knows nothing about columns, buttons or layout — only about rectangles, text and images.
/// </para>
/// <para>
/// The whole frame crosses the boundary in <b>one</b> call. A ProLang <c>array&lt;int&gt;</c> is a
/// real CLR <c>int[]</c>, so the buffer arrives without marshalling; the alternative, one interop
/// call per primitive, would be hundreds per frame and would put Windows Forms back into the middle
/// of the toolkit.
/// </para>
/// <para>
/// The record layout is fixed by <c>std/ui/draw.prl</c> and the two must agree. It is one opcode
/// plus six operands, the same width whatever the opcode, so the reader can step by a constant.
/// </para>
/// </remarks>
public static partial class WinFormsHelper
{
    /// <summary>Ints per display-list record. Must equal <c>dl_stride()</c> in ui/draw.prl.</summary>
    public const int SurfaceOpStride = 7;

    /// <summary>Opcodes, matching the <c>DlOp</c> enum in ui/draw.prl.</summary>
    private const int OpEnd = 0;
    private const int OpRect = 1;
    private const int OpRoundRect = 2;
    private const int OpText = 3;
    private const int OpLine = 4;
    private const int OpImage = 5;
    private const int OpClipPush = 6;
    private const int OpClipPop = 7;

    /// <summary>How many character codes the metrics table carries. Matches <c>font_glyphs()</c>.</summary>
    public const int SurfaceFontGlyphs = 128;

    private static readonly List<Font> _surfaceFonts = [];

    /// <summary>
    /// Registers a font and returns the index the display list refers to it by.
    /// </summary>
    /// <remarks>
    /// The toolkit passes a small integer in every text record rather than a font name, so the
    /// mapping has to live somewhere. Here, because this is the side that owns the
    /// <see cref="Font"/> objects and has to dispose of nothing until the process ends.
    /// </remarks>
    public static int SurfaceAddFont(string family, int pointSize, bool bold)
    {
        var style = bold ? FontStyle.Bold : FontStyle.Regular;

        _surfaceFonts.Add(new Font(family, pointSize, style, GraphicsUnit.Point));

        return _surfaceFonts.Count - 1;
    }

    /// <summary>
    /// Fills in a font's per-character advances and its line metrics.
    /// </summary>
    /// <param name="fontIndex">Index from <see cref="SurfaceAddFont"/>.</param>
    /// <param name="widths">Destination for <see cref="SurfaceFontGlyphs"/> advances, from <paramref name="widthsOffset"/>.</param>
    /// <param name="widthsOffset">Where this font's block starts — the ProLang table is one flat buffer for every font.</param>
    /// <param name="metrics">Destination for the line height and ascent.</param>
    /// <param name="metricsOffset">Where this font's pair starts.</param>
    /// <remarks>
    /// <para>
    /// Called once, at start-up. Its whole purpose is to let the layout pass stay pure arithmetic:
    /// with this table in hand ProLang can measure a string itself and never has to ask a window
    /// how wide its text is.
    /// </para>
    /// <para>
    /// <see cref="TextFormatFlags.NoPadding"/> matters and is used both here and when drawing.
    /// Without it <see cref="TextRenderer"/> adds a few pixels of bearing to each measurement, and
    /// since the toolkit measures characters individually and adds them up, that padding would be
    /// counted once per character — a ten-character label would measure far too wide.
    /// </para>
    /// </remarks>
    public static void SurfaceMeasureFont(int fontIndex, int[] widths, int widthsOffset,
        int[] metrics, int metricsOffset)
    {
        if (fontIndex < 0 || fontIndex >= _surfaceFonts.Count)
        {
            return;
        }

        var font = _surfaceFonts[fontIndex];

        if (widths.Length < widthsOffset + SurfaceFontGlyphs || metrics.Length < metricsOffset + 2)
        {
            return;
        }

        for (var code = 0; code < SurfaceFontGlyphs; code++)
        {
            // Control characters have no advance; measuring them returns whatever the font's
            // "missing glyph" box happens to be, which would make a tab as wide as a letter.
            if (code < 32)
            {
                widths[widthsOffset + code] = 0;
                continue;
            }

            var text = ((char)code).ToString();
            var size = TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

            widths[widthsOffset + code] = size.Width;
        }

        // Ascent comes back from the family in design units, which are only meaningful against
        // that family's line spacing. Scaling by the ratio of the two converts to the pixels the
        // font is actually rendered at, whatever point size it was created with.
        var family = font.FontFamily;
        var lineSpacing = family.GetLineSpacing(font.Style);

        metrics[metricsOffset] = font.Height;
        metrics[metricsOffset + 1] = lineSpacing == 0
            ? font.Height
            : (int)Math.Round(family.GetCellAscent(font.Style) * (float)font.Height / lineSpacing);
    }

    /// <summary>Creates a surface control — the thing a display list is painted onto.</summary>
    public static int CreateSurface(int x, int y, int width, int height)
    {
        var surface = new SurfaceControl
        {
            Left = x,
            Top = y,
            Width = width,
            Height = height,
        };

        return Register(surface);
    }

    /// <summary>
    /// Hands a surface the frame to paint and asks for a repaint.
    /// </summary>
    /// <param name="surfaceId">Handle from <see cref="CreateSurface"/>.</param>
    /// <param name="ops">The display list. Only the first <paramref name="opCount"/> records are read.</param>
    /// <param name="opCount">Records written by <c>dl_build</c>.</param>
    /// <param name="strings">The string table text records index into.</param>
    /// <param name="stringCount">How many leading entries of <paramref name="strings"/> are live.</param>
    /// <remarks>
    /// The buffers are <b>copied</b> rather than kept by reference. They are the caller's live
    /// arena, and it will have overwritten them building the next frame long before Windows gets
    /// round to delivering the paint message — the visible result of sharing them would be a frame
    /// torn between two states. A frame is a few hundred ints; the copy costs nothing worth saving.
    /// </remarks>
    public static void SurfaceSubmit(int surfaceId, int[] ops, int opCount, string[] strings, int stringCount)
    {
        var surface = Get<SurfaceControl>(surfaceId);

        surface.Accept(ops, opCount, strings, stringCount);
        surface.Invalidate();
    }

    /// <summary>
    /// Renders the surface's current display list to a PNG, with no window involved.
    /// </summary>
    /// <returns><see langword="true"/> if the file was written.</returns>
    /// <remarks>
    /// The frame a program produced then becomes something that can be looked at directly, rather
    /// than only through a screenshot of a live window — which brings the compositor, the display's
    /// colour profile and whatever is on top of the window into a picture that is supposed to be
    /// about the toolkit. It is also what a golden-image test would use.
    /// </remarks>
    public static bool SurfaceSaveFrame(int surfaceId, string path)
    {
        var surface = Get<SurfaceControl>(surfaceId);

        var width = Math.Max(1, surface.Width);
        var height = Math.Max(1, surface.Height);

        try
        {
            using var bitmap = new Bitmap(width, height, CanvasFormat);
            using (var g = Graphics.FromImage(bitmap))
            {
                surface.Render(g);
            }

            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            return true;
        }
        catch (Exception)
        {
            // A ProLang program has no exception handling, so a failed write is a false return
            // rather than something that would take the program down with it.
            return false;
        }
    }

    /// <summary>
    /// A control that paints one display list and nothing else.
    /// </summary>
    private sealed class SurfaceControl : Control
    {
        // Fields rather than properties, for the reason CanvasControl gives: a public property on
        // a Control is designer state and has to declare how it serialises. Nothing here is ever
        // seen by a designer.
        private int[] _ops = [];
        private int _opCount;
        private string[] _strings = [];
        private int _stringCount;

        /// <summary>Brushes kept by colour, since a frame reuses a handful across hundreds of records.</summary>
        private readonly Dictionary<int, SolidBrush> _brushes = [];

        public SurfaceControl()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.Selectable
                | ControlStyles.ResizeRedraw,
                true);

            TabStop = true;
        }

        /// <summary>Takes a copy of the frame to paint.</summary>
        internal void Accept(int[] ops, int opCount, string[] strings, int stringCount)
        {
            var needed = Math.Max(0, opCount) * SurfaceOpStride;

            if (needed > ops.Length)
            {
                needed = ops.Length;
                opCount = needed / SurfaceOpStride;
            }

            if (_ops.Length < needed)
            {
                _ops = new int[needed];
            }

            Array.Copy(ops, _ops, needed);
            _opCount = opCount;

            var strandsNeeded = Math.Clamp(stringCount, 0, strings.Length);

            if (_strings.Length < strandsNeeded)
            {
                _strings = new string[strandsNeeded];
            }

            Array.Copy(strings, _strings, strandsNeeded);
            _stringCount = strandsNeeded;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            base.OnMouseDown(e);
        }

        /// <summary>Claims the arrow keys, which the form would otherwise use for focus navigation.</summary>
        protected override bool IsInputKey(Keys keyData) => keyData switch
        {
            Keys.Up or Keys.Down or Keys.Left or Keys.Right => true,
            _ => base.IsInputKey(keyData),
        };

        protected override void OnPaint(PaintEventArgs e) => Render(e.Graphics);

        /// <summary>
        /// Executes the current display list onto any surface.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="OnPaint"/> so the same code can render into a bitmap with no
        /// window involved — see <see cref="WinFormsHelper.SurfaceSaveFrame"/>. That is what makes
        /// a frame something a test can look at, and it is how a rendering problem gets separated
        /// from a screen-capture one.
        /// </remarks>
        internal void Render(Graphics g)
        {
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // A stack, because CLIP_PUSH nests. Saving the whole Graphics state rather than just
            // the clip means a backend change that touches transforms later cannot leak either.
            var clips = new Stack<Region>();

            for (var i = 0; i < _opCount; i++)
            {
                var at = i * SurfaceOpStride;

                if (at + SurfaceOpStride > _ops.Length)
                {
                    break;
                }

                var op = _ops[at];
                var a = _ops[at + 1];
                var b = _ops[at + 2];
                var c = _ops[at + 3];
                var d = _ops[at + 4];
                var f = _ops[at + 5];
                var h = _ops[at + 6];

                switch (op)
                {
                    case OpEnd:
                        i = _opCount;
                        break;

                    case OpRect:
                        g.FillRectangle(BrushFor(f), a, b, c, d);
                        break;

                    case OpRoundRect:
                        FillRoundedRectangle(g, BrushFor(f), a, b, c, d, h);
                        break;

                    case OpText:
                        DrawTextRecord(g, a, b, c, d, f);
                        break;

                    case OpLine:
                        using (var pen = new Pen(Color.FromArgb(f), Math.Max(1, h)))
                        {
                            g.DrawLine(pen, a, b, c, d);
                        }
                        break;

                    case OpImage:
                        if (_bitmaps.TryGetValue(f, out var bitmap))
                        {
                            g.DrawImage(bitmap, a, b, c, d);
                        }
                        break;

                    case OpClipPush:
                        clips.Push(g.Clip);
                        g.SetClip(new Rectangle(a, b, c, d), CombineMode.Intersect);
                        break;

                    case OpClipPop:
                        if (clips.Count > 0)
                        {
                            g.Clip = clips.Pop();
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// Draws one text record.
        /// </summary>
        /// <remarks>
        /// <see cref="TextRenderer"/> rather than <see cref="Graphics.DrawString"/>, and with the
        /// same flags <see cref="SurfaceMeasureFont"/> measured with. Measuring one way and drawing
        /// the other is how text ends up not fitting the box that was reserved for it.
        /// </remarks>
        private void DrawTextRecord(Graphics g, int x, int y, int stringIndex, int argb, int fontIndex)
        {
            if (stringIndex < 0 || stringIndex >= _stringCount)
            {
                return;
            }

            var text = _strings[stringIndex];

            if (string.IsNullOrEmpty(text) || fontIndex < 0 || fontIndex >= _surfaceFonts.Count)
            {
                return;
            }

            TextRenderer.DrawText(g, text, _surfaceFonts[fontIndex], new Point(x, y),
                Color.FromArgb(argb), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        private SolidBrush BrushFor(int argb)
        {
            if (_brushes.TryGetValue(argb, out var cached))
            {
                return cached;
            }

            var brush = new SolidBrush(Color.FromArgb(argb));
            _brushes[argb] = brush;

            return brush;
        }

        /// <summary>
        /// Fills a rectangle with rounded corners.
        /// </summary>
        /// <remarks>
        /// The radius is clamped to half the shorter side. A larger one makes the four arcs overlap,
        /// and GDI+ renders the result as a shape with pinched sides rather than refusing it.
        /// </remarks>
        private static void FillRoundedRectangle(Graphics g, Brush brush, int x, int y, int w, int h, int radius)
        {
            if (w <= 0 || h <= 0)
            {
                return;
            }

            radius = Math.Min(radius, Math.Min(w, h) / 2);

            if (radius <= 0)
            {
                g.FillRectangle(brush, x, y, w, h);
                return;
            }

            var diameter = radius * 2;

            using var path = new GraphicsPath();

            path.AddArc(x, y, diameter, diameter, 180, 90);
            path.AddArc(x + w - diameter, y, diameter, diameter, 270, 90);
            path.AddArc(x + w - diameter, y + h - diameter, diameter, diameter, 0, 90);
            path.AddArc(x, y + h - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();

            g.FillPath(brush, path);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var brush in _brushes.Values)
                {
                    brush.Dispose();
                }

                _brushes.Clear();
            }

            base.Dispose(disposing);
        }
    }
}
