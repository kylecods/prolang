module main

// A template JIT: each method body's operations (see `Op` in decode.v) become MIPS code for the
// PSP's Allegrex CPU, compiled once, at load, before the program starts.
//
// ── Why it pays ──────────────────────────────────────────────────────────────
//
// The interpreter spends most of an operation fetching it, dispatching through a jump table,
// and moving 16-byte values through memory by index. Compiled, an operation is the handful of
// loads and stores its work needs: the dispatch is gone, and because the decoder has proved the
// evaluation stack depth at every instruction (verify_stack), every stack slot is a *fixed
// offset* from one register. There is no stack pointer at run time at all.
//
// ── What it keeps ────────────────────────────────────────────────────────────
//
// The same values, in the same `Vm.vals` slots, with the same frame layout as the interpreter.
// The same checks, in the same order, with the same diagnostics. Anything that is not a hot path
// calls `exec_slow`, the interpreter's own implementation of that operation, so a rare operation
// cannot behave differently compiled. Every operation is therefore supported: there is no
// "cannot compile this method".
//
// ── Calling convention of compiled code ──────────────────────────────────────
//
// Each body compiles to a C-callable function (PSP EABI: arguments in $4..$11, result in $2,
// $16..$23 preserved):
//
//     int body(Vm *vm, Value *frame, Value *ret)
//
// `frame` is the method's argument 0; its locals follow its arguments and its evaluation stack
// follows its locals, exactly as in the interpreter. `ret` is where its result goes. It returns 0,
// or 1 after recording the diagnostic in `Vm.jit_error`. A caller that sees 1 returns 1 at once,
// so an error unwinds every compiled frame to `run_compiled`.
//
// Registers inside a body:
//
//     $16 (s0)  the Vm
//     $17 (s1)  the frame: slot k is at k * sizeof(Value)
//     $18 (s2)  the result slot
//     $8..$13   value copies ($8 is also EABI's fifth argument)
//     $10..$12  operands and results of arithmetic
//     $24       kind checks
//     $25       addresses: of a call's target, an array element, a struct's fields
//
// ── Differences from the interpreter, all invisible to a program ─────────────
//
// - The instruction budget is charged once per basic block rather than once per operation.
// - A returning frame clears the pointer word of each of its slots, not the whole slot: enough
//   to keep the conservative collector from retaining what the frame held. `run` clears the
//   value stack in full when the program ends.

// ── MIPS registers ───────────────────────────────────────────────────────────

const r_zero = 0
const r_v0 = 2
const r_a0 = 4
const r_a1 = 5
const r_a2 = 6
const r_a3 = 7
const r_t0 = 8
const r_t1 = 9
const r_t2 = 10
const r_t3 = 11
const r_t4 = 12
const r_t5 = 13
const r_vm = 16 // s0
const r_frame = 17 // s1
const r_ret = 18 // s2
const r_t8 = 24
const r_t9 = 25
const r_sp = 29
const r_ra = 31

// Registers a value copy cycles through: up to six words, for a desktop build's 24-byte Value.
const copy_regs = [r_t0, r_t1, r_t2, r_t3, r_t4, r_t5]

// ── Instruction encoding ─────────────────────────────────────────────────────
// Everything emitted is one of two MIPS formats.

// R-type: SPECIAL (opcode 0) register-register operations, selected by `funct`.
fn mips_r(rs int, rt int, rd int, sa int, funct int) u32 {
	return (u32(rs) << 21) | (u32(rt) << 16) | (u32(rd) << 11) | (u32(sa) << 6) | u32(funct)
}

// I-type: an opcode, two registers and a 16-bit immediate (or branch offset).
fn mips_i(op int, rs int, rt int, imm int) u32 {
	return (u32(op) << 26) | (u32(rs) << 21) | (u32(rt) << 16) | (u32(imm) & 0xffff)
}

fn fits_i16(v int) bool {
	return v >= -32768 && v <= 32767
}

// The diagnostics compiled code raises itself, through `jit_fail`. Each is word for word what the
// interpreter raises in the same situation.
enum JitFail {
	not_int = 1 // expected an int32 value
	not_array // expected non-null array
	out_of_bounds // array index out of bounds
	depth // call depth limit exceeded
	budget // instruction budget exceeded
	stack // value stack exhausted
	bad_delegate // Invoke on a null or invalid delegate
	bad_token // invalid MethodDef token (detail: the token)
	arg_count // argument count mismatch (detail: the callee)
}

struct Fixup {
	at    int // index of the branch instruction
	label int
}

struct Stub {
	label  int
	reason JitFail
}

// A growing buffer of instructions with forward labels.
struct Asm {
mut:
	code   []u32
	labels []int
	fixups []Fixup
	stubs  []Stub
}

fn (mut a Asm) emit(w u32) {
	a.code << w
}

fn (mut a Asm) label() int {
	a.labels << -1
	return a.labels.len - 1
}

fn (mut a Asm) bind(l int) {
	a.labels[l] = a.code.len
}

// A branch to `l`, given its encoding with a zero offset. The delay slot is always a nop: simpler
// than scheduling, and the branch is rarely the cost.
fn (mut a Asm) branch(w u32, l int) {
	a.fixups << Fixup{
		at:    a.code.len
		label: l
	}
	a.emit(w)
	a.emit(0) // nop in the delay slot
}

// Patches every branch's offset, counted in words from the instruction after the branch.
fn (mut a Asm) resolve() ! {
	for f in a.fixups {
		target := a.labels[f.label]
		if target < 0 {
			return error('jit: unbound label')
		}
		offset := target - (f.at + 1)
		if !fits_i16(offset) {
			return error('jit: method too large for a 16-bit branch')
		}
		a.code[f.at] = (a.code[f.at] & 0xffff0000) | (u32(offset) & 0xffff)
	}
}

// Shorthands for the instructions used, named and ordered as in MIPS assembly: destination first.

fn (mut a Asm) addu(rd int, rs int, rt int) {
	a.emit(mips_r(rs, rt, rd, 0, 0x21))
}

fn (mut a Asm) subu(rd int, rs int, rt int) {
	a.emit(mips_r(rs, rt, rd, 0, 0x23))
}

fn (mut a Asm) and_(rd int, rs int, rt int) {
	a.emit(mips_r(rs, rt, rd, 0, 0x24))
}

fn (mut a Asm) or_(rd int, rs int, rt int) {
	a.emit(mips_r(rs, rt, rd, 0, 0x25))
}

fn (mut a Asm) xor(rd int, rs int, rt int) {
	a.emit(mips_r(rs, rt, rd, 0, 0x26))
}

fn (mut a Asm) nor(rd int, rs int, rt int) {
	a.emit(mips_r(rs, rt, rd, 0, 0x27))
}

// rd = (rs < rt) ? 1 : 0, signed / unsigned.
fn (mut a Asm) slt(rd int, rs int, rt int) {
	a.emit(mips_r(rs, rt, rd, 0, 0x2a))
}

fn (mut a Asm) sltu(rd int, rs int, rt int) {
	a.emit(mips_r(rs, rt, rd, 0, 0x2b))
}

// Shifts by a register: rd = rt shifted by the low five bits of rs, exactly the CLI's `& 31`.
fn (mut a Asm) sllv(rd int, rt int, rs int) {
	a.emit(mips_r(rs, rt, rd, 0, 0x04))
}

fn (mut a Asm) srlv(rd int, rt int, rs int) {
	a.emit(mips_r(rs, rt, rd, 0, 0x06))
}

fn (mut a Asm) srav(rd int, rt int, rs int) {
	a.emit(mips_r(rs, rt, rd, 0, 0x07))
}

// Shifts by a constant: left, logical right, arithmetic right.
fn (mut a Asm) sll(rd int, rt int, sa int) {
	a.emit(mips_r(0, rt, rd, sa, 0x00))
}

fn (mut a Asm) srl(rd int, rt int, sa int) {
	a.emit(mips_r(0, rt, rd, sa, 0x02))
}

fn (mut a Asm) sra(rd int, rt int, sa int) {
	a.emit(mips_r(0, rt, rd, sa, 0x03))
}

// rd = low 32 bits of rs * rt. MIPS II has no three-register multiply; the product goes to LO.
// MIPS II also forbids a mult within two instructions after an mflo; every template that
// multiplies loads its operands first, which keeps two products at least that far apart.
fn (mut a Asm) mul(rd int, rs int, rt int) {
	a.emit(mips_r(rs, rt, 0, 0, 0x18)) // mult rs, rt
	a.emit(mips_r(0, 0, rd, 0, 0x12)) // mflo rd
}

// rd = rs * k for a constant k: a shift when k is a power of two, else a multiply through `tmp`.
fn (mut a Asm) mul_const(rd int, rs int, k int, tmp int) {
	if k > 0 && k & (k - 1) == 0 {
		mut shift := 0
		for (1 << shift) < k {
			shift++
		}
		a.sll(rd, rs, shift)
	} else {
		a.li(tmp, k)
		a.mul(rd, rs, tmp)
	}
}

fn (mut a Asm) jr(rs int) {
	a.emit(mips_r(rs, 0, 0, 0, 0x08))
}

// Calls the address in rs, leaving the return address in $ra.
fn (mut a Asm) jalr(rs int) {
	a.emit(mips_r(rs, 0, r_ra, 0, 0x09))
	a.emit(0) // nop in the delay slot
}

fn (mut a Asm) addiu(rt int, rs int, imm int) {
	a.emit(mips_i(0x09, rs, rt, imm))
}

fn (mut a Asm) slti(rt int, rs int, imm int) {
	a.emit(mips_i(0x0a, rs, rt, imm))
}

fn (mut a Asm) sltiu(rt int, rs int, imm int) {
	a.emit(mips_i(0x0b, rs, rt, imm))
}

// andi, ori and xori zero-extend their immediate; addiu, slti and sltiu sign-extend it.
fn (mut a Asm) andi(rt int, rs int, imm int) {
	a.emit(mips_i(0x0c, rs, rt, imm))
}

fn (mut a Asm) ori(rt int, rs int, imm int) {
	a.emit(mips_i(0x0d, rs, rt, imm))
}

fn (mut a Asm) xori(rt int, rs int, imm int) {
	a.emit(mips_i(0x0e, rs, rt, imm))
}

fn (mut a Asm) lui(rt int, imm int) {
	a.emit(mips_i(0x0f, 0, rt, imm))
}

fn (mut a Asm) lw(rt int, off int, base int) {
	a.emit(mips_i(0x23, base, rt, off))
}

fn (mut a Asm) sw(rt int, off int, base int) {
	a.emit(mips_i(0x2b, base, rt, off))
}

fn (mut a Asm) move(rd int, rs int) {
	a.addu(rd, rs, r_zero)
}

fn (mut a Asm) beq(rs int, rt int, l int) {
	a.branch(mips_i(0x04, rs, rt, 0), l)
}

fn (mut a Asm) bne(rs int, rt int, l int) {
	a.branch(mips_i(0x05, rs, rt, 0), l)
}

fn (mut a Asm) b(l int) {
	a.beq(r_zero, r_zero, l)
}

// Loads any 32-bit constant: one instruction when it fits an immediate, else lui then ori.
fn (mut a Asm) li(rt int, v int) {
	if fits_i16(v) {
		a.addiu(rt, r_zero, v)
	} else if v >= 0 && v <= 0xffff {
		a.ori(rt, r_zero, v)
	} else {
		a.lui(rt, int((u32(v) >> 16) & 0xffff))
		if v & 0xffff != 0 {
			a.ori(rt, rt, v & 0xffff)
		}
	}
}

// Loads an address. Absolute: code is generated after every buffer it names has stopped moving.
fn (mut a Asm) la(rt int, p voidptr) {
	a.li(rt, int(u32(usize(p))))
}

// ── Layout ───────────────────────────────────────────────────────────────────

// Where the fields compiled code touches live, taken from this build's own structs rather than
// assumed, so the generator is right for whatever layout the C compiler chose.
struct JitLayout {
	vsize       int // sizeof(Value)
	kind        int // offsets within Value
	n           int
	data        int
	arr_data    int // offsets within a V array
	arr_len     int
	heap_size   int // sizeof(HeapArray)
	heap_values int // offset of HeapArray.values
	vd_fields   int // offset of ValueData.fields
	vm_steps    int // offsets within Vm
	vm_calls    int
	vm_depth    int
	vm_vals_end int
	vm_heap     int
}

fn jit_layout() JitLayout {
	return JitLayout{
		vsize:       int(sizeof(Value))
		kind:        int(__offsetof(Value, kind))
		n:           int(__offsetof(Value, n))
		data:        int(__offsetof(Value, data))
		// V 0.4.12's array header is { data, offset, len, cap, ... }; V will not name its
		// internal `array` type here, so `check_array_layout` confirms this against a real one.
		arr_data:    0
		arr_len:     int(sizeof(voidptr)) + 4
		heap_size:   int(sizeof(HeapArray))
		heap_values: int(__offsetof(HeapArray, values))
		vd_fields:   int(__offsetof(ValueData, fields))
		vm_steps:    int(__offsetof(Vm, steps))
		vm_calls:    int(__offsetof(Vm, calls))
		vm_depth:    int(__offsetof(Vm, jit_depth))
		vm_vals_end: int(__offsetof(Vm, jit_vals_end))
		vm_heap:     int(__offsetof(Vm, heap))
	}
}

// The Kind values compiled code compares against (value.v).
const kind_int = 1
const kind_array = 3
const kind_record = 5
const kind_delegate = 8

// ── Compiling one body ───────────────────────────────────────────────────────

struct JitBody {
	ly     JitLayout
	vm     &Vm
	body   Body
	depths []int
	nargs  int
	sb     int // slot of evaluation stack position 0: nargs + nlocals
	frame  int // slots in the frame: nargs + nlocals + max_stack
mut:
	a          Asm
	labels     []int // a label at every operation, for branches
	err        int   // jumps here once the diagnostic is recorded: return 1
	ok_exit    int   // returns 0
	ret_common int   // every `ret` jumps here with its result in $t0..
}

// Byte offset of `field` of frame slot `slot`, from the frame register.
fn (j &JitBody) at(slot int, field int) int {
	return slot * j.ly.vsize + field
}

// The address of literal `i` or static field `i`. Both arrays are complete before compilation and
// never grow afterwards, so the address is fixed.
fn (j &JitBody) literal(i int) voidptr {
	return unsafe { voidptr(&u8(j.vm.literals.data) + i * j.ly.vsize) }
}

fn (j &JitBody) global(i int) voidptr {
	return unsafe { voidptr(&u8(j.vm.globals.data) + i * j.ly.vsize) }
}

// Copies one Value, word by word: loads first, then stores, so no load waits on a store.
fn (mut j JitBody) copy(dst_base int, dst_off int, src_base int, src_off int) {
	words := j.ly.vsize / 4
	for w in 0 .. words {
		j.a.lw(copy_regs[w], src_off + w * 4, src_base)
	}
	for w in 0 .. words {
		j.a.sw(copy_regs[w], dst_off + w * 4, dst_base)
	}
}

// Writes the int in register `v` as a whole Value at `off(base)`: kind integer, n = v, every
// other word zero. (An int on the stack always has `id` 0 and no payload; see value.v.)
fn (mut j JitBody) store_int_at(base int, off int, v int) {
	j.a.li(r_t8, kind_int)
	for w in 0 .. j.ly.vsize / 4 {
		o := w * 4
		if o == j.ly.kind {
			j.a.sw(r_t8, off + o, base)
		} else if o == j.ly.n {
			j.a.sw(v, off + o, base)
		} else {
			j.a.sw(r_zero, off + o, base)
		}
	}
}

fn (mut j JitBody) store_int(slot int, v int) {
	j.store_int_at(r_frame, j.at(slot, 0), v)
}

// The label of this function's stub for `reason`: one per reason, shared by every check.
fn (mut j JitBody) stub(reason JitFail) int {
	for s in j.a.stubs {
		if s.reason == reason {
			return s.label
		}
	}
	l := j.a.label()
	j.a.stubs << Stub{
		label:  l
		reason: reason
	}
	return l
}

// Branches to a stub raising `reason` unless register `r` is zero.
fn (mut j JitBody) fail_unless_zero(r int, reason JitFail) {
	l := j.stub(reason)
	j.a.bne(r, r_zero, l)
}

// Branches to a stub raising `reason` if register `r` is zero.
fn (mut j JitBody) fail_if_zero(r int, reason JitFail) {
	l := j.stub(reason)
	j.a.beq(r, r_zero, l)
}

// Fails with `expected an int32 value` unless the slot holds an int:
//     lw   t8, kind(slot) ; xori t8, t8, 1 ; bne t8, zero, fail
fn (mut j JitBody) need_int(slot int) {
	j.a.lw(r_t8, j.at(slot, j.ly.kind), r_frame)
	j.a.xori(r_t8, r_t8, kind_int)
	j.fail_unless_zero(r_t8, .not_int)
}

// Leaves 0 in $t8 if both slots hold ints, nonzero otherwise.
fn (mut j JitBody) both_ints(x int, y int) {
	j.a.lw(r_t8, j.at(x, j.ly.kind), r_frame)
	j.a.lw(r_t9, j.at(y, j.ly.kind), r_frame)
	j.a.xori(r_t8, r_t8, kind_int)
	j.a.xori(r_t9, r_t9, kind_int)
	j.a.or_(r_t8, r_t8, r_t9)
}

// The interpreter's `if a.kind != .integer || b.kind != .integer { fail }`.
fn (mut j JitBody) need_ints(x int, y int) {
	j.both_ints(x, y)
	j.fail_unless_zero(r_t8, .not_int)
}

// Calls `exec_slow` for operation `pc`, at stack depth `d`:
//     if (jit_slow(vm, &fast[pc], frame, &locals, &stack[d])) return 1
// On failure the diagnostic is already recorded.
fn (mut j JitBody) slow(pc int, d int) {
	j.a.move(r_a0, r_vm)
	j.a.la(r_a1, unsafe { voidptr(&j.body.fast[pc]) })
	j.a.move(r_a2, r_frame)
	j.a.addiu(r_a3, r_frame, j.at(j.nargs, 0))
	j.a.addiu(r_t0, r_frame, j.at(j.sb + d, 0)) // one past the top
	j.a.la(r_t9, voidptr(jit_slow))
	j.a.jalr(r_t9)
	j.a.bne(r_v0, r_zero, j.err)
}

// Compiles body `index` of `vm`.
fn jit_compile_body(vm &Vm, ly JitLayout, index int) ![]u32 {
	b := vm.bodies[index]
	depths := verify_stack(b.fast, b, vm.image.methods[index].name, vm.bodies)!
	nargs := b.signature.params.len
	mut j := JitBody{
		ly:     ly
		vm:     unsafe { vm }
		body:   b
		depths: depths
		nargs:  nargs
		sb:     nargs + b.locals.len
		frame:  nargs + b.locals.len + b.max_stack
		// Sized up front: growing these by reallocation leaves garbage the collector must
		// sweep, and a program's whole compilation happens at once.
		a:      Asm{
			code:   []u32{cap: b.fast.len * 24 + 64}
			labels: []int{cap: b.fast.len * 2 + 16}
			fixups: []Fixup{cap: b.fast.len * 2 + 16}
		}
		labels: []int{cap: b.fast.len + 1}
	}
	// Every slot offset, and the frame size added to the frame pointer, must fit an immediate.
	if j.frame * ly.vsize + ly.vsize > 32767 {
		return error('jit: frame of ${j.frame} slots is too large')
	}
	for _ in 0 .. b.fast.len + 1 {
		j.labels << j.a.label()
	}
	j.err = j.a.label()
	j.ok_exit = j.a.label()
	j.ret_common = j.a.label()
	exit := j.a.label()
	fail := j.a.label()

	j.prologue()
	leaders := block_leaders(b.fast)
	for pc, f in b.fast {
		j.a.bind(j.labels[pc])
		d := depths[pc]
		if d < 0 {
			continue // unreachable: nothing branches here
		}
		if leaders[pc] > 0 {
			j.charge(leaders[pc])
		}
		j.op(pc, f, d)!
	}
	j.a.bind(j.labels[b.fast.len])
	j.epilogue()

	// Out-of-line failures. Each stub loads its reason and joins the common path, which records
	// the diagnostic — jit_fail(vm, reason, detail), the detail already in $a2 — and returns 1.
	for s in j.a.stubs.clone() {
		j.a.bind(s.label)
		j.a.li(r_a1, int(s.reason))
		j.a.b(fail)
	}
	j.a.bind(fail)
	j.a.move(r_a0, r_vm)
	j.a.la(r_t9, voidptr(jit_fail))
	j.a.jalr(r_t9)
	j.a.bind(j.err)
	j.a.li(r_v0, 1)
	j.a.b(exit)
	j.a.bind(j.ok_exit)
	j.a.move(r_v0, r_zero)
	// Restores the saved registers and returns.
	j.a.bind(exit)
	j.a.lw(r_ra, 28, r_sp)
	j.a.lw(r_vm, 24, r_sp)
	j.a.lw(r_frame, 20, r_sp)
	j.a.lw(r_ret, 16, r_sp)
	j.a.jr(r_ra)
	j.a.addiu(r_sp, r_sp, 32) // in the delay slot: pops the stack frame as it returns
	j.a.resolve()!
	return j.a.code
}

// Method entry — the interpreter's `.call` and `enter`: save registers, count the call against
// the depth limit, check the frame fits in the value stack, give each local its zero value.
fn (mut j JitBody) prologue() {
	ly := j.ly
	j.a.addiu(r_sp, r_sp, -32)
	j.a.sw(r_ra, 28, r_sp)
	j.a.sw(r_vm, 24, r_sp)
	j.a.sw(r_frame, 20, r_sp)
	j.a.sw(r_ret, 16, r_sp)
	j.a.move(r_vm, r_a0)
	j.a.move(r_frame, r_a1)
	j.a.move(r_ret, r_a2)
	// if (++vm->jit_depth >= max_depth) fail(depth)
	j.a.lw(r_t0, ly.vm_depth, r_vm)
	j.a.addiu(r_t0, r_t0, 1)
	j.a.sw(r_t0, ly.vm_depth, r_vm)
	j.a.slti(r_t1, r_t0, max_depth)
	j.fail_if_zero(r_t1, .depth)
	// vm->calls++
	j.a.lw(r_t0, ly.vm_calls, r_vm)
	j.a.addiu(r_t0, r_t0, 1)
	j.a.sw(r_t0, ly.vm_calls, r_vm)
	// if (vm->jit_vals_end < frame + frame_bytes) fail(stack)
	j.a.lw(r_t0, ly.vm_vals_end, r_vm)
	j.a.addiu(r_t1, r_frame, j.frame * ly.vsize)
	j.a.sltu(r_t2, r_t0, r_t1)
	j.fail_unless_zero(r_t2, .stack)
	// Locals: null and an int are written directly; anything else (a struct's zero value) is
	// copied from the body's own template, which never moves.
	for i, v in j.body.local_init {
		slot := j.nargs + i
		if v.data == unsafe { nil } && v.id == 0 && v.kind in [.null_value, .integer] {
			if v.kind == .integer {
				j.a.li(r_t1, v.n)
				j.store_int(slot, r_t1)
			} else {
				for w in 0 .. ly.vsize / 4 {
					j.a.sw(r_zero, j.at(slot, w * 4), r_frame)
				}
			}
		} else {
			j.a.la(r_t9, unsafe { voidptr(&j.body.local_init[i]) })
			j.copy(r_frame, j.at(slot, 0), r_t9, 0)
		}
	}
}

// The return path every `ret` jumps to, with its result in $t0..: release every slot's payload
// pointer, store the result where the caller wants it, and leave: --vm->jit_depth; return 0.
fn (mut j JitBody) epilogue() {
	ly := j.ly
	j.a.bind(j.ret_common)
	for s in 0 .. j.frame {
		for off := ly.data; off < ly.vsize; off += 4 {
			j.a.sw(r_zero, j.at(s, off), r_frame)
		}
	}
	if j.body.signature.returns != 1 {
		for w in 0 .. ly.vsize / 4 {
			j.a.sw(copy_regs[w], w * 4, r_ret)
		}
	}
	j.a.lw(r_t8, ly.vm_depth, r_vm)
	j.a.addiu(r_t8, r_t8, -1)
	j.a.sw(r_t8, ly.vm_depth, r_vm)
	j.a.b(j.ok_exit)
}

// Charges a basic block's operations against the instruction budget, as the interpreter charges
// each operation:
//     vm->steps += len; if (vm->steps > max_steps) fail(budget)
fn (mut j JitBody) charge(len int) {
	j.a.lw(r_t0, j.ly.vm_steps, r_vm)
	j.a.addiu(r_t0, r_t0, len)
	j.a.sw(r_t0, j.ly.vm_steps, r_vm)
	j.a.li(r_t1, max_steps)
	j.a.slt(r_t1, r_t1, r_t0)
	j.fail_unless_zero(r_t1, .budget)
}

// For each operation that starts a basic block, the block's length; 0 elsewhere. A block starts
// at the entry, at every branch target, and after every branch or return.
fn block_leaders(code []Fast) []int {
	mut start := []bool{len: code.len + 1}
	if code.len > 0 {
		start[0] = true
	}
	for i, f in code {
		if is_branch(f.op) {
			start[f.a] = true
			start[i + 1] = true
		}
		if f.op == .ret {
			start[i + 1] = true
		}
	}
	mut lens := []int{len: code.len}
	mut i := 0
	for i < code.len {
		mut k := i + 1
		for k < code.len && !start[k] {
			k++
		}
		lens[i] = k - i
		i = k
	}
	return lens
}

// Emits operation `pc`, which runs at evaluation stack depth `d`. Slot names: `top` is the
// topmost value, `next` where a push lands, `x` and `y` a binary operation's operands (x below).
// A binary operation's result goes to x, which is where the interpreter leaves it too.
fn (mut j JitBody) op(pc int, f Fast, d int) ! {
	ly := j.ly
	top := j.sb + d - 1
	next := j.sb + d
	x := j.sb + d - 2
	y := j.sb + d - 1
	match f.op {
		.ldarg {
			j.copy(r_frame, j.at(next, 0), r_frame, j.at(f.a, 0))
		}
		.ldloc {
			j.copy(r_frame, j.at(next, 0), r_frame, j.at(j.nargs + f.a, 0))
		}
		.stloc {
			j.copy(r_frame, j.at(j.nargs + f.a, 0), r_frame, j.at(top, 0))
		}
		.starg {
			j.copy(r_frame, j.at(f.a, 0), r_frame, j.at(top, 0))
		}
		.ldc {
			j.a.li(r_t2, f.a)
			j.store_int(next, r_t2)
		}
		.ldnull {
			for w in 0 .. ly.vsize / 4 {
				j.a.sw(r_zero, j.at(next, w * 4), r_frame)
			}
		}
		.ldstr {
			j.a.la(r_t9, j.literal(f.a))
			j.copy(r_frame, j.at(next, 0), r_t9, 0)
		}
		.ldsfld {
			j.a.la(r_t9, j.global(f.a))
			j.copy(r_frame, j.at(next, 0), r_t9, 0)
		}
		.stsfld {
			j.a.la(r_t9, j.global(f.a))
			j.copy(r_t9, 0, r_frame, j.at(top, 0))
		}
		.pop {
			// Nothing: depths are static, so the next push simply lands on this slot.
		}
		.dup {
			j.copy(r_frame, j.at(next, 0), r_frame, j.at(top, 0))
		}
		.add, .sub, .mul, .and_, .or_, .xor, .shl, .shr, .shr_un {
			// x = x OP y; both must be ints. Only x's n word changes: its kind is already int,
			// and an int's other words are already zero.
			j.need_ints(x, y)
			j.a.lw(r_t2, j.at(x, ly.n), r_frame)
			j.a.lw(r_t3, j.at(y, ly.n), r_frame)
			match f.op {
				.add { j.a.addu(r_t2, r_t2, r_t3) }
				.sub { j.a.subu(r_t2, r_t2, r_t3) }
				.mul { j.a.mul(r_t2, r_t2, r_t3) }
				.and_ { j.a.and_(r_t2, r_t2, r_t3) }
				.or_ { j.a.or_(r_t2, r_t2, r_t3) }
				.xor { j.a.xor(r_t2, r_t2, r_t3) }
				.shl { j.a.sllv(r_t2, r_t2, r_t3) }
				.shr { j.a.srav(r_t2, r_t2, r_t3) }
				else { j.a.srlv(r_t2, r_t2, r_t3) }
			}
			j.a.sw(r_t2, j.at(x, ly.n), r_frame)
		}
		.add_imm, .sub_imm, .mul_imm {
			// top = top OP a
			j.need_int(top)
			j.a.lw(r_t2, j.at(top, ly.n), r_frame)
			if f.op == .add_imm && fits_i16(f.a) {
				j.a.addiu(r_t2, r_t2, f.a)
			} else if f.op == .sub_imm && fits_i16(-f.a) && f.a != -2147483647 - 1 {
				j.a.addiu(r_t2, r_t2, -f.a)
			} else {
				j.a.li(r_t3, f.a)
				match f.op {
					.add_imm { j.a.addu(r_t2, r_t2, r_t3) }
					.sub_imm { j.a.subu(r_t2, r_t2, r_t3) }
					else { j.a.mul(r_t2, r_t2, r_t3) }
				}
			}
			j.a.sw(r_t2, j.at(top, ly.n), r_frame)
		}
		.clt, .cgt, .clt_un, .cgt_un, .cge, .cle {
			// x = (x < y), (x > y), ... as 0 or 1, which slt and sltu produce directly. >= and
			// <= are the negations of < and >: the 0/1 is flipped with xori 1.
			j.need_ints(x, y)
			j.a.lw(r_t2, j.at(x, ly.n), r_frame)
			j.a.lw(r_t3, j.at(y, ly.n), r_frame)
			j.compare(f.op, r_t2, r_t3)
			j.a.sw(r_t2, j.at(x, ly.n), r_frame)
		}
		.clt_imm, .cgt_imm, .cge_imm, .cle_imm {
			j.need_int(top)
			j.a.lw(r_t2, j.at(top, ly.n), r_frame)
			j.a.li(r_t3, f.a)
			j.compare(match f.op {
				.clt_imm { Op.clt }
				.cgt_imm { Op.cgt }
				.cge_imm { Op.cge }
				else { Op.cle }
			}, r_t2, r_t3)
			j.a.sw(r_t2, j.at(top, ly.n), r_frame)
		}
		.ceq_imm {
			// top = (top is an int) && (top.n == a). The slot may have held any kind, so the
			// whole Value is rewritten.
			j.a.lw(r_t2, j.at(top, ly.kind), r_frame)
			j.a.lw(r_t3, j.at(top, ly.n), r_frame)
			j.a.xori(r_t2, r_t2, kind_int) // 0 when an int
			j.a.li(r_t4, f.a)
			j.a.xor(r_t3, r_t3, r_t4) // 0 when equal
			j.a.or_(r_t2, r_t2, r_t3)
			j.a.sltiu(r_t2, r_t2, 1) // 1 when both were 0
			j.store_int(top, r_t2)
		}
		.ceq {
			// Two ints compare inline; any other pair goes to exec_slow, which also raises the
			// string-identity diagnostic.
			slow := j.a.label()
			done := j.a.label()
			j.both_ints(x, y)
			j.a.bne(r_t8, r_zero, slow)
			j.a.lw(r_t2, j.at(x, ly.n), r_frame)
			j.a.lw(r_t3, j.at(y, ly.n), r_frame)
			j.a.xor(r_t2, r_t2, r_t3)
			j.a.sltiu(r_t2, r_t2, 1)
			j.a.sw(r_t2, j.at(x, ly.n), r_frame)
			j.a.b(done)
			j.a.bind(slow)
			j.slow(pc, d)
			j.a.bind(done)
		}
		.neg, .not, .conv_i1, .conv_i2, .conv_u1, .conv_u2, .conv_i4 {
			// top = op(top). conv.i4 is the identity, so it only checks.
			j.need_int(top)
			if f.op != .conv_i4 {
				j.a.lw(r_t2, j.at(top, ly.n), r_frame)
				j.narrow(f.op, r_t2)
				j.a.sw(r_t2, j.at(top, ly.n), r_frame)
			}
		}
		.ldelem, .ldelem_imm {
			// x = x[y] (or top = top[a]), with the interpreter's checks in its order: the index
			// is an int, the array a live array handle, the index within its length.
			arr := if f.op == .ldelem { x } else { top }
			if f.op == .ldelem {
				j.need_int(y)
				j.element_address(arr, y, 0)
			} else {
				j.element_address(arr, -1, f.a)
			}
			if f.op == .ldelem && f.a != 0 {
				// A narrowing load (ldelem.i1 and the like) needs the element to be an int too.
				j.a.lw(r_t8, ly.kind, r_t9)
				j.a.xori(r_t8, r_t8, kind_int)
				j.fail_unless_zero(r_t8, .not_int)
				j.a.lw(r_t2, ly.n, r_t9)
				j.narrow(match f.a {
					1 { Op.conv_i1 }
					2 { Op.conv_u1 }
					3 { Op.conv_i2 }
					else { Op.conv_u2 }
				}, r_t2)
				j.store_int(arr, r_t2)
			} else {
				j.copy(r_frame, j.at(arr, 0), r_t9, 0)
			}
		}
		.stelem {
			// array[index] = v, from [..., array, index, v].
			arr := j.sb + d - 3
			idx := j.sb + d - 2
			v := j.sb + d - 1
			j.need_int(idx)
			j.element_address(arr, idx, 0)
			if f.a == 0 {
				j.copy(r_t9, 0, r_frame, j.at(v, 0))
			} else {
				// stelem.i1 / .i2 store the int narrowed to 8 or 16 bits, unsigned.
				j.need_int(v)
				j.a.lw(r_t2, j.at(v, ly.n), r_frame)
				j.a.andi(r_t2, r_t2, if f.a == 1 { 0xff } else { 0xffff })
				j.store_int_at(r_t9, 0, r_t2)
			}
		}
		.ldfld, .ldarg_fld {
			// The common case inline: a struct value whose type is the field's declaring type,
			// with the slot in range. A managed address, or anything invalid, goes to exec_slow,
			// which follows the address and raises the diagnostics.
			if f.b < 0 || !fits_i16(f.b + 1) || !fits_i16(f.b * ly.vsize + ly.vsize) {
				return error('jit: field slot ${f.b} out of range')
			}
			obj := if f.op == .ldfld { top } else { f.a }
			dst := if f.op == .ldfld { top } else { next }
			slow := j.a.label()
			done := j.a.label()
			j.a.lw(r_t2, j.at(obj, ly.kind), r_frame)
			j.a.xori(r_t2, r_t2, kind_record)
			j.a.bne(r_t2, r_zero, slow)
			j.a.lw(r_t2, j.at(obj, ly.n), r_frame) // the struct's TypeDef row
			j.a.li(r_t3, f.c)
			j.a.bne(r_t2, r_t3, slow)
			j.a.lw(r_t4, j.at(obj, ly.data), r_frame) // its payload
			j.a.lw(r_t2, ly.vd_fields + ly.arr_len, r_t4)
			j.a.slti(r_t3, r_t2, f.b + 1) // fields.len <= b ?
			j.a.bne(r_t3, r_zero, slow)
			j.a.lw(r_t9, ly.vd_fields + ly.arr_data, r_t4)
			j.copy(r_frame, j.at(dst, 0), r_t9, f.b * ly.vsize)
			j.a.b(done)
			j.a.bind(slow)
			j.slow(pc, d)
			j.a.bind(done)
		}
		.br {
			j.a.b(j.labels[f.a])
		}
		.brtrue, .brfalse {
			// False is null or the int 0; every other value is true:
			//     kind == null -> false;  kind != int -> true;  otherwise n != 0
			target := j.labels[f.a]
			skip := j.a.label()
			j.a.lw(r_t2, j.at(top, ly.kind), r_frame)
			j.a.lw(r_t3, j.at(top, ly.n), r_frame)
			if f.op == .brtrue {
				j.a.beq(r_t2, r_zero, skip)
				j.a.xori(r_t2, r_t2, kind_int)
				j.a.bne(r_t2, r_zero, target)
				j.a.bne(r_t3, r_zero, target)
			} else {
				j.a.beq(r_t2, r_zero, target)
				j.a.xori(r_t2, r_t2, kind_int)
				j.a.bne(r_t2, r_zero, skip)
				j.a.beq(r_t3, r_zero, target)
			}
			j.a.bind(skip)
		}
		.beq, .bne_un {
			// Two ints compare inline; any other pair asks `jit_equal` (equal_values). Either
			// way $v0 ends up 1 when equal.
			target := j.labels[f.a]
			slow := j.a.label()
			decide := j.a.label()
			j.both_ints(x, y)
			j.a.bne(r_t8, r_zero, slow)
			j.a.lw(r_t2, j.at(x, ly.n), r_frame)
			j.a.lw(r_t3, j.at(y, ly.n), r_frame)
			j.a.xor(r_t2, r_t2, r_t3)
			j.a.sltiu(r_v0, r_t2, 1)
			j.a.b(decide)
			j.a.bind(slow)
			j.a.addiu(r_a0, r_frame, j.at(x, 0))
			j.a.addiu(r_a1, r_frame, j.at(y, 0))
			j.a.la(r_t9, voidptr(jit_equal))
			j.a.jalr(r_t9)
			j.a.bind(decide)
			if f.op == .beq {
				j.a.bne(r_v0, r_zero, target)
			} else {
				j.a.beq(r_v0, r_zero, target)
			}
		}
		.bge, .bgt, .ble, .blt, .bge_un, .bgt_un, .ble_un, .blt_un {
			// Compare as clt/cgt would, then branch on the 0 or 1: < and > are taken on 1,
			// >= and <= (their negations) on 0.
			target := j.labels[f.a]
			j.need_ints(x, y)
			j.a.lw(r_t2, j.at(x, ly.n), r_frame)
			j.a.lw(r_t3, j.at(y, ly.n), r_frame)
			match f.op {
				.blt, .bge { j.a.slt(r_t4, r_t2, r_t3) }
				.bgt, .ble { j.a.slt(r_t4, r_t3, r_t2) }
				.blt_un, .bge_un { j.a.sltu(r_t4, r_t2, r_t3) }
				else { j.a.sltu(r_t4, r_t3, r_t2) }
			}
			if f.op in [.blt, .bgt, .blt_un, .bgt_un] {
				j.a.bne(r_t4, r_zero, target)
			} else {
				j.a.beq(r_t4, r_zero, target)
			}
		}
		.call {
			// The arguments are already in place: they become the callee's frame, and its
			// result lands on the first of them.
			//     if (table[a](vm, &slot[first], &slot[first])) return 1
			first := j.sb + d - f.b
			j.a.move(r_a0, r_vm)
			j.a.addiu(r_a1, r_frame, j.at(first, 0))
			j.a.move(r_a2, r_a1)
			j.a.la(r_t9, unsafe { voidptr(&j.vm.jit_table[f.a]) })
			j.a.lw(r_t9, 0, r_t9)
			j.a.jalr(r_t9)
			j.a.bne(r_v0, r_zero, j.err)
		}
		.invoke {
			// [..., delegate, args...]: resolve the delegate's method, check its arity, and call
			// it with the arguments as its frame and the delegate's slot for the result. The
			// checks are the interpreter's, in its order.
			dslot := j.sb + d - f.b
			j.a.lw(r_t2, j.at(dslot, ly.kind), r_frame)
			j.a.xori(r_t2, r_t2, kind_delegate)
			j.fail_unless_zero(r_t2, .bad_delegate)
			j.a.lw(r_a2, j.at(dslot, ly.n), r_frame) // the MethodDef token: the detail if it fails
			j.a.sll(r_t2, r_a2, 8)
			j.a.srl(r_t2, r_t2, 8) // token & 0xffffff
			j.a.addiu(r_t2, r_t2, -1) // body index; -1 wraps high and fails the unsigned test
			j.a.sltiu(r_t3, r_t2, j.vm.bodies.len)
			j.fail_if_zero(r_t3, .bad_token)
			j.a.move(r_a2, r_t2) // from here the detail is the body index
			j.a.sll(r_t3, r_t2, 2)
			j.a.la(r_t4, unsafe { voidptr(&j.vm.jit_nparams[0]) })
			j.a.addu(r_t4, r_t4, r_t3)
			j.a.lw(r_t4, 0, r_t4)
			j.a.li(r_t5, f.b - 1)
			j.a.xor(r_t4, r_t4, r_t5)
			j.fail_unless_zero(r_t4, .arg_count)
			j.a.la(r_t9, unsafe { voidptr(&j.vm.jit_table[0]) })
			j.a.addu(r_t9, r_t9, r_t3)
			j.a.lw(r_t9, 0, r_t9)
			j.a.move(r_a0, r_vm)
			j.a.addiu(r_a1, r_frame, j.at(dslot + 1, 0))
			j.a.addiu(r_a2, r_frame, j.at(dslot, 0))
			j.a.jalr(r_t9)
			j.a.bne(r_v0, r_zero, j.err)
		}
		.ret {
			// Hold the result in registers ($t0..) and join the method's one return path
			// (see `epilogue`), which releases the frame and stores the result.
			if j.body.signature.returns != 1 {
				for w in 0 .. ly.vsize / 4 {
					j.a.lw(copy_regs[w], j.at(top, w * 4), r_frame)
				}
			}
			j.a.b(j.ret_common)
		}
		else {
			// Everything else: the one shared implementation, exec_slow. Its effect on the
			// stack is the one verify_stack already accounted for.
			j.slow(pc, d)
		}
	}
}

// rd (= $t2) = rd OP rs as 0 or 1, for the comparisons. Signed unless `_un`.
fn (mut j JitBody) compare(op Op, rd int, rs int) {
	match op {
		.clt {
			j.a.slt(rd, rd, rs)
		}
		.cgt {
			j.a.slt(rd, rs, rd)
		}
		.clt_un {
			j.a.sltu(rd, rd, rs)
		}
		.cgt_un {
			j.a.sltu(rd, rs, rd)
		}
		.cge {
			j.a.slt(rd, rd, rs)
			j.a.xori(rd, rd, 1)
		}
		else {
			j.a.slt(rd, rs, rd)
			j.a.xori(rd, rd, 1)
		}
	}
}

// r = op(r) for neg, not and the narrowing conversions.
fn (mut j JitBody) narrow(op Op, r int) {
	match op {
		.neg {
			j.a.subu(r, r_zero, r)
		}
		.not {
			j.a.nor(r, r, r_zero)
		}
		.conv_i1 {
			j.a.sll(r, r, 24)
			j.a.sra(r, r, 24)
		}
		.conv_i2 {
			j.a.sll(r, r, 16)
			j.a.sra(r, r, 16)
		}
		.conv_u1 {
			j.a.andi(r, r, 0xff)
		}
		else {
			j.a.andi(r, r, 0xffff)
		}
	}
}

// Leaves the address of element [index] of the array in slot `arr` in $t9, failing as the
// interpreter does. `idx` is the index's slot, or -1 for the constant `imm`.
//     h = slot[arr].n; if (kind != array || h >= heap.len) fail(not_array)
//     e = &heap[h]; if (index >= e->values.len) fail(out_of_bounds)
//     $t9 = e->values.data + index * sizeof(Value)
// Unsigned comparisons reject a negative handle or index in the same test as a too-large one.
fn (mut j JitBody) element_address(arr int, idx int, imm int) {
	ly := j.ly
	j.a.lw(r_t2, j.at(arr, ly.kind), r_frame)
	j.a.xori(r_t2, r_t2, kind_array)
	j.fail_unless_zero(r_t2, .not_array)
	j.a.lw(r_t2, j.at(arr, ly.n), r_frame)
	j.a.lw(r_t3, ly.vm_heap + ly.arr_len, r_vm)
	j.a.sltu(r_t3, r_t2, r_t3)
	j.fail_if_zero(r_t3, .not_array)
	j.a.lw(r_t4, ly.vm_heap + ly.arr_data, r_vm)
	j.a.mul_const(r_t2, r_t2, ly.heap_size, r_t3)
	j.a.addu(r_t4, r_t4, r_t2) // &heap[h]
	j.a.lw(r_t3, ly.heap_values + ly.arr_len, r_t4)
	if idx >= 0 {
		j.a.lw(r_t2, j.at(idx, ly.n), r_frame)
	} else {
		j.a.li(r_t2, imm)
	}
	j.a.sltu(r_t3, r_t2, r_t3)
	j.fail_if_zero(r_t3, .out_of_bounds)
	j.a.lw(r_t9, ly.heap_values + ly.arr_data, r_t4)
	j.a.mul_const(r_t2, r_t2, ly.vsize, r_t3)
	j.a.addu(r_t9, r_t9, r_t2)
}

// ── Whole-program compilation and entry ──────────────────────────────────────

// Compiles every body into one buffer of machine code and enables compiled execution. The caller
// interprets the program instead if this fails.
fn (mut vm Vm) jit_compile() ! {
	words, starts := vm.jit_generate()!
	size := words.len * 4
	mem := C.prl_host_jit_alloc(size)
	if mem == unsafe { nil } {
		return error('jit: no memory for ${size} bytes of code')
	}
	unsafe { C.memcpy(mem, words.data, size) }
	// The code was written through the data cache; the CPU fetches instructions through the
	// instruction cache. Write the one back and invalidate the other before running any of it.
	C.prl_host_jit_flush(mem, size)
	for i in 0 .. vm.bodies.len {
		vm.jit_table[i] = unsafe { voidptr(&u8(mem) + starts[i] * 4) }
	}
	// The generated words are garbage now, but a conservative collector would keep them for as
	// long as any stale stack word looks like their address. Free them outright.
	unsafe {
		words.free()
		starts.free()
	}
	host_log('JIT ${vm.bodies.len} methods, ${size} bytes of code\n')
	vm.jit_on = true
}

// Generates code for every body, end to end, with the offset where each one starts. Separate from
// `jit_compile` so it can run, and be tested, where the code cannot.
fn (mut vm Vm) jit_generate() !([]u32, []int) {
	ly := jit_layout()
	check_array_layout(ly)!
	// The table's buffer must not move once code embeds its address: allocate it in full now.
	vm.jit_table = []voidptr{len: vm.bodies.len, init: unsafe { nil }}
	vm.jit_nparams = []int{len: vm.bodies.len + 1}
	for i, b in vm.bodies {
		vm.jit_nparams[i] = b.signature.params.len
	}
	mut estimate := 0
	for b in vm.bodies {
		estimate += b.fast.len * 24 + 64
	}
	mut words := []u32{cap: estimate}
	mut starts := []int{cap: vm.bodies.len}
	for i in 0 .. vm.bodies.len {
		starts << words.len
		words << jit_compile_body(vm, ly, i)!
	}
	return words, starts
}

// Confirms where a V array keeps its data pointer and length, by reading a real one's header.
fn check_array_layout(ly JitLayout) ! {
	probe := []u8{len: 3, cap: 7}
	header := unsafe { &u8(&probe) }
	data := unsafe { *(&voidptr(header + ly.arr_data)) }
	len := unsafe { *(&int(header + ly.arr_len)) }
	if data != probe.data || len != 3 {
		return error('jit: unexpected V array layout')
	}
}

// Runs the entry body as compiled code. Its arguments, if any, are already at slot 0.
fn (mut vm Vm) run_compiled(entry int) ! {
	vm.jit_depth = -1
	vm.jit_vals_end = unsafe { voidptr(&u8(vm.vals.data) + usize(vm.vals.len) * usize(sizeof(Value))) }
	frame := unsafe { voidptr(&vm.vals[0]) }
	if C.prl_host_jit_call(vm.jit_table[entry], voidptr(vm), frame, frame) != 0 {
		return error(vm.jit_error)
	}
}

// ── Helpers called from compiled code ────────────────────────────────────────
// Plain C-callable functions: no V Result crosses into generated code. Each returns 0 for
// success, or records the diagnostic and returns 1.

// The slot index of a pointer into the value stack.
fn (vm &Vm) slot_of(p voidptr) int {
	return int((usize(p) - usize(vm.vals.data)) / usize(sizeof(Value)))
}

// Runs one operation through `exec_slow`, the interpreter's implementation.
fn jit_slow(mut vm Vm, f &Fast, args voidptr, locals voidptr, top voidptr) int {
	_ = vm.exec_slow(f, vm.slot_of(args), vm.slot_of(locals), vm.slot_of(top)) or {
		vm.jit_error = err.msg()
		return 1
	}
	return 0
}

// Records the diagnostic for a failure detected in compiled code.
fn jit_fail(mut vm Vm, reason int, detail int) int {
	vm.jit_error = match unsafe { JitFail(reason) } {
		.not_int { 'expected an int32 value' }
		.not_array { 'expected non-null array' }
		.out_of_bounds { 'array index out of bounds' }
		.depth { 'call depth limit exceeded' }
		.budget { 'instruction budget exceeded' }
		.stack { 'value stack exhausted' }
		.bad_delegate { 'Invoke on a null or invalid delegate' }
		.bad_token { 'invalid MethodDef token 0x${u32(detail):08x}' }
		.arg_count { 'argument count mismatch: ${vm.image.methods[detail].name}' }
	}
	return 1
}

// equal_values, for beq and bne.un on anything but two ints.
fn jit_equal(a &Value, b &Value) int {
	return if equal_values(*a, *b) { 1 } else { 0 }
}
