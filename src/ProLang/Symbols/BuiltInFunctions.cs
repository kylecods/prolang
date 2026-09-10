using System.Collections.Immutable;
using ProLang.Symbols.Modules;

namespace ProLang.Symbols;

internal static class BuiltInFunctions
{
    public static readonly FunctionSymbol Print = new("print",
        ImmutableArray.Create(new ParameterSymbol("text", TypeSymbol.Any, 0)), TypeSymbol.Void)
    {
        Documentation =
            "Writes a value and a newline to standard output.\n\n" +
            "Buffered: the text is collected and written when `main()` returns, so a program's " +
            "output appears as one block. A failing `assert()` flushes the buffer first, so the " +
            "output leading up to a failure is never lost, and `console_write()` flushes it too " +
            "so that mixing the two keeps the order they were written in.",
    };

    public static readonly FunctionSymbol ReadInput = new("readInput", ImmutableArray<ParameterSymbol>.Empty,
        TypeSymbol.String)
    {
        Documentation =
            "Reads one line from standard input, without its line break.\n\n" +
            "Returns the empty string at end of input, so a read loop ends rather than blocking.",
    };

    public static readonly FunctionSymbol Random = new("random",
        ImmutableArray.Create(new ParameterSymbol("max", TypeSymbol.Int,0)), TypeSymbol.Int)
    {
        Documentation =
            "A random integer from 0 up to but not including `max`.\n\n" +
            "Not seeded and not reproducible: a program that must repeat a sequence needs its " +
            "own generator.",
    };

    public static readonly FunctionSymbol Min = new("min",
        ImmutableArray.Create(new ParameterSymbol("a", TypeSymbol.Int,0), new ParameterSymbol("b", TypeSymbol.Int,1)),
        TypeSymbol.Int)
    {
        Documentation = "The smaller of two integers.",
    };

    public static readonly FunctionSymbol Max = new("max",
        ImmutableArray.Create(new ParameterSymbol("a", TypeSymbol.Int, 0), new ParameterSymbol("b", TypeSymbol.Int,1)),
        TypeSymbol.Int)
    {
        Documentation = "The larger of two integers.",
    };

    public static readonly FunctionSymbol FileExists = new("fileExists", ImmutableArray.Create(new ParameterSymbol("path",TypeSymbol.String,0)),TypeSymbol.Bool)
    {
        Documentation = "Whether a file exists at `path`. False for a directory, and for a path that cannot be read.",
    };

    public static readonly FunctionSymbol ReadFile = new("readFile", ImmutableArray.Create(new ParameterSymbol("path",TypeSymbol.String,0)),TypeSymbol.String)
    {
        Documentation =
            "The whole contents of a text file, as one string.\n\n" +
            "Ends the program if the file does not exist — check with `fileExists()` first. Use " +
            "`readFileBytes()` for anything that is not text.",
    };

    public static readonly FunctionSymbol ReadFileBytes = new("readFileBytes",
        ImmutableArray.Create(new ParameterSymbol("path", TypeSymbol.String, 0)),
        TypeSymbol.Array.WithArgs(TypeSymbol.UInt8))
    {
        Documentation =
            "The whole contents of a file, as an `array<uint8>`.\n\n" +
            "For binary data — an image, a ROM, a font — where reading as text would corrupt it.",
    };

    public static readonly FunctionSymbol WriteFile = new("writeFile",
        [new ParameterSymbol("path", TypeSymbol.String,0), new ParameterSymbol("contents", TypeSymbol.String,0)],
        TypeSymbol.Void)
    {
        Documentation = "Writes `contents` to `path`, replacing the file if it already exists.",
    };

    public static readonly FunctionSymbol ArrayLength = new("length",
        ImmutableArray.Create(new ParameterSymbol("arr", TypeSymbol.Any, 0)),
        TypeSymbol.Int)
    {
        Documentation =
            "The number of elements in an array.\n\n" +
            "Written as a method: `arr.length()`. It is the only method an array has — arrays are " +
            "fixed-length, so there is no push or pop. See `std/dynarray.prl` for one that grows.",
    };

    public static readonly FunctionSymbol ArrayNew = new("array_new",
        ImmutableArray.Create(new ParameterSymbol("size", TypeSymbol.Int, 0)),
        TypeSymbol.Array)
    {
        Documentation =
            "A new array of `size` elements, every one zero, false or empty.\n\n" +
            "The element type comes from the variable it is assigned to: " +
            "`let cells: array<int> = array_new(64)`. The length is fixed once created.",
    };

    // String methods
    public static readonly FunctionSymbol StringLength = new("length",
        ImmutableArray.Create(new ParameterSymbol("str", TypeSymbol.String, 0)),
        TypeSymbol.Int)
    {
        Documentation =
            "The number of characters in a string, written as `s.length()`.\n\n" +
            "Counts UTF-16 code units, matching `charAt()` and `charCode()`, so a character " +
            "outside the basic multilingual plane counts as two.",
    };

    public static readonly FunctionSymbol StringCharAt = new("charAt",
        ImmutableArray.Create(
            new ParameterSymbol("str", TypeSymbol.String, 0),
            new ParameterSymbol("index", TypeSymbol.Int, 1)),
        TypeSymbol.Char)
    {
        Documentation =
            "The character at `index`, as a `char`.\n\n" +
            "`'a' == s.charAt(0)` compares directly, and `c - '0'` does arithmetic on it. " +
            "Use `string(c)` to get one-character text back. Ends the program if `index` is " +
            "outside the string.",
    };

    public static readonly FunctionSymbol StringCharCode = new("charCode",
        ImmutableArray.Create(
            new ParameterSymbol("str", TypeSymbol.String, 0),
            new ParameterSymbol("index", TypeSymbol.Int, 1)),
        TypeSymbol.Int)
    {
        Documentation =
            "The character at `index`, as its numeric code.\n\n" +
            "Kept for code written before the char type: a `char` is now an int implicitly, so " +
            "`s.charAt(i) - '0'` does the same without it. `\"a\".charCode(0)` is 97. Ends the " +
            "program if `index` is outside the string.",
    };

    public static readonly FunctionSymbol StringSubstring = new("substring",
        ImmutableArray.Create(
            new ParameterSymbol("str", TypeSymbol.String, 0),
            new ParameterSymbol("start", TypeSymbol.Int, 1),
            new ParameterSymbol("end", TypeSymbol.Int, 2)),
        TypeSymbol.String)
    {
        Documentation =
            "The text from `start` up to but **not** including `end`.\n\n" +
            "`end` is an index, not a length — `\"hello\".substring(1, 3)` is `\"el\"`. Ends the " +
            "program if the range falls outside the string or `end` precedes `start`.",
    };

    public static readonly FunctionSymbol StringIndexOf = new("indexOf",
        ImmutableArray.Create(
            new ParameterSymbol("str", TypeSymbol.String, 0),
            new ParameterSymbol("needle", TypeSymbol.String, 1)),
        TypeSymbol.Int)
    {
        Documentation =
            "The index where `needle` first appears, or -1 if it does not.\n\n" +
            "The empty string is found at 0.",
    };

    // Console and threading functions
    public static readonly FunctionSymbol ConsoleWrite = new("console_write",
        ImmutableArray.Create(new ParameterSymbol("text", TypeSymbol.String, 0)), TypeSymbol.Void)
    {
        Documentation =
            "Writes text to the terminal immediately, with no trailing newline.\n\n" +
            "Unbuffered, unlike `print()` — which is what makes it usable for a game loop that " +
            "redraws a frame. It flushes `print()`'s buffer first so that mixing the two keeps " +
            "the order they were written in.",
    };

    public static readonly FunctionSymbol ConsoleSetCursor = new("console_set_cursor",
        ImmutableArray.Create(
            new ParameterSymbol("x", TypeSymbol.Int, 0),
            new ParameterSymbol("y", TypeSymbol.Int, 1)),
        TypeSymbol.Void)
    {
        Documentation =
            "Moves the cursor to a column and row, counted from 0 at the top left.\n\n" +
            "Does nothing when output is redirected, so a program that draws a screen still runs " +
            "correctly into a pipe or a file instead of crashing.",
    };

    public static readonly FunctionSymbol ConsoleHideCursor = new("console_hide_cursor",
        ImmutableArray<ParameterSymbol>.Empty, TypeSymbol.Void)
    {
        Documentation =
            "Hides the blinking cursor, so it does not flicker across a redrawn screen.\n\n" +
            "Does nothing when output is redirected.",
    };

    public static readonly FunctionSymbol ConsoleSetColor = new("console_set_color",
        ImmutableArray.Create(new ParameterSymbol("color", TypeSymbol.Int, 0)), TypeSymbol.Void)
    {
        Documentation =
            "Sets the foreground colour for everything written after it.\n\n" +
            "0-15, in the terminal's own palette: 0 black, 7 grey, 9 blue, 10 green, 12 red, " +
            "15 white. Call `console_reset_color()` when finished. Does nothing when output is " +
            "redirected.",
    };

    public static readonly FunctionSymbol ConsoleResetColor = new("console_reset_color",
        ImmutableArray<ParameterSymbol>.Empty, TypeSymbol.Void)
    {
        Documentation =
            "Restores the terminal's default colours.\n\n" +
            "Worth calling before a program ends: a colour left set outlives the program and " +
            "tints the user's shell.",
    };

    public static readonly FunctionSymbol ConsoleKeyAvailable = new("console_key_available",
        ImmutableArray<ParameterSymbol>.Empty, TypeSymbol.Bool)
    {
        Documentation =
            "Whether a keypress is waiting to be read.\n\n" +
            "Test this before `console_read_key()` to poll for input without blocking, which is " +
            "what lets a game loop keep running when nobody is pressing anything. Always false " +
            "when input is redirected, so such a loop does not spin forever non-interactively.",
    };

    public static readonly FunctionSymbol ConsoleReadKey = new("console_read_key",
        ImmutableArray<ParameterSymbol>.Empty, TypeSymbol.Int)
    {
        Documentation =
            "Reads one keypress without echoing it, as a key code.\n\n" +
            "Blocks until a key is pressed unless `console_key_available()` says one is ready. " +
            "Arrow keys are 37 left, 38 up, 39 right, 40 down; letters are their capital's code. " +
            "Returns -1 when input is redirected.",
    };

    public static readonly FunctionSymbol ThreadSleep = new("thread_sleep",
        ImmutableArray.Create(new ParameterSymbol("ms", TypeSymbol.Int, 0)), TypeSymbol.Void)
    {
        Documentation =
            "Pauses the program for roughly `ms` milliseconds.\n\n" +
            "Roughly: the scheduler decides when the program runs again, so it may sleep longer. " +
            "Pair it with `time_millis()` to hold a frame rate rather than assuming it slept " +
            "exactly as long as asked.",
    };

    /// <summary>
    /// Milliseconds since the program started, from a monotonic clock.
    /// </summary>
    /// <remarks>
    /// The companion of <see cref="ThreadSleep"/>, which is why it sits in the same module: those
    /// two are the whole of the language's relationship with time. Monotonic rather than a wall
    /// clock, so an interval can never come out negative, and 32-bit like every other ProLang
    /// <c>int</c> — it wraps after about twenty-five days, which subtracting two readings survives.
    /// </remarks>
    public static readonly FunctionSymbol TimeMillis = new("time_millis",
        ImmutableArray<ParameterSymbol>.Empty, TypeSymbol.Int)
    {
        Documentation =
            "Milliseconds from an arbitrary origin, from a monotonic clock.\n\n" +
            "**Always subtract two readings**; never compare them. The origin is arbitrary, so " +
            "only differences mean anything, and the value is a 32-bit `int` like everything else " +
            "in ProLang — it wraps after about twenty-five days. Subtraction gives the right " +
            "interval straight through a wrap; `if (now > then)` does not.\n\n" +
            "Monotonic rather than a wall clock, so an interval can never come out negative " +
            "because the system time was adjusted underneath it.",
    };

    /// <summary>
    /// Fails the program when <c>condition</c> is false. Exposed by <c>import "test"</c>.
    /// </summary>
    /// <remarks>
    /// ProLang has no exceptions and no way to set an exit code, so before this a test program
    /// could only print "passed" and return 0 whatever happened — which is what
    /// <c>examples/09-json-parser/json-parser-tests.prl</c> does. The execution tests assert on a
    /// zero exit code and empty stderr, so an assert that throws is what lets a ProLang test suite
    /// actually fail.
    /// </remarks>
    public static readonly FunctionSymbol Assert = new("assert",
        ImmutableArray.Create(
            new ParameterSymbol("condition", TypeSymbol.Bool, 0),
            new ParameterSymbol("message", TypeSymbol.String, 1)),
        TypeSymbol.Void)
    {
        Documentation =
            "Ends the program with a non-zero exit code and `message` on stderr, if `condition` " +
            "is false.\n\n" +
            "This is what makes a test suite written in ProLang a real gate: the language has no " +
            "exceptions and no other way to set an exit code, so without it a failing test could " +
            "only print that it failed and still exit 0.\n\n" +
            "It flushes `print()`'s buffer first, so everything the program printed before the " +
            "failure is still shown.",
    };

    // PSP graphics / input
    //
    // These compile only under --emit-psp, where they become calls into the PSP SDK. A program
    // that imports "psp" and is built for .NET will bind but has nothing to run against, which is
    // why each of these says so.
    public static readonly FunctionSymbol PspInit = new("psp_init",
        ImmutableArray<ParameterSymbol>.Empty, TypeSymbol.Void)
    {
        Documentation =
            "Sets up the PSP's graphics hardware and display list. Call once, before drawing " +
            "anything.\n\nPSP only — build with `--emit-psp`.",
    };

    public static readonly FunctionSymbol PspClear = new("psp_clear",
        ImmutableArray.Create(new ParameterSymbol("color", TypeSymbol.Int, 0)), TypeSymbol.Void)
    {
        Documentation =
            "Fills the whole frame with one colour, starting a frame.\n\n" +
            "`color` is packed ARGB — see `ui/color`. PSP only.",
    };

    public static readonly FunctionSymbol PspFillRect = new("psp_fill_rect",
        ImmutableArray.Create(
            new ParameterSymbol("x", TypeSymbol.Int, 0),
            new ParameterSymbol("y", TypeSymbol.Int, 1),
            new ParameterSymbol("w", TypeSymbol.Int, 2),
            new ParameterSymbol("h", TypeSymbol.Int, 3),
            new ParameterSymbol("color", TypeSymbol.Int, 4)),
        TypeSymbol.Void)
    {
        Documentation =
            "Fills a rectangle `w` by `h` with its top left corner at `x`, `y`.\n\n" +
            "The screen is 480 by 272. PSP only.",
    };

    public static readonly FunctionSymbol PspDrawText = new("psp_draw_text",
        ImmutableArray.Create(
            new ParameterSymbol("x", TypeSymbol.Int, 0),
            new ParameterSymbol("y", TypeSymbol.Int, 1),
            new ParameterSymbol("text", TypeSymbol.String, 2),
            new ParameterSymbol("color", TypeSymbol.Int, 3)),
        TypeSymbol.Void)
    {
        Documentation =
            "Draws text in the built-in debug font, with its top left corner at `x`, `y`.\n\n" +
            "One fixed size, 8 pixels wide per character. PSP only.",
    };

    public static readonly FunctionSymbol PspSwapBuffers = new("psp_swap_buffers",
        ImmutableArray<ParameterSymbol>.Empty, TypeSymbol.Void)
    {
        Documentation =
            "Shows the frame that has just been drawn and starts drawing into the other buffer.\n\n" +
            "Call `psp_vsync()` first, or the swap can happen mid-scan and tear. PSP only.",
    };

    public static readonly FunctionSymbol PspVsync = new("psp_vsync",
        ImmutableArray<ParameterSymbol>.Empty, TypeSymbol.Void)
    {
        Documentation =
            "Waits for the display to finish its current scan.\n\n" +
            "This is what paces a game loop to 60 frames a second, and what stops a frame being " +
            "swapped in halfway down the screen. PSP only.",
    };

    public static readonly FunctionSymbol PspButtonsHeld = new("psp_buttons_held",
        ImmutableArray<ParameterSymbol>.Empty, TypeSymbol.Int)
    {
        Documentation =
            "Every button currently held down, as one integer of bit flags.\n\n" +
            "Test one with `psp_button_pressed()` rather than unpacking it by hand. PSP only.",
    };

    public static readonly FunctionSymbol PspButtonPressed = new("psp_button_pressed",
        ImmutableArray.Create(new ParameterSymbol("button", TypeSymbol.Int, 0)), TypeSymbol.Bool)
    {
        Documentation =
            "Whether one button is currently held down.\n\n" +
            "`button` is a single bit flag. This reports what is held *now*, not what was newly " +
            "pressed this frame — for that, compare against the previous frame's reading. PSP only.",
    };

    public static readonly FunctionSymbol PspDrawLine = new("psp_draw_line",
        ImmutableArray.Create(
            new ParameterSymbol("x1", TypeSymbol.Int, 0),
            new ParameterSymbol("y1", TypeSymbol.Int, 1),
            new ParameterSymbol("x2", TypeSymbol.Int, 2),
            new ParameterSymbol("y2", TypeSymbol.Int, 3),
            new ParameterSymbol("color", TypeSymbol.Int, 4)),
        TypeSymbol.Void)
    {
        Documentation = "Draws a one-pixel line between two points. PSP only.",
    };

    internal static IEnumerable<FunctionSymbol> GetAll() => BuiltInModule.GetAllFunctions();
}
