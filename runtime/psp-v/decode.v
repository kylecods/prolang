module main

// The execution form of a method body.
//
// `il.v` decodes and checks the IL as written. This file turns it into what the interpreter
// loop actually runs: a dense opcode per instruction, every token already resolved, `nop`s
// removed, calls to constant-returning methods folded into constants, and the evaluation
// stack depth proven at every instruction. The loop in `vm.v` and the compiler in `jit.v` both
// rely on that proof, so neither checks for stack underflow or overflow per instruction.
//
// ── Reading the operation list ───────────────────────────────────────────────
//
// Each operation is one `Fast`: an opcode and up to three integer operands, `a`, `b` and `c`.
// The stack effect is written `[..., x, y] -> [..., r]`, top of stack on the right.
//
// Values carry a kind (see value.v). "int" below means a value of kind `.integer`; an
// operation that needs one and gets anything else fails with `expected an int32 value`.
// Integer arithmetic wraps at 32 bits, as the CLI's does. Conditions are ints, 0 or 1.
//
// Branch targets (`a` of every branch) are indices into the same `Fast` array.
enum Op as u8 {
	// ── Fused operations ─────────────────────────────────────────────────────
	// Produced only by `fuse`, each standing for the IL sequence shown. They behave exactly as
	// that sequence did, including its diagnostics; they exist because the interpreter's cost
	// is mostly per operation, not per unit of work.

	// `ldarg a; ldfld`. Pushes field `b` of the struct in argument `a`, which must be a struct of
	// type `c` (or a managed address of one). [...] -> [..., field]
	ldarg_fld
	// `ldc a; add|sub|mul`. [..., x] -> [..., x OP a]. x must be an int.
	add_imm
	sub_imm
	mul_imm
	// `ldc a; clt|cgt`. [..., x] -> [..., x < a] or [..., x > a], signed. x must be an int.
	clt_imm
	cgt_imm
	// `ldc a; ceq`. [..., x] -> [..., 1 if x is the int a, else 0]. Never fails: comparing any
	// other kind with an int is simply unequal.
	ceq_imm
	// `ldc a; clt; ldc 0; ceq` and `ldc a; cgt; ldc 0; ceq`: how the compiler spells x >= a and
	// x <= a. [..., x] -> [..., x >= a] or [..., x <= a], signed.
	cge_imm
	cle_imm
	// `clt; ldc 0; ceq` and `cgt; ldc 0; ceq`. [..., x, y] -> [..., x >= y] or [..., x <= y].
	cge
	cle
	// `ldc a; ldelem` with no narrowing. [..., array] -> [..., array[a]]. Fails with `expected
	// non-null array` or `array index out of bounds`.
	ldelem_imm

	// ── Arguments, locals and constants ──────────────────────────────────────

	// [...] -> [..., argument a]
	ldarg
	// [...] -> [..., int a]
	ldc
	// [...] -> [..., local a]
	ldloc
	// [..., v] -> [...]; local a = v
	stloc
	// [..., v] -> [...]; argument a = v
	starg
	// [...] -> [..., null]
	ldnull
	// [...] -> [..., literal string a]. Literals are decoded once, at load.
	ldstr
	// [..., v] -> [...]
	pop
	// [..., v] -> [..., v, v]
	dup

	// ── Calls ────────────────────────────────────────────────────────────────

	// Calls method body `a` with the top `b` values as its arguments, the deepest first.
	// [..., arg0 .. argN] -> [..., result] when `c` is 1, else [...]. The arguments stay where
	// they are and become the callee's argument slots. Fails with `call depth limit exceeded`
	// past 128 active calls.
	call
	// Calls a runtime or BCL method: native call site `a` (see NativeSite), `b` arguments
	// (including `this` for an instance method), `c` = 1 if it returns a value.
	call_native
	// Delegate Invoke. The delegate is below its `b - 1` arguments:
	// [..., delegate, arg0 .. argN] -> [..., result] when `c` is 1, else [...].
	// Fails with `Invoke on a null or invalid delegate`.
	invoke
	// Returns: the top value if the method returns one. The frame is cleared.
	ret

	// ── Branches ─────────────────────────────────────────────────────────────

	// Jumps to `a`.
	br
	// [..., v] -> [...]; jumps to `a` if v is false / true. False is null or the int 0;
	// anything else, a string or a struct included, is true.
	brfalse
	brtrue
	// [..., x, y] -> [...]; jumps to `a` if x and y are equal / not equal (see `equal_values`).
	beq
	bne_un
	// [..., x, y] -> [...]; jumps to `a` if x >= y, x > y, x <= y or x < y. Both must be ints.
	// The `_un` forms compare as unsigned.
	bge
	bgt
	ble
	blt
	bge_un
	bgt_un
	ble_un
	blt_un

	// ── Arithmetic and comparison ────────────────────────────────────────────
	// Binary operations are [..., x, y] -> [..., x OP y]; both operands must be ints.

	add
	sub
	mul
	// Signed and unsigned division and remainder. Fail with `division by zero`; signed division
	// of -2147483648 by -1 fails with `int32 division overflow`. `b` holds the IL opcode.
	div
	div_un
	rem
	rem_un
	and_
	or_
	xor
	// Shifts by y & 31. `shr` is arithmetic, `shr_un` logical.
	shl
	shr
	shr_un
	// [..., x, y] -> [..., x < y] or [..., x > y]; `_un` compares as unsigned.
	clt
	cgt
	clt_un
	cgt_un
	// [..., x, y] -> [..., x == y] for any two values (see `equal_values`), except that two
	// strings fail with `string reference identity comparison is unsupported`.
	ceq
	// [..., x] -> [..., -x] / [..., ~x]
	neg
	not
	// [..., x] -> [..., x narrowed to 8 or 16 bits, then widened back, signed or unsigned].
	conv_i1
	conv_i2
	conv_u1
	conv_u2
	// [..., x] -> [..., x]. The identity on an int32, but still rejects anything else.
	conv_i4

	// ── Arrays ───────────────────────────────────────────────────────────────

	// [..., count] -> [..., new array]. `a` is the element type; elements start at their zero
	// value. Fails with `array heap limit exceeded`.
	newarr
	// [..., array] -> [..., length]
	ldlen
	// [..., array, index] -> [..., element]. `a` narrows the element: 0 none, 1 i1, 2 u1, 3 i2,
	// 4 u2. Fails with `expected non-null array` or `array index out of bounds`.
	ldelem
	// [..., array, index, v] -> [...]; array[index] = v, first narrowed by `a`: 0 none, 1 u1,
	// 2 u2.
	stelem
	// [..., array, index] -> [..., managed address of array[index]]
	ldelema

	// ── Structs, fields and addresses ────────────────────────────────────────
	// A struct is an immutable snapshot (see value.v), so storing to a field builds a new one.

	// [..., struct] -> [..., field]. Field index `a`, at slot `b` of a struct of type `c`. The
	// operand may also be a managed address of the struct. Anything else fails with `invalid
	// struct field access`.
	ldfld
	// [..., struct address] -> [..., address of its field]. Operands as for `ldfld`.
	ldflda
	// [..., struct address, v] -> [...]; the field is set to v through the address. Operands
	// as for `ldfld`; fails with `stfld needs a struct address` if given a struct value.
	stfld
	// [...] -> [..., static field a] / [..., v] -> [...] / [...] -> [..., its address]
	ldsfld
	stsfld
	ldsflda
	// [...] -> [..., managed address of argument a / local a]
	ldarga
	ldloca
	// [..., address] -> [..., the value it points at]
	ldobj
	// [..., address, v] -> [...]; stores v through the address
	stobj
	// [..., address] -> [...]; stores the zero value of struct type `a` through the address
	initobj

	// ── Delegates and boxes ──────────────────────────────────────────────────

	// [...] -> [..., function a], a MethodDef token: what a delegate is built from.
	ldftn
	// [..., null, function] -> [..., delegate]. Only capture-free static delegates exist.
	newobj_delegate
	// Any other constructor: fails with `unsupported constructor`, naming entry `a` of
	// Vm.unsupported. `b` is its argument count, for the stack depth proof.
	newobj_unsupported
	// [..., int] -> [..., box], for the small integer types named by Vm.texts[a].
	box
	// [..., box] -> [..., int]. Fails with `unbox.any type mismatch`.
	unbox
}

// One operation. What `a`, `b` and `c` mean depends on the opcode; see Op.
struct Fast {
	op Op
	a  int
	b  int
	c  int
}

enum NativeId {
	unknown
	psp_init
	psp_clear
	psp_fill_rect
	psp_draw_line
	psp_draw_text
	psp_swap
	psp_vsync
	psp_buttons_held
	psp_button_pressed
	out_init
	out_flush
	out_write
	str_from
	str_concat
	str_equality
	str_inequality
	str_length
	str_char
	str_substring
	str_index_of
	math_min
	math_max
	assert
	console_write
	sleep
	millis
}

struct NativeSite {
	id   NativeId
	key  string
	sig  Signature
}

fn native_id(key string) NativeId {
	return match key {
		'ProLang.Runtime.PspOps.Init:->1' { .psp_init }
		'ProLang.Runtime.PspOps.Clear:8,->1' { .psp_clear }
		'ProLang.Runtime.PspOps.FillRect:8,8,8,8,8,->1' { .psp_fill_rect }
		'ProLang.Runtime.PspOps.DrawLine:8,8,8,8,8,->1' { .psp_draw_line }
		'ProLang.Runtime.PspOps.DrawText:8,8,14,8,->1' { .psp_draw_text }
		'ProLang.Runtime.PspOps.SwapBuffers:->1' { .psp_swap }
		'ProLang.Runtime.PspOps.Vsync:->1' { .psp_vsync }
		'ProLang.Runtime.PspOps.ButtonsHeld:->8' { .psp_buttons_held }
		'ProLang.Runtime.PspOps.ButtonPressed:8,->2' { .psp_button_pressed }
		'ProLang.Runtime.Output.Initialize:->1' { .out_init }
		'ProLang.Runtime.Output.Flush:->1' { .out_flush }
		'ProLang.Runtime.Output.Write:14,->1', 'ProLang.Runtime.Output.Write:28,->1',
		'ProLang.Runtime.Output.Write:8,->1', 'ProLang.Runtime.Output.Write:9,->1',
		'ProLang.Runtime.Output.Write:2,->1', 'ProLang.Runtime.Output.Write:3,->1' {
			.out_write
		}
		'ProLang.Runtime.StringOps.From:8,->14', 'ProLang.Runtime.StringOps.From:9,->14',
		'ProLang.Runtime.StringOps.From:2,->14', 'ProLang.Runtime.StringOps.From:3,->14',
		'ProLang.Runtime.StringOps.From:28,->14' {
			.str_from
		}
		'System.String.Concat:14,14,->14', 'System.String.Concat:28,28,->14' { .str_concat }
		'System.String.op_Equality:14,14,->2' { .str_equality }
		'System.String.op_Inequality:14,14,->2' { .str_inequality }
		'System.String.get_Length:this,->8' { .str_length }
		'ProLang.Runtime.StringOps.CharAt:14,8,->3', 'ProLang.Runtime.StringOps.CharCode:14,8,->8' {
			.str_char
		}
		'ProLang.Runtime.StringOps.Substring:14,8,8,->14' { .str_substring }
		'System.String.IndexOf:this,14,->8' { .str_index_of }
		'System.Math.Min:8,8,->8' { .math_min }
		'System.Math.Max:8,8,->8' { .math_max }
		'ProLang.Runtime.TestOps.Assert:2,14,->1' { .assert }
		'ProLang.Runtime.ConsoleOps.Write:14,->1' { .console_write }
		'System.Threading.Thread.Sleep:8,->1' { .sleep }
		'ProLang.Runtime.TimeOps.Millis:->8' { .millis }
		else { .unknown }
	}
}

// How many values an instruction pops and pushes.
fn effect(f Fast) (int, int) {
	match f.op {
		.ldarg, .ldc, .ldloc, .ldnull, .ldstr, .ldsfld, .ldsflda, .ldarga, .ldloca, .ldftn,
		.ldarg_fld {
			return 0, 1
		}
		.add_imm, .sub_imm, .mul_imm, .clt_imm, .cgt_imm, .ceq_imm, .cge_imm, .cle_imm,
		.ldelem_imm {
			return 1, 1
		}
		.call, .call_native, .invoke {
			return f.b, f.c
		}
		.ret, .br {
			return 0, 0
		}
		.add, .sub, .mul, .div, .div_un, .rem, .rem_un, .and_, .or_, .xor, .shl, .shr, .shr_un,
		.ceq, .cgt, .cgt_un, .clt, .clt_un, .ldelem, .ldelema, .cge, .cle {
			return 2, 1
		}
		.ldfld, .ldflda, .neg, .not, .conv_i1, .conv_i2, .conv_u1, .conv_u2, .conv_i4, .ldlen,
		.newarr, .ldobj, .box, .unbox {
			return 1, 1
		}
		.pop, .stloc, .starg, .stsfld, .brfalse, .brtrue, .initobj {
			return 1, 0
		}
		.dup {
			return 1, 2
		}
		.stelem {
			return 3, 0
		}
		.stfld, .stobj, .beq, .bne_un, .bge, .bgt, .ble, .blt, .bge_un, .bgt_un, .ble_un, .blt_un {
			return 2, 0
		}
		.newobj_delegate {
			return 2, 1
		}
		.newobj_unsupported {
			return f.b, 1
		}
	}
}

fn is_branch(op Op) bool {
	return op in [.br, .brfalse, .brtrue, .beq, .bne_un, .bge, .bgt, .ble, .blt, .bge_un, .bgt_un,
		.ble_un, .blt_un]
}

// Translates one decoded body. Branch targets still index `code` (the IL instruction list);
// `remap` fixes them once every body is translated and `nop`s are gone.
fn (mut vm Vm) translate(code []Instruction, body Body) ![]Fast {
	nargs := body.signature.params.len
	mut out := []Fast{cap: code.len}
	for ins in code {
		op := ins.op
		n := ins.operand
		f := match op {
			0x00 {
				Fast{
					op: .br
					a:  -1 // a nop: dropped by `remap`
				}
			}
			0x02...0x05 { vm.local_ref(.ldarg, op - 2, nargs, 'argument')! }
			0x06...0x09 { vm.local_ref(.ldloc, op - 6, body.locals.len, 'local')! }
			0x0a...0x0d { vm.local_ref(.stloc, op - 10, body.locals.len, 'local')! }
			0x0e, 0xfe09 { vm.local_ref(.ldarg, n, nargs, 'argument')! }
			0x10, 0xfe0b { vm.local_ref(.starg, n, nargs, 'argument')! }
			0x11, 0xfe0c { vm.local_ref(.ldloc, n, body.locals.len, 'local')! }
			0x0f, 0xfe0a { vm.local_ref(.ldarga, n, nargs, 'argument')! }
			0x12, 0xfe0d { vm.local_ref(.ldloca, n, body.locals.len, 'local')! }
			0x13, 0xfe0e { vm.local_ref(.stloc, n, body.locals.len, 'local')! }
			0x14 { Fast{ op: .ldnull } }
			0x15...0x1e { Fast{ op: .ldc, a: op - 0x16 } }
			0x1f, 0x20 { Fast{ op: .ldc, a: n } }
			0x25 { Fast{ op: .dup } }
			0x26 { Fast{ op: .pop } }
			0x28, 0x6f { vm.translate_call(ins)! }
			0x2a { Fast{ op: .ret } }
			0x2b...0x44 { Fast{ op: branch_op(if op >= 0x38 { op - 13 } else { op }), a: n } }
			0x58...0x64 { Fast{ op: arithmetic_op(op), b: op } }
			0x65 { Fast{ op: .neg } }
			0x66 { Fast{ op: .not } }
			0x67 { Fast{ op: .conv_i1 } }
			0x68 { Fast{ op: .conv_i2 } }
			0xd1 { Fast{ op: .conv_u2 } }
			0xd2 { Fast{ op: .conv_u1 } }
			0x69, 0x6d, 0xd3, 0xe0 { Fast{ op: .conv_i4 } }
			0x72 {
				vm.literals << ins.literal
				Fast{
					op: .ldstr
					a:  vm.literals.len - 1
				}
			}
			0xfe06 {
				_ = vm.image.method(u32(n))!
				Fast{
					op: .ldftn
					a:  n
				}
			}
			0x73 {
				if is_delegate(ins.owner) && ins.name == '.ctor' && ins.signature.params == [28, 24] {
					Fast{
						op: .newobj_delegate
					}
				} else {
					vm.unsupported << '${ins.owner}.${ins.name}'
					Fast{
						op: .newobj_unsupported
						a:  vm.unsupported.len - 1
						b:  ins.signature.params.len
					}
				}
			}
			0xfe15 { Fast{ op: .initobj, a: n & 0xffffff } }
			0x71 { Fast{ op: .ldobj } }
			0x81 { Fast{ op: .stobj } }
			0x7b...0x7d {
				if ins.field < 0 || ins.field >= vm.field_offsets.len {
					return error('invalid field token')
				}
				// b and c pre-resolve the field's slot and the struct that declares it, which is
				// what every access checks the record against.
				Fast{
					op: if op == 0x7b {
						Op.ldfld
					} else if op == 0x7c {
						Op.ldflda
					} else {
						Op.stfld
					}
					a:  ins.field
					b:  vm.field_offsets[ins.field]
					c:  vm.field_owner[ins.field]
				}
			}
			0x7e...0x80 {
				if ins.field < 0 || ins.field >= vm.globals.len {
					return error('argument/local index out of bounds')
				}
				Fast{
					op: if op == 0x7e {
						Op.ldsfld
					} else if op == 0x7f {
						Op.ldsflda
					} else {
						Op.stsfld
					}
					a:  ins.field
				}
			}
			0x8c, 0xa5 {
				vm.texts << ins.text
				Fast{
					op: if op == 0x8c { Op.box } else { Op.unbox }
					a:  vm.texts.len - 1
				}
			}
			0x8d { Fast{ op: .newarr, a: ins.field } }
			0x8f { Fast{ op: .ldelema } }
			0x8e { Fast{ op: .ldlen } }
			0x90 { Fast{ op: .ldelem, a: 1 } }
			0x91 { Fast{ op: .ldelem, a: 2 } }
			0x92 { Fast{ op: .ldelem, a: 3 } }
			0x93 { Fast{ op: .ldelem, a: 4 } }
			0x94, 0x95, 0x9a, 0xa3 { Fast{ op: .ldelem } }
			0x9c { Fast{ op: .stelem, a: 1 } }
			0x9d { Fast{ op: .stelem, a: 2 } }
			0x9b, 0x9e, 0xa2, 0xa4 { Fast{ op: .stelem } }
			0xfe01 { Fast{ op: .ceq } }
			0xfe02 { Fast{ op: .cgt } }
			0xfe03 { Fast{ op: .cgt_un } }
			0xfe04 { Fast{ op: .clt } }
			0xfe05 { Fast{ op: .clt_un } }
			else { return error('opcode 0x${op:04x} at IL_${ins.offset:04x} has no handler') }
		}
		out << f
	}
	return out
}

fn (vm &Vm) local_ref(op Op, index int, count int, what string) !Fast {
	if index < 0 || index >= count {
		return error('${what} index out of bounds')
	}
	return Fast{
		op: op
		a:  index
	}
}

fn (mut vm Vm) translate_call(ins Instruction) !Fast {
	sig := ins.signature
	count := sig.params.len + if sig.instance { 1 } else { 0 }
	returns := if sig.returns != 1 { 1 } else { 0 }
	token := u32(ins.operand)
	if token >> 24 == 6 {
		row := int(token & 0xffffff)
		_ = vm.image.method(token)!
		return Fast{
			op: .call
			a:  row - 1
			b:  count
			c:  returns
		}
	}
	if ins.name == 'Invoke' && is_delegate(ins.owner) {
		return Fast{
			op: .invoke
			b:  count
			c:  returns
		}
	}
	vm.sites << NativeSite{
		id:   native_id(ins.key)
		key:  ins.key
		sig:  sig
	}
	return Fast{
		op: .call_native
		a:  vm.sites.len - 1
		b:  count
		c:  returns
	}
}

fn branch_op(op int) Op {
	return match op {
		0x2b { .br }
		0x2c { .brfalse }
		0x2d { .brtrue }
		0x2e { .beq }
		0x2f { .bge }
		0x30 { .bgt }
		0x31 { .ble }
		0x32 { .blt }
		0x33 { .bne_un }
		0x34 { .bge_un }
		0x35 { .bgt_un }
		0x36 { .ble_un }
		else { .blt_un }
	}
}

fn arithmetic_op(op int) Op {
	return match op {
		0x58 { .add }
		0x59 { .sub }
		0x5a { .mul }
		0x5b { .div }
		0x5c { .div_un }
		0x5d { .rem }
		0x5e { .rem_un }
		0x5f { .and_ }
		0x60 { .or_ }
		0x61 { .xor }
		0x62 { .shl }
		0x63 { .shr }
		else { .shr_un }
	}
}

// Drops the `nop`s and rewrites branch targets from IL instruction indices to positions in
// the compacted code. A branch to a `nop` lands on the instruction after it.
fn remap(code []Fast) []Fast {
	mut position := []int{len: code.len + 1}
	mut kept := 0
	for i, f in code {
		position[i] = kept
		if !(f.op == .br && f.a == -1) {
			kept++
		}
	}
	position[code.len] = kept
	mut out := []Fast{cap: kept}
	for f in code {
		if f.op == .br && f.a == -1 {
			continue
		}
		if is_branch(f.op) {
			out << Fast{
				...f
				a: position[f.a]
			}
		} else {
			out << f
		}
	}
	return out
}

// Proves the evaluation stack depth at every reachable instruction: never negative, never
// above maxstack, the same along every path into a branch target, and exactly what `ret`
// needs. Returns the error the old per-instruction checks would have raised at run time, or
// else each instruction's depth before it runs (-1 if unreachable), which is what lets the JIT
// address every stack slot at a fixed offset.
fn verify_stack(code []Fast, body Body, name string, bodies []Body) ![]int {
	mut depth := []int{len: code.len, init: -1}
	mut work := []int{}
	if code.len == 0 {
		return error('method ${name} falls through without ret')
	}
	depth[0] = 0
	work << 0
	for work.len > 0 {
		pc := work.pop()
		f := code[pc]
		d := depth[pc]
		pops, pushes := effect(f)
		if f.op == .call && f.b != bodies[f.a].signature.params.len {
			return error('argument count mismatch: ${name}')
		}
		if pops > d {
			if f.op in [.call, .call_native, .invoke] {
				return error('call stack underflow in ${name}')
			}
			return error('evaluation stack underflow in ${name}')
		}
		after := d - pops + pushes
		if after > body.max_stack {
			return error('maxstack exceeded in ${name}')
		}
		if f.op == .ret {
			if body.signature.returns == 1 && d != 0 {
				return error('non-empty stack at void return')
			}
			if body.signature.returns != 1 && d != 1 {
				return error('invalid return stack')
			}
			continue
		}
		// Successors: the branch target, then the next instruction unless the branch is
		// unconditional. At most two, so no list is allocated per instruction.
		branch := if is_branch(f.op) { f.a } else { -1 }
		fallthrough := if f.op != .br { pc + 1 } else { -1 }
		if fallthrough >= code.len {
			return error('method ${name} falls through without ret')
		}
		for target in [branch, fallthrough]! {
			if target < 0 {
				continue
			}
			if depth[target] == -1 {
				depth[target] = after
				work << target
			} else if depth[target] != after {
				return error('inconsistent evaluation stack depth at a branch target in ${name}')
			}
		}
	}
	return depth
}

// The value a parameterless, branch-free method always returns, if it is a constant.
//
// `ldc.i4 30; ret` is the whole of `Ui->stride()`, and the UI toolkit calls functions like it
// on nearly every field access: about half the calls the cube makes. Replacing such a call
// with the constant is exact, because a method with no arguments, no locals and no other
// instructions can neither observe nor change anything.
fn constant_result(code []Fast, body Body, known map[int]int) ?int {
	if body.signature.params.len != 0 || body.locals.len != 0 || body.signature.returns != 8 {
		return none
	}
	mut stack := []int{}
	for f in code {
		match f.op {
			.ldc {
				stack << f.a
			}
			.call {
				if f.b != 0 || f.a !in known {
					return none
				}
				stack << known[f.a]
			}
			.add, .sub, .mul, .and_, .or_, .xor, .shl, .shr, .shr_un {
				if stack.len < 2 {
					return none
				}
				b := stack.pop()
				a := stack.pop()
				stack << binary(match f.op {
					.add { 0x58 }
					.sub { 0x59 }
					.mul { 0x5a }
					.and_ { 0x5f }
					.or_ { 0x60 }
					.xor { 0x61 }
					.shl { 0x62 }
					.shr { 0x63 }
					else { 0x64 }
				}, a, b) or { return none }
			}
			.ret {
				return if stack.len == 1 { stack[0] } else { none }
			}
			else {
				return none
			}
		}
	}
	return none
}

// Rewrites every call to a constant method as the constant. Repeats until nothing changes,
// because a constant may be defined in terms of another (`cube_proj_base` is
// `cube_sin_base() + 65`).
fn fold_constants(mut bodies []Body) {
	mut known := map[int]int{}
	for {
		mut changed := false
		for i, b in bodies {
			if i in known {
				continue
			}
			if v := constant_result(b.fast, b, known) {
				known[i] = v
				changed = true
			}
		}
		if !changed {
			break
		}
	}
	if known.len == 0 {
		return
	}
	for mut b in bodies {
		for j in 0 .. b.fast.len {
			f := b.fast[j]
			if f.op == .call && f.a in known {
				b.fast[j] = Fast{
					op: .ldc
					a:  known[f.a]
				}
			}
		}
	}
}

// Whether instruction `j` exists and no branch lands on it, so it can be merged into the
// instruction before it without changing where any path enters.
fn mergeable(code []Fast, target []bool, j int) bool {
	return j < code.len && !target[j]
}

fn is_zero(f Fast) bool {
	return f.op == .ldc && f.a == 0
}

// Fuses the instruction sequences the ProLang compiler emits most, each into one operation that
// does exactly what the sequence did. The interpreter's cost is mostly per instruction rather
// than per unit of work, so this is worth about a quarter of the instructions the cube executes.
//
// A sequence is fused only when no branch lands inside it. A branch to its first instruction
// still lands on the fused operation, which performs the whole sequence, as the original did.
fn fuse(code []Fast) []Fast {
	mut target := []bool{len: code.len + 1}
	for f in code {
		if is_branch(f.op) {
			target[f.a] = true
		}
	}
	mut out := code.clone()
	gone := Fast{
		op: .br
		a:  -1 // dropped by `remap`, like a nop
	}
	mut i := 0
	for i < out.len {
		f := out[i]
		mut used := 1
		if f.op == .ldc && mergeable(out, target, i + 3) && mergeable(out, target, i + 2)
			&& mergeable(out, target, i + 1) && out[i + 1].op in [.clt, .cgt] && is_zero(out[i + 2])
			&& out[i + 3].op == .ceq {
			// x >= K, as the compiler spells it: !(x < K).
			out[i] = Fast{
				op: if out[i + 1].op == .clt { Op.cge_imm } else { Op.cle_imm }
				a:  f.a
			}
			used = 4
		} else if f.op in [.clt, .cgt] && mergeable(out, target, i + 2)
			&& mergeable(out, target, i + 1) && is_zero(out[i + 1]) && out[i + 2].op == .ceq {
			out[i] = Fast{
				op: if f.op == .clt { Op.cge } else { Op.cle }
			}
			used = 3
		} else if f.op == .ldc && mergeable(out, target, i + 1)
			&& out[i + 1].op in [.add, .sub, .mul, .clt, .cgt, .ceq] {
			out[i] = Fast{
				op: match out[i + 1].op {
					.add { Op.add_imm }
					.sub { Op.sub_imm }
					.mul { Op.mul_imm }
					.clt { Op.clt_imm }
					.cgt { Op.cgt_imm }
					else { Op.ceq_imm }
				}
				a:  f.a
			}
			used = 2
		} else if f.op == .ldc && mergeable(out, target, i + 1) && out[i + 1].op == .ldelem
			&& out[i + 1].a == 0 {
			out[i] = Fast{
				op: .ldelem_imm
				a:  f.a
			}
			used = 2
		} else if f.op == .ldarg && mergeable(out, target, i + 1) && out[i + 1].op == .ldfld {
			out[i] = Fast{
				op: .ldarg_fld
				a:  f.a
				b:  out[i + 1].b
				c:  out[i + 1].c
			}
			used = 2
		} else if f.op == .dup && mergeable(out, target, i + 2) && mergeable(out, target, i + 1)
			&& out[i + 1].op in [.stloc, .starg, .stsfld] && out[i + 2].op == .pop {
			// An assignment used as a statement: the copy dup made is stored and the original
			// is popped, which leaves the same stack as storing the original.
			out[i] = out[i + 1]
			used = 3
		} else if f.op == .ldnull && mergeable(out, target, i + 1) && out[i + 1].op == .pop {
			// Pushes and discards a null: nothing at all. A branch to the ldnull lands on what
			// follows, which is where the pair would have left it.
			out[i] = gone
			used = 2
		}
		for j in 1 .. used {
			out[i + j] = gone
		}
		i += used
	}
	return remap(out)
}

// ── Inlining ─────────────────────────────────────────────────────────────────

// The largest callee, in operations, that `inline_calls` copies into its callers.
const inline_limit = 40

// Whether `b` can be copied into its callers: small, calling no ProLang method (so a copy never
// recurses), and with locals whose zero value is the int 0 or null, which the copy re-creates
// with `ldc 0` or `ldnull` each time it runs.
fn inlinable(b Body) bool {
	if b.fast.len == 0 || b.fast.len > inline_limit {
		return false
	}
	for f in b.fast {
		if f.op in [.call, .invoke] {
			return false
		}
	}
	for v in b.local_init {
		if v.kind !in [.null_value, .integer] || v.n != 0 || v.data != unsafe { nil } {
			return false
		}
	}
	return true
}

// Replaces calls to small leaf methods with a copy of the method.
//
// `Ui->get`, a bounds-checked read of one int, is called 650 times a frame by the cube; the call
// costs more than the read. A copy at the call site:
//
//     stloc s+N-1 .. stloc s+0     pop the arguments into scratch locals, last first
//     ldc 0 / ldnull; stloc ...    re-create the callee's own locals at their zero value
//     <callee body>                ldarg i -> ldloc s+i, its locals -> s+N+j, ret -> br end
//   end:
//
// Every copy in a caller uses the same scratch locals starting at `s`: a copy contains no calls,
// so two copies never run at once. Runs to a fixed point, so a method that becomes a leaf once
// its own calls are copied in (`Ui->kind` is a `Ui->get`) is copied in turn.
//
// The one observable difference: a copied method no longer counts towards the 128-call depth
// limit, which can only let a program that was at the limit go one level deeper.
fn inline_calls(mut bodies []Body) {
	for _ in 0 .. 4 {
		mut ok := []bool{len: bodies.len}
		for i, b in bodies {
			ok[i] = inlinable(b)
		}
		mut changed := false
		callees := bodies.clone()
		for i in 0 .. bodies.len {
			if inline_into(mut bodies[i], callees, ok) {
				changed = true
			}
		}
		if !changed {
			break
		}
	}
}

fn inline_into(mut caller Body, bodies []Body, ok []bool) bool {
	base := caller.locals.len
	mut out := []Fast{cap: caller.fast.len * 2}
	mut position := []int{len: caller.fast.len + 1}
	mut caller_branches := []int{}
	mut scratch := 0
	mut stack := 0
	for i, f in caller.fast {
		position[i] = out.len
		if f.op != .call || !ok[f.a] || f.b != bodies[f.a].signature.params.len {
			if is_branch(f.op) {
				caller_branches << out.len
			}
			out << f
			continue
		}
		callee := bodies[f.a]
		nargs := f.b
		for k := nargs - 1; k >= 0; k-- {
			out << Fast{
				op: .stloc
				a:  base + k
			}
		}
		for j, v in callee.local_init {
			// The callee's scratch locals are always written before they are read.
			if j >= callee.scratch_base {
				break
			}
			out << Fast{
				op: if v.kind == .integer { Op.ldc } else { Op.ldnull }
			}
			out << Fast{
				op: .stloc
				a:  base + nargs + j
			}
		}
		start := out.len
		end := start + callee.fast.len
		for k, g in callee.fast {
			out << match g.op {
				.ldarg {
					Fast{
						op: .ldloc
						a:  base + g.a
					}
				}
				.starg {
					Fast{
						op: .stloc
						a:  base + g.a
					}
				}
				.ldarga {
					Fast{
						op: .ldloca
						a:  base + g.a
					}
				}
				.ldloc, .stloc, .ldloca {
					Fast{
						...g
						a: base + nargs + g.a
					}
				}
				.ret {
					// The final ret falls through to `end` anyway, so it is dropped (remap
					// removes the marker); any earlier one jumps there.
					Fast{
						op: .br
						a:  if k == callee.fast.len - 1 { -1 } else { end }
					}
				}
				else {
					if is_branch(g.op) {
						Fast{
							...g
							a: start + g.a
						}
					} else {
						g
					}
				}
			}
		}
		if nargs + callee.locals.len > scratch {
			scratch = nargs + callee.locals.len
		}
		if callee.max_stack > stack {
			stack = callee.max_stack
		}
	}
	if scratch == 0 {
		return false
	}
	position[caller.fast.len] = out.len
	for k in caller_branches {
		out[k] = Fast{
			...out[k]
			a: position[out[k].a]
		}
	}
	for _ in 0 .. scratch {
		caller.locals << 0x1c
		caller.local_init << Value{}
	}
	// An upper bound: the callee's stack sits on top of whatever the caller had below the call.
	caller.max_stack += stack
	caller.fast = remap(out)
	return true
}
