using System.Diagnostics;
using System.Net;
using System.Text;

namespace ProLang.Mips;

sealed record Frame(int Number, int Lines, int Rects, int Texts, uint Geometry, double Milliseconds, long Steps, long Guest, long Bridges, long Allocated);

// Deterministic headless PSP boundary: no vblank, GPU, sleeping or real controller.
// Geometry hashes exactly follow runtime/psp-v/host.h. Optional SVG is outside benchmark runs.
sealed class Host(int limit)
{
    public readonly List<Frame> Frames = [];
    // The toolkit reads the prior frame's controller state before entering its loop.
    public int Buttons => Frames.Count >= limit - 1 ? 8 : 0;
    public long LastSteps;
    public bool Capture;
    public string FirstSvg = "", LastSvg = "";
    long lastTick, lastGuest, lastBridges, lastAllocated;
    int lines, rects, texts, background;
    uint geometry = 2166136261;
    readonly StringBuilder svg = new();
    static string Colour(int c) => $"#{c & 0xffffff:x6}";
    public void Start() { lastTick = Stopwatch.GetTimestamp(); lastAllocated = GC.GetTotalAllocatedBytes(false); }
    public void Clear(int colour) { background = colour; svg.Clear(); }
    public void Rect(int x, int y, int w, int h, int c)
    {
        rects++;
        if (Capture) svg.Append($"<rect x='{x}' y='{y}' width='{Math.Max(0, w)}' height='{Math.Max(0, h)}' fill='{Colour(c)}'/>");
    }
    public void Line(int x, int y, int xx, int yy, int c)
    {
        lines++;
        geometry = unchecked((geometry ^ (uint)x) * 16777619);
        geometry = unchecked((geometry ^ (uint)y) * 16777619);
        geometry = unchecked((geometry ^ (uint)xx) * 16777619);
        geometry = unchecked((geometry ^ (uint)yy) * 16777619);
        if (Capture) svg.Append($"<path d='M{x} {y}L{xx} {yy}' stroke='{Colour(c)}' fill='none'/>");
    }
    public void Text(int x, int y, string text, int c)
    {
        texts++;
        if (Capture) svg.Append($"<text x='{x}' y='{y + 8}' fill='{Colour(c)}' font-family='monospace' font-size='10'>{WebUtility.HtmlEncode(text)}</text>");
    }
    public void Present(Vm vm)
    {
        long now = Stopwatch.GetTimestamp(), allocated = GC.GetTotalAllocatedBytes(false);
        Frames.Add(new(Frames.Count + 1, lines, rects, texts, geometry, (now - lastTick) * 1000d / Stopwatch.Frequency,
            vm.Steps - LastSteps, vm.GuestInstructions - lastGuest, vm.Bridges - lastBridges, allocated - lastAllocated));
        if (Capture)
        {
            LastSvg = $"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 480 272'><rect width='480' height='272' fill='{Colour(background)}'/>{svg}</svg>";
            if (Frames.Count == 1) FirstSvg = LastSvg;
        }
        lines = rects = texts = 0; geometry = 2166136261;
        LastSteps = vm.Steps; lastGuest = vm.GuestInstructions; lastBridges = vm.Bridges;
        lastAllocated = GC.GetTotalAllocatedBytes(false); lastTick = Stopwatch.GetTimestamp();
    }
}
