using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ProLang.Mips;

static class SelfTests
{
    public static void Run()
    {
        int count = 0;
        void Check(bool yes, string message) { if (!yes) throw new Exception(message); count++; }
        void Fails(Action action, string message)
        {
            try { action(); } catch (Exception e) when (e.Message.Contains(message, StringComparison.OrdinalIgnoreCase)) { count++; return; }
            throw new Exception("expected failure: " + message);
        }
        Site Site() => new("Test", "main", new MethodSignature<Ty>(new SignatureHeader(0), Ty.Int, 0, 0, ImmutableArray<Ty>.Empty));
        Method Method(byte[] il, int stack = 4) { var m = new Method("test", Site(), [], stack, Image.Decode(il)); Image.Verify(m); return m; }
        Val Run(byte[] il, Engine e)
        {
            var im = new Image { Methods = [Method(il)] };
            if (e != Engine.Il) MipsCompiler.Compile(im, e == Engine.Optimized);
            return new Vm(im, e).Invoke(0, 0);
        }
        Check(MipsCompiler.R(8, 9, 8, 0, 33) == 0x01094021, "addu encoding");
        Check(MipsCompiler.I(35, 16, 8, 12) == 0x8e08000c, "lw encoding");
        Check(MipsCompiler.I(9, 0, 8, -1) == 0x2408ffff, "signed immediate encoding");
        foreach (Engine e in Enum.GetValues<Engine>())
        {
            Check(Run([0x1f, 40, 0x18, 0x58, 0x2a], e).N == 42, e + " add");
            Check(Run([0x1f, 13, 0x65, 0x1b, 0x5b, 0x2a], e).N == -2, e + " signed division");
            Check(Run([0x1f, 13, 0x65, 0x1b, 0x5d, 0x2a], e).N == -3, e + " signed remainder");
            Check(Run([0x20, 0xff, 0xff, 0xff, 0x7f, 0x17, 0x58, 0x2a], e).N == int.MinValue, e + " wrap");
            Check(Run([0x1f, 2, 0x2d, 2, 0x16, 0x2a, 0x17, 0x2a], e).N == 1, e + " even truth + delay slot");
            Check(Run([0x14, 0x2c, 2, 0x16, 0x2a, 0x17, 0x2a], e).N == 1, e + " null truth");
            Check(Run([0x15, 0x18, 0x64, 0x2a], e).N == 0x3fffffff, e + " unsigned shift");
            Check(Run([0x15, 0x17, 0xfe, 0x05, 0x2a], e).N == 0, e + " unsigned comparison");
            Check(Run([0x1f, 6, 0x18, 0x5a, 0x2a], e).N == 12, e + " multiply");
            Check(Run([0x17, 0x18, 0x2b, 1, 0x00, 0x58, 0x2a], e).N == 3, e + " targeted arithmetic is not fused");
            Fails(() => Run([0x20, 0, 0, 0, 0x80, 0x15, 0x5b, 0x2a], e), "division overflow");
            Check(Run([0x17, 0x18, 0x32, 2, 0x16, 0x2a, 0x17, 0x2a], e).N == 1, e + " slow branch target");
            Fails(() => Run([0x17, 0x16, 0x5b, 0x2a], e), "division by zero");
            Fails(() => Run([0x14, 0x17, 0x58, 0x2a], e), "expected an int32 value");
        }
        Fails(() => Image.Decode([0x20, 1]), "truncated");
        Fails(() => Image.Decode([0x2b, 0xff]), "instruction boundary");
        Fails(() => Method([0x58, 0x2a]), "evaluation stack");
        Fails(() => Method([0x17, 0x17, 0x2a]), "return stack");
        Fails(() => Method([0x17]), "falls out");
        Fails(() => Method([0x06, 0x2a]), "local index");
        Fails(() => Image.Decode([0xfe, 0xff]), "unsupported IL");
        Fails(() => Image.Decode([0x6a]), "unsupported IL");
        var delayMethod = Method([0x16, 0x2a]);
        delayMethod.Words = [MipsCompiler.I(9, 0, 8, 1), MipsCompiler.I(43, 16, 8, 0),
            MipsCompiler.I(4, 0, 0, 2), MipsCompiler.I(9, 0, 9, 42), (2u << 6) | 13,
            MipsCompiler.I(43, 16, 9, 4), MipsCompiler.R(31, 0, 0, 0, 8), 0];
        delayMethod.WordIl = [0, 0, 0, 0, 0, 1, 1, 1]; delayMethod.Starts = [0, 5];
        var delayVm = new Vm(new Image { Methods = [delayMethod] }, Engine.Mips);
        Check(delayVm.Invoke(0, 0).N == 42, "non-nop MIPS branch delay slot executes exactly once");
        Console.WriteLine($"PASS: {count} loader, stack, MIPS encoding, arithmetic, branch/delay-slot and diagnostic checks");
    }
}
