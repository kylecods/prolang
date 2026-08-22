using WinFormsHelper;

namespace WinFormsHelperTests;

/// <summary>
/// Covers the input queue's ordering, coalescing and overflow rules.
/// </summary>
/// <remarks>
/// <see cref="EventQueue"/> holds no reference to <c>Control</c> precisely so that these can run
/// on an ordinary thread. Anything touching a real Windows Forms control would need an STA
/// thread, and xUnit's default apartment is MTA.
/// </remarks>
public sealed class EventQueueTests
{
    private static PrlEvent Move(int x, int y, int source = 1)
        => new(PrlEventKind.MouseMove, x, y, Source: source);

    private static PrlEvent Down(int x, int y, int source = 1)
        => new(PrlEventKind.MouseDown, x, y, Button: 1, Source: source);

    [Fact]
    public void EmptyQueue_DequeuesNothing()
    {
        var queue = new EventQueue();

        Assert.False(queue.TryDequeue(out var e));
        Assert.Equal(PrlEventKind.None, e.Kind);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void Events_ComeBackInOrder()
    {
        var queue = new EventQueue();

        queue.Enqueue(Down(1, 1));
        queue.Enqueue(new PrlEvent(PrlEventKind.MouseUp, 2, 2));
        queue.Enqueue(new PrlEvent(PrlEventKind.KeyDown, Key: 65));

        Assert.True(queue.TryDequeue(out var first));
        Assert.True(queue.TryDequeue(out var second));
        Assert.True(queue.TryDequeue(out var third));

        Assert.Equal(PrlEventKind.MouseDown, first.Kind);
        Assert.Equal(PrlEventKind.MouseUp, second.Kind);
        Assert.Equal(PrlEventKind.KeyDown, third.Kind);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void AllFields_SurviveTheRoundTrip()
    {
        var queue = new EventQueue();
        var sent = new PrlEvent(PrlEventKind.Wheel, 10, 20, 2, 65, -3, 6, 42, 7);

        queue.Enqueue(sent);

        Assert.True(queue.TryDequeue(out var got));
        Assert.Equal(sent, got);
    }

    /// <summary>
    /// The single most important behaviour here: Windows Forms produces movement faster than a
    /// prolang program consumes it, and a queue that keeps every one falls further behind the
    /// cursor with each event until the brush trails it by seconds.
    /// </summary>
    [Fact]
    public void ConsecutiveMoves_CollapseToTheLatest()
    {
        var queue = new EventQueue();

        for (var i = 0; i < 500; i++)
        {
            queue.Enqueue(Move(i, i));
        }

        Assert.Equal(1, queue.Count);
        Assert.True(queue.TryDequeue(out var e));
        Assert.Equal(499, e.X);
        Assert.Equal(499, e.Y);
    }

    [Fact]
    public void MovesFromDifferentControls_DoNotCollapse()
    {
        var queue = new EventQueue();

        queue.Enqueue(Move(1, 1, source: 1));
        queue.Enqueue(Move(2, 2, source: 2));

        Assert.Equal(2, queue.Count);
    }

    /// <summary>
    /// Only a move may be superseded. Collapsing across a button press would lose the press.
    /// </summary>
    [Fact]
    public void MovesSeparatedByAPress_DoNotCollapse()
    {
        var queue = new EventQueue();

        queue.Enqueue(Move(1, 1));
        queue.Enqueue(Down(1, 1));
        queue.Enqueue(Move(2, 2));

        Assert.Equal(3, queue.Count);
    }

    [Fact]
    public void ButtonEvents_AreNeverCollapsed()
    {
        var queue = new EventQueue();

        for (var i = 0; i < 20; i++)
        {
            queue.Enqueue(Down(i, i));
        }

        Assert.Equal(20, queue.Count);
    }

    /// <summary>
    /// When the queue fills, movement is what gets dropped — a later one supersedes it anyway.
    /// </summary>
    [Fact]
    public void Overflow_DiscardsTheOldestMovement()
    {
        var queue = new EventQueue(capacity: 3);

        queue.Enqueue(Move(1, 1));
        queue.Enqueue(Down(2, 2));
        queue.Enqueue(Down(3, 3));
        Assert.Equal(3, queue.Count);

        Assert.True(queue.Enqueue(Down(4, 4)));
        Assert.Equal(3, queue.Count);

        // The move went; the three presses survived in order.
        Assert.True(queue.TryDequeue(out var first));
        Assert.True(queue.TryDequeue(out var second));
        Assert.True(queue.TryDequeue(out var third));
        Assert.Equal(2, first.X);
        Assert.Equal(3, second.X);
        Assert.Equal(4, third.X);
    }

    /// <summary>
    /// A press that cannot be stored is refused rather than silently displacing another press:
    /// a dropped button event is an action the user took that never happens.
    /// </summary>
    [Fact]
    public void Overflow_WithNoMovementToDrop_RefusesTheEvent()
    {
        var queue = new EventQueue(capacity: 2);

        Assert.True(queue.Enqueue(Down(1, 1)));
        Assert.True(queue.Enqueue(Down(2, 2)));
        Assert.False(queue.Enqueue(Down(3, 3)));

        Assert.Equal(2, queue.Count);
    }

    /// <summary>
    /// The ring wraps rather than growing, so pushing far more events through it than it can hold
    /// at once has to keep working.
    /// </summary>
    [Fact]
    public void Ring_WrapsWithoutLosingOrder()
    {
        var queue = new EventQueue(capacity: 4);

        for (var round = 0; round < 100; round++)
        {
            queue.Enqueue(Down(round, round));
            Assert.True(queue.TryDequeue(out var e));
            Assert.Equal(round, e.X);
        }

        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void Clear_EmptiesTheQueue()
    {
        var queue = new EventQueue();

        queue.Enqueue(Down(1, 1));
        queue.Enqueue(Down(2, 2));
        queue.Clear();

        Assert.Equal(0, queue.Count);
        Assert.False(queue.TryDequeue(out _));
    }

    [Fact]
    public void Capacity_MustBePositive()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new EventQueue(0));
}
