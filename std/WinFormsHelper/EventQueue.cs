namespace WinFormsHelper;

/// <summary>What happened. Mirrored as integer constants in prolang.</summary>
/// <remarks>
/// Values are part of the contract with prolang programs, which compare against plain integers,
/// so they are assigned explicitly and must not be reordered.
/// </remarks>
public enum PrlEventKind
{
    /// <summary>Nothing was queued. The event's other fields are meaningless.</summary>
    None = 0,
    MouseDown = 1,
    MouseMove = 2,
    MouseUp = 3,
    Wheel = 4,
    KeyDown = 5,
    KeyUp = 6,
    MouseLeave = 7,
    Resize = 8,

    /// <summary>The window was closed. The program should stop its loop.</summary>
    Quit = 9,

    /// <summary>A control with a registered tag was activated — a toolbar or palette button.</summary>
    Command = 10,
}

/// <summary>One input event, flattened to integers so prolang can read it.</summary>
/// <param name="Kind">Which kind of event this is.</param>
/// <param name="X">Position within the source control, in pixels.</param>
/// <param name="Y">Position within the source control, in pixels.</param>
/// <param name="Button">Bit flags: 1 left, 2 right, 4 middle.</param>
/// <param name="Key">Virtual key code, as <c>System.Windows.Forms.Keys</c>.</param>
/// <param name="Delta">Wheel movement in notches; positive is away from the user.</param>
/// <param name="Modifiers">Bit flags: 1 shift, 2 control, 4 alt.</param>
/// <param name="Tag">The tag registered for a <see cref="PrlEventKind.Command"/> source.</param>
/// <param name="Source">Handle of the control that raised it.</param>
public readonly record struct PrlEvent(
    PrlEventKind Kind,
    int X = 0,
    int Y = 0,
    int Button = 0,
    int Key = 0,
    int Delta = 0,
    int Modifiers = 0,
    int Tag = 0,
    int Source = 0);

/// <summary>
/// A bounded FIFO of input events that collapses redundant mouse movement.
/// </summary>
/// <remarks>
/// <para>
/// Windows Forms raises <c>MouseMove</c> far faster than a prolang program polls — the program
/// does real work per event, and each poll crosses the interop boundary. Queued verbatim, the
/// backlog grows without bound and the brush trails the cursor by an interval that only ever
/// increases: the program spends its time replaying positions the mouse left long ago.
/// </para>
/// <para>
/// So a movement arriving while a movement is already at the tail <b>replaces</b> it. Only the
/// latest position matters, and drawing interpolates between the previous committed point and the
/// current one anyway, so nothing is lost by discarding the intermediate ones. Button, key and
/// command events are never collapsed — each is a discrete thing that happened, and dropping one
/// loses a click.
/// </para>
/// <para>
/// This type deliberately holds no reference to <c>Control</c> or anything else in Windows Forms,
/// so it can be tested on an ordinary thread. Anything touching a real control needs an STA
/// thread, and xUnit's default is MTA.
/// </para>
/// </remarks>
public sealed class EventQueue
{
    private readonly PrlEvent[] _items;
    private int _head;
    private int _count;

    /// <summary>The default bound: large enough that only a stalled consumer ever reaches it.</summary>
    public const int DefaultCapacity = 4096;

    /// <summary>Creates a queue holding at most <paramref name="capacity"/> events.</summary>
    public EventQueue(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _items = new PrlEvent[capacity];
    }

    /// <summary>How many events are waiting.</summary>
    public int Count => _count;

    /// <summary>The most events that can be held at once.</summary>
    public int Capacity => _items.Length;

    /// <summary>
    /// Adds an event, collapsing it into the one at the tail when both are movement of the same
    /// control.
    /// </summary>
    /// <returns>
    /// True if the event was stored or coalesced; false if the queue was full of events that may
    /// not be dropped and this one was discarded.
    /// </returns>
    public bool Enqueue(PrlEvent e)
    {
        if (e.Kind == PrlEventKind.MouseMove && TryCoalesce(e))
        {
            return true;
        }

        if (_count == _items.Length && !TryEvict())
        {
            return false;
        }

        _items[(_head + _count) % _items.Length] = e;
        _count++;
        return true;
    }

    /// <summary>Removes and returns the oldest event.</summary>
    /// <param name="e">The event, or a <see cref="PrlEventKind.None"/> event when empty.</param>
    /// <returns>False when the queue is empty.</returns>
    public bool TryDequeue(out PrlEvent e)
    {
        if (_count == 0)
        {
            e = new PrlEvent(PrlEventKind.None);
            return false;
        }

        e = _items[_head];
        _head = (_head + 1) % _items.Length;
        _count--;
        return true;
    }

    /// <summary>Discards everything queued.</summary>
    public void Clear()
    {
        _head = 0;
        _count = 0;
    }

    /// <summary>
    /// Overwrites a trailing movement event from the same control with a newer one.
    /// </summary>
    private bool TryCoalesce(PrlEvent e)
    {
        if (_count == 0)
        {
            return false;
        }

        var tailIndex = (_head + _count - 1) % _items.Length;
        var tail = _items[tailIndex];

        if (tail.Kind != PrlEventKind.MouseMove || tail.Source != e.Source)
        {
            return false;
        }

        _items[tailIndex] = e;
        return true;
    }

    /// <summary>
    /// Makes room by discarding the oldest movement event.
    /// </summary>
    /// <remarks>
    /// Movement is the only kind that is safe to lose: a later one supersedes it. A dropped
    /// button, key or command event is an action the user took that never happens, so when the
    /// queue holds nothing but those, the new event is refused instead.
    /// </remarks>
    private bool TryEvict()
    {
        for (var i = 0; i < _count; i++)
        {
            var index = (_head + i) % _items.Length;

            if (_items[index].Kind != PrlEventKind.MouseMove)
            {
                continue;
            }

            // Close the gap by shifting the older half forward, so ordering is preserved.
            for (var j = i; j > 0; j--)
            {
                var to = (_head + j) % _items.Length;
                var from = (_head + j - 1) % _items.Length;
                _items[to] = _items[from];
            }

            _head = (_head + 1) % _items.Length;
            _count--;
            return true;
        }

        return false;
    }
}
