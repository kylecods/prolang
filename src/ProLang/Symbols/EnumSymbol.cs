using System.Collections.Immutable;

namespace ProLang.Symbols;

public sealed record EnumMember(string Name, int Value);

public sealed class EnumSymbol : TypeSymbol
{
    public EnumSymbol(string name, ImmutableArray<EnumMember> members) : base(name)
    {
        Members = members;
    }

    public ImmutableArray<EnumMember> Members { get; }

    public EnumMember? FindMember(string name)
        => Members.FirstOrDefault(m => m.Name == name);
}
