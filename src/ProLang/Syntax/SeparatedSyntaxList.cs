using System.Collections;
using System.Collections.Immutable;

namespace ProLang.Syntax;

internal abstract class SeparatedSyntaxList
{
    public abstract ImmutableArray<SyntaxNode> GetWithSeparators();
}

internal sealed class SeparatedSyntaxList<T> : SeparatedSyntaxList, IEnumerable<T>
where T : SyntaxNode
{
    private readonly ImmutableArray<SyntaxNode> _nodeAndSeparators;
    public SeparatedSyntaxList(ImmutableArray<SyntaxNode> nodeAndSeparators)
    {
        _nodeAndSeparators = nodeAndSeparators;
    }

    public int Count => (_nodeAndSeparators.Length + 1) / 2;

    public T this[int index] => (T)_nodeAndSeparators[index * 2];

    public SyntaxNode? GetSeparator(int index)
    {
        if (index == Count - 1)
        {
            return null;
        }

        return (SyntaxNode)_nodeAndSeparators[index * 2 + 1];
    }

    public override ImmutableArray<SyntaxNode> GetWithSeparators() => _nodeAndSeparators;

    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < Count; i++)
        {
            yield return this[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}