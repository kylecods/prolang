using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace ProLang.Mips;

enum Kind : uint { Null, Int, String, Array, Record, Address, Function, Delegate, Box }
readonly record struct Val(Kind Kind, int N)
{
    public static Val Int(int n) => new(Kind.Int, n);
    public int Integer => Kind == Kind.Int ? N : throw new InvalidOperationException("expected an int32 value");
    public bool Truth => Kind != Kind.Null && (Kind != Kind.Int || N != 0);
}
sealed record Record(int Type, Val[] Fields);
sealed record Box(Val Value, string Type);
sealed record HeapArray(Ty Type, Val[] Values);
abstract class Location
{
    public abstract Val Read(Vm vm);
    public abstract void Write(Vm vm, Val value);
}
sealed class SlotLocation(int slot) : Location
{
    public override Val Read(Vm vm) => vm.Get(slot);
    public override void Write(Vm vm, Val value) => vm.Set(slot, value);
}
sealed class ArrayLocation(Val[] values, int index) : Location
{
    public override Val Read(Vm vm) => values[index];
    public override void Write(Vm vm, Val value) => values[index] = value;
}
sealed class FieldLocation(Location parent, int field) : Location
{
    public override Val Read(Vm vm) => vm.Object<Record>(parent.Read(vm), Kind.Record).Fields[field];
    public override void Write(Vm vm, Val value)
    {
        var record = vm.Object<Record>(parent.Read(vm), Kind.Record);
        var fields = (Val[])record.Fields.Clone(); fields[field] = value;
        parent.Write(vm, vm.Add(Kind.Record, new Record(record.Type, fields)));
    }
}

enum Engine { Il, Mips, Optimized }

sealed class Vm(Image image, Engine engine, int frames = 60)
{
    public readonly Image Image = image;
    public readonly uint[] Memory = new uint[16384 * 2];
    public readonly List<object?> Heap = [null];
    public readonly Val[] Globals = new Val[image.Fields.Length];
    public readonly Host Host = new(frames);
    public readonly long[] Calls = new long[image.Methods.Length], Instructions = new long[image.Methods.Length];
    public long Steps, GuestInstructions, Bridges, TotalCalls;
    public int Depth, Top, AllocatedElements;
    public string Output => output.ToString();
    readonly StringBuilder output = new();
    readonly Dictionary<string, Val> literals = new(StringComparer.Ordinal);
    readonly uint[] registers = new uint[128 * 32];
    public Val Get(int slot) => new((Kind)Memory[slot * 2], unchecked((int)Memory[slot * 2 + 1]));
    public void Set(int slot, Val v) { Memory[slot * 2] = (uint)v.Kind; Memory[slot * 2 + 1] = unchecked((uint)v.N); }
    public Val Add(Kind kind, object obj)
    {
        if (Heap.Count >= 2_000_000) throw new InvalidOperationException("object handle limit exceeded");
        Heap.Add(obj); return new(kind, Heap.Count - 1);
    }
    public T Object<T>(Val v, Kind kind) => v.Kind == kind && (uint)v.N < Heap.Count && Heap[v.N] is T t ? t : throw new InvalidOperationException("expected " + kind);
    public Val Text(string s) => Add(Kind.String, s);
    Val Literal(string s)
    {
        if (!literals.TryGetValue(s, out var v)) literals.Add(s, v = Text(s));
        return v;
    }
    public Val Zero(Ty t, int depth = 0)
    {
        if (depth > 32) throw new InvalidOperationException("recursive or excessively nested struct");
        if (t.Row > 0)
            return Add(Kind.Record, new Record(t.Row, Image.Structs[t.Row].Select(f => Zero(Image.Fields[f].Type, depth + 1)).ToArray()));
        return t.Name is "Boolean" or "Char" or "SByte" or "Byte" or "Int16" or "UInt16" or "Int32" or "UInt32" or "System.Int32" or "System.Char" or "System.Boolean" or "System.Byte" or "IntPtr" ? Val.Int(0) : default;
    }
    public Val NewArray(Ty t, int n)
    {
        if (n < 0 || n > 262144 - AllocatedElements) throw new InvalidOperationException("array heap limit exceeded");
        AllocatedElements += n;
        var vals = new Val[n];
        for (int i = 0; i < n; i++) vals[i] = Zero(t);
        return Add(Kind.Array, new HeapArray(t, vals));
    }
    Val[] Array(Val v, int index)
    {
        if (v.Kind != Kind.Array) throw new InvalidOperationException("expected non-null array");
        var a = Object<HeapArray>(v, Kind.Array).Values;
        if ((uint)index >= a.Length) throw new InvalidOperationException("array index out of bounds");
        return a;
    }
    Val Deref(Val v) => v.Kind == Kind.Address ? Object<Location>(v, Kind.Address).Read(this) : v;
    Location Address(Val v) => Object<Location>(v, Kind.Address);
    public void Run(string[] args)
    {
        for (int f = 0; f < Globals.Length; f++) Globals[f] = Zero(Image.Fields[f].Type);
        var entry = Image.Methods[Image.Entry];
        if (entry.Site.Count == 1 && entry.Site.Signature.ParameterTypes[0].Name == "String[]")
        {
            var a = NewArray(new("String"), args.Length);
            var values = Object<HeapArray>(a, Kind.Array).Values;
            for (int i = 0; i < args.Length; i++) values[i] = Text(args[i]);
            Set(0, a);
        }
        else if (entry.Site.Count != 0) throw new InvalidOperationException("unsupported entry signature");
        Invoke(Image.Entry, 0);
    }

    public Val Invoke(int method, int start)
    {
        if (Depth >= 128) throw new InvalidOperationException("call depth limit exceeded");
        if ((uint)method >= Image.Methods.Length) throw new InvalidOperationException("invalid MethodDef token");
        var m = Image.Methods[method];
        int local = start + m.Site.Count, eval = local + m.Locals.Length;
        int end = eval + m.MaxStack;
        if (end > Memory.Length / 2) throw new InvalidOperationException("value stack exhausted");
        int oldTop = Top; Top = Math.Max(Top, end);
        Calls[method]++; TotalCalls++; Depth++;
        for (int i = 0; i < m.Locals.Length; i++) Set(local + i, Zero(m.Locals[i]));
        try
        {
            if (engine == Engine.Il)
            {
                int pc = 0;
                while (true)
                {
                    var ins = m.Code[pc]; Charge(method);
                    int sp = eval + ins.Depth;
                    if (ins.Code == 0x2a) return m.Site.Returns ? Get(sp - 1) : default;
                    pc = Step(m, pc, start, local, eval);
                }
            }
            return MipsCpu.Execute(this, m, method, start, local, eval, registers.AsSpan((Depth - 1) * 32, 32));
        }
        finally
        {
            // The caller's arguments overlap this frame and may contain its return destination.
            System.Array.Clear(Memory, start * 2, (end - start) * 2);
            Depth--; Top = oldTop;
        }
    }

    public void Charge(int method)
    {
        Instructions[method]++; Steps++;
        if (Steps - Host.LastSteps > 10_000_000) throw new InvalidOperationException("instruction budget exceeded");
    }

    // Shared checked implementation for the IL interpreter and MIPS service boundary.
    public int Step(Method m, int pc, int start, int local, int eval)
    {
        var i = m.Code[pc]; int op = i.Code, a = i.Operand, sp = eval + i.Depth;
        Val x = sp > eval ? Get(sp - 1) : default;
        Val y = sp > eval + 1 ? Get(sp - 2) : default;
        if (op is >= 0x02 and <= 0x05) Set(sp, Get(start + op - 2));
        else if (op is >= 0x06 and <= 0x09) Set(sp, Get(local + op - 6));
        else if (op is >= 0x0a and <= 0x0d) Set(local + op - 0x0a, x);
        else if (op is >= 0x15 and <= 0x1e) Set(sp, Val.Int(op - 0x16));
        else switch (op)
        {
            case 0x00: break;
            case 0x0e: case 0xfe09: Set(sp, Get(start + a)); break;
            case 0x0f: case 0xfe0a: Set(sp, Add(Kind.Address, new SlotLocation(start + a))); break;
            case 0x10: case 0xfe0b: Set(start + a, x); break;
            case 0x11: case 0xfe0c: Set(sp, Get(local + a)); break;
            case 0x12: case 0xfe0d: Set(sp, Add(Kind.Address, new SlotLocation(local + a))); break;
            case 0x13: case 0xfe0e: Set(local + a, x); break;
            case 0x14: Set(sp, default); break;
            case 0x1f: case 0x20: Set(sp, Val.Int(a)); break;
            case 0x25: Set(sp, x); break;
            case 0x26: break;
            case 0x28: case 0x6f:
            {
                var s = i.Site!; int at = sp - s.Count;
                Val result;
                if (s.Method >= 0) result = Invoke(s.Method, at);
                else if (IsDelegate(s.Owner) && s.Name == "Invoke")
                {
                    var d = Get(at);
                    if (d.Kind != Kind.Delegate) throw new InvalidOperationException("Invoke on a null or invalid delegate");
                    if (Image.Methods[d.N].Site.Count != s.Count - 1) throw new InvalidOperationException("argument count mismatch");
                    result = Invoke(d.N, at + 1);
                }
                else result = Native(s, at);
                if (s.Returns) Set(at, result);
                break;
            }
            case 0x2b: case 0x38: return a;
            case 0x2c: case 0x39: if (!x.Truth) return a; break;
            case 0x2d: case 0x3a: if (x.Truth) return a; break;
            case >= 0x2e and <= 0x37: case >= 0x3b and <= 0x44:
            {
                int c = op <= 0x37 ? op - 0x2e : op - 0x3b;
                bool take = c switch
                {
                    0 => Equal(y, x, true), 5 => !Equal(y, x, true),
                    1 => y.Integer >= x.Integer, 2 => y.Integer > x.Integer, 3 => y.Integer <= x.Integer, 4 => y.Integer < x.Integer,
                    6 => (uint)y.Integer >= (uint)x.Integer, 7 => (uint)y.Integer > (uint)x.Integer,
                    8 => (uint)y.Integer <= (uint)x.Integer, 9 => (uint)y.Integer < (uint)x.Integer, _ => false
                };
                if (take) return a;
                break;
            }
            case >= 0x58 and <= 0x64: Set(sp - 2, Val.Int(Binary(op, y.Integer, x.Integer))); break;
            case 0x65: Set(sp - 1, Val.Int(unchecked(-x.Integer))); break;
            case 0x66: Set(sp - 1, Val.Int(~x.Integer)); break;
            case 0x67: Set(sp - 1, Val.Int((sbyte)x.Integer)); break;
            case 0x68: Set(sp - 1, Val.Int((short)x.Integer)); break;
            case 0x69: case 0x6d: _ = x.Integer; break;
            case 0xd1: Set(sp - 1, Val.Int((ushort)x.Integer)); break;
            case 0xd2: Set(sp - 1, Val.Int((byte)x.Integer)); break;
            case 0x71: Set(sp - 1, Address(x).Read(this)); break;
            case 0x72: Set(sp, Literal(i.Text!)); break;
            case 0x73:
                if (!IsDelegate(i.Site!.Owner) || i.Site.Name != ".ctor" || x.Kind != Kind.Function || y.Kind != Kind.Null)
                    throw new NotSupportedException("unsupported constructor " + i.Site);
                Set(sp - 2, new(Kind.Delegate, x.N)); break;
            case 0x7b: case 0x7c: case 0x7d:
            {
                var f = Image.Fields[a];
                var value = Deref(op == 0x7d ? y : x);
                var record = Object<Record>(value, Kind.Record);
                if (f.Static || record.Type != f.Owner) throw new InvalidOperationException("invalid struct field access");
                if (op == 0x7b) Set(sp - 1, record.Fields[f.Index]);
                else
                {
                    var loc = new FieldLocation(Address(op == 0x7d ? y : x), f.Index);
                    if (op == 0x7c) Set(sp - 1, Add(Kind.Address, loc)); else loc.Write(this, x);
                }
                break;
            }
            case 0x7e: Set(sp, Globals[a]); break;
            case 0x7f: Set(sp, Add(Kind.Address, new ArrayLocation(Globals, a))); break;
            case 0x80: Globals[a] = x; break;
            case 0x81: Address(y).Write(this, x); break;
            case 0x8c: Set(sp - 1, Add(Kind.Box, new Box(x, i.Type!.Name))); break;
            case 0x8d: Set(sp - 1, NewArray(i.Type!, x.Integer)); break;
            case 0x8e: Set(sp - 1, Val.Int(Object<HeapArray>(x, Kind.Array).Values.Length)); break;
            case 0x8f:
            {
                int idx = x.Integer; Set(sp - 2, Add(Kind.Address, new ArrayLocation(Array(y, idx), idx))); break;
            }
            case >= 0x90 and <= 0x95: case 0x9a: case 0xa3:
            {
                int idx = x.Integer; var v = Array(y, idx)[idx];
                v = op switch { 0x90 => Val.Int((sbyte)v.Integer), 0x91 => Val.Int((byte)v.Integer), 0x92 => Val.Int((short)v.Integer), 0x93 => Val.Int((ushort)v.Integer), _ => v };
                Set(sp - 2, v); break;
            }
            case 0x9b: case 0x9c: case 0x9d: case 0x9e: case 0xa2: case 0xa4:
            {
                int idx = y.Integer; var v = op switch { 0x9c => Val.Int((byte)x.Integer), 0x9d => Val.Int((ushort)x.Integer), _ => x };
                Array(Get(sp - 3), idx)[idx] = v; break;
            }
            case 0xa5:
                var box = Object<Box>(x, Kind.Box);
                if (box.Type != i.Type!.Name) throw new InvalidOperationException("unbox.any type mismatch");
                Set(sp - 1, box.Value); break;
            case 0xfe01: Set(sp - 2, Val.Int(Equal(y, x, true) ? 1 : 0)); break;
            case 0xfe02: Set(sp - 2, Val.Int(y.Integer > x.Integer ? 1 : 0)); break;
            case 0xfe03: Set(sp - 2, Val.Int((uint)y.Integer > (uint)x.Integer ? 1 : 0)); break;
            case 0xfe04: Set(sp - 2, Val.Int(y.Integer < x.Integer ? 1 : 0)); break;
            case 0xfe05: Set(sp - 2, Val.Int((uint)y.Integer < (uint)x.Integer ? 1 : 0)); break;
            case 0xfe06: Set(sp, new(Kind.Function, i.Site!.Method)); break;
            case 0xfe15: Address(x).Write(this, Zero(i.Type!)); break;
            default: throw new NotSupportedException($"unsupported instruction {i}");
        }
        return pc + 1;
    }

    public static int Binary(int op, int a, int b) => op switch
    {
        0x58 => unchecked(a + b), 0x59 => unchecked(a - b), 0x5a => unchecked(a * b),
        0x5b or 0x5d when b == 0 => throw new InvalidOperationException("division by zero"),
        0x5b when a == int.MinValue && b == -1 => throw new InvalidOperationException("int32 division overflow"),
        0x5b => a / b, 0x5d => b == -1 ? 0 : a % b,
        0x5c or 0x5e when b == 0 => throw new InvalidOperationException("division by zero"),
        0x5c => unchecked((int)((uint)a / (uint)b)), 0x5e => unchecked((int)((uint)a % (uint)b)),
        0x5f => a & b, 0x60 => a | b, 0x61 => a ^ b, 0x62 => a << b, 0x63 => a >> b, 0x64 => unchecked((int)((uint)a >> b)),
        _ => throw new NotSupportedException("arithmetic opcode")
    };
    bool Equal(Val a, Val b, bool identity = false)
    {
        if (a.Kind != b.Kind) return false;
        if (a.Kind == Kind.String)
        {
            if (identity) throw new NotSupportedException("string reference identity comparison is unsupported");
            return Object<string>(a, Kind.String) == Object<string>(b, Kind.String);
        }
        return a == b;
    }
    static bool IsDelegate(string owner) => owner.StartsWith("System.Func`", StringComparison.Ordinal) || owner.StartsWith("System.Action", StringComparison.Ordinal);
    string Format(Val v, string declared = "Object")
    {
        if (v.Kind == Kind.Box) { var b = Object<Box>(v, Kind.Box); return Format(b.Value, b.Type); }
        if (v.Kind == Kind.Null) return "";
        if (v.Kind == Kind.String) return Object<string>(v, Kind.String);
        if (v.Kind == Kind.Int) return declared switch
        {
            "Boolean" or "System.Boolean" => v.N == 0 ? "False" : "True",
            "Char" or "System.Char" => ((char)v.N).ToString(),
            "UInt32" or "System.UInt32" => ((uint)v.N).ToString(CultureInfo.InvariantCulture),
            _ => v.N.ToString(CultureInfo.InvariantCulture)
        };
        throw new NotSupportedException("unsupported scalar formatting");
    }

    Val Native(Site s, int at)
    {
        Val V(int n) => Get(at + n);
        int N(int n) => V(n).Integer;
        string S(int n) => Object<string>(V(n), Kind.String);
        // Match the complete signature so an unrelated assembly method cannot acquire a bridge.
        string key = s.Key;
        if (s.Instance && key is not ("System.String::get_Length()->Int32" or "System.String::IndexOf(String)->Int32"))
            throw new NotSupportedException("unsupported native method " + key);
        switch (key)
        {
            case "ProLang.Runtime.PspOps::Init()->Void": Host.Start(); break;
            case "ProLang.Runtime.PspOps::Clear(Int32)->Void": Host.Clear(N(0)); break;
            case "ProLang.Runtime.PspOps::FillRect(Int32,Int32,Int32,Int32,Int32)->Void": Host.Rect(N(0), N(1), N(2), N(3), N(4)); break;
            case "ProLang.Runtime.PspOps::DrawLine(Int32,Int32,Int32,Int32,Int32)->Void": Host.Line(N(0), N(1), N(2), N(3), N(4)); break;
            case "ProLang.Runtime.PspOps::DrawText(Int32,Int32,String,Int32)->Void": Host.Text(N(0), N(1), S(2), N(3)); break;
            case "ProLang.Runtime.PspOps::SwapBuffers()->Void": Host.Present(this); break;
            case "ProLang.Runtime.PspOps::Vsync()->Void": break;
            case "ProLang.Runtime.PspOps::ButtonsHeld()->Int32": return Val.Int(Host.Buttons);
            case "ProLang.Runtime.PspOps::ButtonPressed(Int32)->Boolean": return Val.Int((Host.Buttons & N(0)) != 0 ? 1 : 0);
            case "ProLang.Runtime.Output::Initialize()->Void": output.Clear(); break;
            case "ProLang.Runtime.Output::Flush()->Void": break;
            case "ProLang.Runtime.Output::Write(String)->Void": case "ProLang.Runtime.Output::Write(Object)->Void":
            case "ProLang.Runtime.Output::Write(Int32)->Void": case "ProLang.Runtime.Output::Write(UInt32)->Void":
            case "ProLang.Runtime.Output::Write(Boolean)->Void": case "ProLang.Runtime.Output::Write(Char)->Void":
                output.Append(Format(V(0), s.Signature.ParameterTypes[0].Name)).Append('\n');
                if (output.Length > 32768) throw new InvalidOperationException("output buffer limit exceeded"); break;
            case "ProLang.Runtime.StringOps::From(Int32)->String": case "ProLang.Runtime.StringOps::From(UInt32)->String":
            case "ProLang.Runtime.StringOps::From(Boolean)->String": case "ProLang.Runtime.StringOps::From(Char)->String":
            case "ProLang.Runtime.StringOps::From(Object)->String": return Text(Format(V(0), s.Signature.ParameterTypes[0].Name));
            case "System.String::Concat(String,String)->String": case "System.String::Concat(Object,Object)->String": return Text(Format(V(0)) + Format(V(1)));
            case "System.String::op_Equality(String,String)->Boolean": return Val.Int(Equal(V(0), V(1)) ? 1 : 0);
            case "System.String::op_Inequality(String,String)->Boolean": return Val.Int(Equal(V(0), V(1)) ? 0 : 1);
            case "System.String::get_Length()->Int32": return Val.Int(S(0).Length);
            case "System.String::IndexOf(String)->Int32": return Val.Int(S(0).IndexOf(S(1), StringComparison.Ordinal));
            case "ProLang.Runtime.StringOps::CharAt(String,Int32)->Char": case "ProLang.Runtime.StringOps::CharCode(String,Int32)->Int32": return Val.Int(S(0)[N(1)]);
            case "ProLang.Runtime.StringOps::Substring(String,Int32,Int32)->String": return Text(S(0)[N(1)..N(2)]);
            case "System.Math::Min(Int32,Int32)->Int32": return Val.Int(Math.Min(N(0), N(1)));
            case "System.Math::Max(Int32,Int32)->Int32": return Val.Int(Math.Max(N(0), N(1)));
            case "ProLang.Runtime.TestOps::Assert(Boolean,String)->Void": if (N(0) == 0) throw new InvalidOperationException("assertion failed: " + S(1)); break;
            case "ProLang.Runtime.ConsoleOps::Write(String)->Void": output.Append(S(0)); break;
            case "System.Threading.Thread::Sleep(Int32)->Void": if (N(0) is < 0 or > 60000) throw new InvalidOperationException("sleep must be between 0 and 60000 ms"); break;
            case "ProLang.Runtime.TimeOps::Millis()->Int32": return Val.Int(Host.Frames.Count * 17);
            default: throw new NotSupportedException("unsupported native method " + key);
        }
        return default;
    }
}
