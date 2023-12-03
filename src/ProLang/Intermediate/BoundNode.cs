namespace ProLang.Intermediate;

internal abstract class BoundNode
{
    public abstract BoundNodeKind  Kind { get; }
}