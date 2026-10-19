namespace ProLang.Mips;

// Little-endian MIPS32 words. s0 points at the argument/local/evaluation frame.
// Values are { uint kind; int payload; }; checked services operate on the same memory.
// syscall's 20-bit code is the original IL index. break 1 is the integer-kind trap.
// This is a documented desktop test ABI, not the V runtime's C/PSP EABI.
static class MipsCompiler
{
    public static uint R(int rs, int rt, int rd, int sa, int f) => (uint)(rs << 21 | rt << 16 | rd << 11 | sa << 6 | f);
    public static uint I(int op, int rs, int rt, int imm) => (uint)(op << 26 | rs << 21 | rt << 16 | (imm & 65535));
    public static int? Constant(Ins i) => i.Code switch { >= 0x15 and <= 0x1e => i.Code - 0x16, 0x1f or 0x20 => i.Operand, _ => null };

    public static void Compile(Image image, bool optimize)
    {
        foreach (var m in image.Methods)
        {
            var words = new List<uint>(); var text = new List<string>(); var il = new List<int>();
            var labels = new int[m.Code.Length];
            var fixups = new List<(int At, int Target)>();
            var targets = m.Code.Where(i => i.Code is >= 0x2b and <= 0x44).Select(i => i.Operand).ToHashSet();
            int current = 0, eval = m.Site.Count + m.Locals.Length;
            m.FoldedCalls = m.FusedPairs = 0;
            void Emit(uint w, string asm) { words.Add(w); text.Add(asm); il.Add(current); }
            void Imm(int r, int v)
            {
                if (v is >= -32768 and <= 32767) Emit(I(9, 0, r, v), $"addiu ${r}, $zero, {v}");
                else { Emit(I(15, 0, r, (int)((uint)v >> 16)), $"lui ${r}, 0x{(uint)v >> 16:x4}"); Emit(I(13, r, r, v), $"ori ${r}, ${r}, 0x{v & 65535:x4}"); }
            }
            void Lw(int r, int slot, int part) => Emit(I(35, 16, r, slot * 8 + part), $"lw ${r}, {slot * 8 + part}($s0)");
            void Sw(int r, int slot, int part) => Emit(I(43, 16, r, slot * 8 + part), $"sw ${r}, {slot * 8 + part}($s0)");
            void Copy(int source, int dest) { Lw(8, source, 0); Lw(9, source, 4); Sw(8, dest, 0); Sw(9, dest, 4); }
            void Int(int slot, int v) { Imm(8, 1); Sw(8, slot, 0); Imm(9, v); Sw(9, slot, 4); }
            void Branch(int op, int rs, int rt, int target, string name)
            {
                fixups.Add((words.Count, target)); Emit(I(op, rs, rt, 0), $"{name} ${rs}, ${rt}, IL_{m.Code[target].Offset:x4}"); Emit(0, "nop ; branch delay slot");
            }
            void CheckInt(int slot)
            {
                Lw(10, slot, 0); Imm(11, 1);
                // Local branch skips the trap, and always executes its delay-slot nop.
                Emit(I(4, 10, 11, 3), "beq $10, $11, +3 ; checked int32"); Emit(0, "nop");
                Emit((1u << 6) | 13, "break 1 ; expected an int32 value"); Emit(0, "nop");
            }
            void Binary(int op, int sp, int? immediate = null)
            {
                int left = immediate.HasValue ? sp - 1 : sp - 2;
                CheckInt(left); Lw(8, left, 4);
                if (immediate.HasValue) Imm(9, immediate.Value);
                else { CheckInt(sp - 1); Lw(9, sp - 1, 4); }
                switch (op)
                {
                    case 0x58: Emit(R(8, 9, 8, 0, 33), "addu $8, $8, $9"); break;
                    case 0x59: Emit(R(8, 9, 8, 0, 35), "subu $8, $8, $9"); break;
                    case 0x5a: Emit(R(8, 9, 0, 0, 24), "mult $8, $9"); Emit(R(0, 0, 8, 0, 18), "mflo $8"); break;
                    case 0x5f: Emit(R(8, 9, 8, 0, 36), "and $8, $8, $9"); break;
                    case 0x60: Emit(R(8, 9, 8, 0, 37), "or $8, $8, $9"); break;
                    case 0x61: Emit(R(8, 9, 8, 0, 38), "xor $8, $8, $9"); break;
                    case 0x62: Emit(R(9, 8, 8, 0, 4), "sllv $8, $8, $9"); break;
                    case 0x63: Emit(R(9, 8, 8, 0, 7), "srav $8, $8, $9"); break;
                    case 0x64: Emit(R(9, 8, 8, 0, 6), "srlv $8, $8, $9"); break;
                    case 0xfe02: case 0xfe03: Emit(R(9, 8, 8, 0, op == 0xfe02 ? 42 : 43), op == 0xfe02 ? "slt $8, $9, $8" : "sltu $8, $9, $8"); break;
                    case 0xfe04: case 0xfe05: Emit(R(8, 9, 8, 0, op == 0xfe04 ? 42 : 43), op == 0xfe04 ? "slt $8, $8, $9" : "sltu $8, $8, $9"); break;
                    default: throw new NotSupportedException("MIPS arithmetic template");
                }
                Sw(8, left, 4); // Both inputs are checked integers; the existing tag stays 1.
            }
            int? Fold(Ins i)
            {
                if (Constant(i) is int n) return n;
                if (optimize && i.Code == 0x28 && i.Site is { Method: >= 0, Count: 0, Returns: true } s)
                {
                    var body = image.Methods[s.Method].Code.Where(x => x.Code != 0).ToArray();
                    if (body.Length == 2 && body[1].Code == 0x2a && Constant(body[0]) is int k) { m.FoldedCalls++; return k; }
                }
                return null;
            }
            for (int j = 0; j < m.Code.Length; j++)
            {
                current = j; labels[j] = words.Count;
                var ins = m.Code[j]; int op = ins.Code, sp = eval + ins.Depth;
                if (ins.Depth < 0) { Emit((2u << 6) | 13, "break 2 ; unreachable IL"); continue; }
                int? constant = Fold(ins);
                if (constant.HasValue)
                {
                    if (optimize && j + 1 < m.Code.Length && !targets.Contains(j + 1) && m.Code[j + 1].Code is 0x58 or 0x59 or 0x5a or 0x5f or 0x60 or 0x61 or 0x62 or 0x63 or 0x64 or >= 0xfe02 and <= 0xfe05)
                    {
                        Binary(m.Code[j + 1].Code, sp, constant); labels[++j] = labels[current]; m.FusedPairs++;
                    }
                    else Int(sp, constant.Value);
                }
                else if (op is >= 2 and <= 5) Copy(op - 2, sp);
                else if (op is 0x0e or 0xfe09) Copy(ins.Operand, sp);
                else if (op is >= 6 and <= 9) Copy(m.Site.Count + op - 6, sp);
                else if (op is 0x11 or 0xfe0c) Copy(m.Site.Count + ins.Operand, sp);
                else if (op is >= 10 and <= 13) Copy(sp - 1, m.Site.Count + op - 10);
                else if (op is 0x13 or 0xfe0e) Copy(sp - 1, m.Site.Count + ins.Operand);
                else if (op is 0x10 or 0xfe0b) Copy(sp - 1, ins.Operand);
                else if (op == 0x14) { Sw(0, sp, 0); Sw(0, sp, 4); }
                else if (op == 0x25) Copy(sp - 1, sp);
                else if (op is 0 or 0x26) Emit(0, "nop");
                else if (op is 0x2b or 0x38) Branch(4, 0, 0, ins.Operand, "beq");
                else if (op is 0x2c or 0x2d or 0x39 or 0x3a)
                {
                    Lw(8, sp - 1, 0); Lw(9, sp - 1, 4);
                    // null is false, integer zero is false, every other kind is true.
                    Imm(10, 1);
                    Emit(I(4, 8, 10, 2), "beq $8, $10, +2 ; integer uses full payload"); Emit(0, "nop");
                    Emit(R(8, 0, 9, 0, 33), "addu $9, $8, $zero ; null/reference truth");
                    Branch(op is 0x2c or 0x39 ? 4 : 5, 9, 0, ins.Operand, op is 0x2c or 0x39 ? "beq" : "bne");
                }
                else if (op is 0x58 or 0x59 or 0x5a or >= 0x5f and <= 0x64 or >= 0xfe02 and <= 0xfe05) Binary(op, sp);
                else if (op == 0x2a) { Emit(R(31, 0, 0, 0, 8), "jr $ra ; return"); Emit(0, "nop ; return delay slot"); }
                else Emit(((uint)j << 6) | 12, $"syscall {j} ; checked service: {ins}");
            }
            foreach (var f in fixups)
            {
                int delta = labels[f.Target] - f.At - 1;
                if (delta is < short.MinValue or > short.MaxValue) throw new InvalidOperationException("MIPS branch out of range");
                words[f.At] = (words[f.At] & 0xffff0000) | (uint)(delta & 65535);
            }
            m.Words = words.ToArray(); m.Assembly = text.ToArray(); m.WordIl = il.ToArray(); m.Starts = labels;
        }
    }
}

static class MipsCpu
{
    // Executes actual encoded words, not emitter annotations. Branches and jr execute one
    // delay-slot instruction. Recursive services use a different register window per frame.
    public static Val Execute(Vm vm, Method m, int method, int start, int local, int eval, Span<uint> r)
    {
        r.Clear(); r[16] = (uint)(start * 8); r[31] = uint.MaxValue;
        int pc = 0, pending = -1; uint lo = 0;
        while (true)
        {
            if ((uint)pc >= m.Words.Length) throw new InvalidOperationException("MIPS PC outside method");
            int index = pc;
            uint w = m.Words[pc++]; vm.GuestInstructions++;
            int il = m.WordIl[index];
            if (index == m.Starts[il]) vm.Charge(method);
            int delayed = pending; pending = -1;
            int op = (int)(w >> 26), rs = (int)(w >> 21 & 31), rt = (int)(w >> 16 & 31), rd = (int)(w >> 11 & 31), sa = (int)(w >> 6 & 31), fn = (int)(w & 63);
            int imm = (short)(w & 65535);
            if (op == 0)
            {
                switch (fn)
                {
                    case 0: r[rd] = r[rt] << sa; break;
                    case 4: r[rd] = r[rt] << (int)(r[rs] & 31); break;
                    case 6: r[rd] = r[rt] >> (int)(r[rs] & 31); break;
                    case 7: r[rd] = (uint)((int)r[rt] >> (int)(r[rs] & 31)); break;
                    case 8: pending = r[rs] == uint.MaxValue ? -2 : checked((int)(r[rs] / 4)); break;
                    case 12:
                        vm.Bridges++;
                        int next = vm.Step(m, checked((int)(w >> 6)), start, local, eval);
                        if (next != il + 1) pc = m.Starts[next];
                        break;
                    case 13: throw new InvalidOperationException((w >> 6) == 1 ? "expected an int32 value" : "unreachable IL executed");
                    case 18: r[rd] = lo; break;
                    case 24: lo = unchecked((uint)((long)(int)r[rs] * (int)r[rt])); break;
                    case 33: r[rd] = unchecked(r[rs] + r[rt]); break;
                    case 35: r[rd] = unchecked(r[rs] - r[rt]); break;
                    case 36: r[rd] = r[rs] & r[rt]; break;
                    case 37: r[rd] = r[rs] | r[rt]; break;
                    case 38: r[rd] = r[rs] ^ r[rt]; break;
                    case 42: r[rd] = (int)r[rs] < (int)r[rt] ? 1u : 0; break;
                    case 43: r[rd] = r[rs] < r[rt] ? 1u : 0; break;
                    default: throw new NotSupportedException($"MIPS SPECIAL 0x{fn:x}");
                }
            }
            else switch (op)
            {
                case 4: pending = r[rs] == r[rt] ? pc + imm : pc + 1; break;
                case 5: pending = r[rs] != r[rt] ? pc + imm : pc + 1; break;
                case 9: r[rt] = unchecked(r[rs] + (uint)imm); break;
                case 11: r[rt] = r[rs] < unchecked((uint)imm) ? 1u : 0; break;
                case 13: r[rt] = r[rs] | (w & 65535); break;
                case 14: r[rt] = r[rs] ^ (w & 65535); break;
                case 15: r[rt] = w << 16; break;
                case 35: case 43:
                    uint address = unchecked(r[rs] + (uint)imm);
                    if ((address & 3) != 0 || address / 4 >= vm.Memory.Length) throw new InvalidOperationException("MIPS memory access outside value stack");
                    if (op == 35) r[rt] = vm.Memory[address / 4]; else vm.Memory[address / 4] = r[rt];
                    break;
                default: throw new NotSupportedException($"MIPS opcode 0x{op:x}");
            }
            r[0] = 0;
            if (delayed == -2)
            {
                var ret = m.Code[il];
                return m.Site.Returns ? vm.Get(eval + ret.Depth - 1) : default;
            }
            if (delayed >= 0) pc = delayed;
        }
    }
}
