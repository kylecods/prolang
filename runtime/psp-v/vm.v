module main

const max_steps = 10000000
const max_depth = 128
const max_array_elements = 262144
const max_output = 65536
// Every active frame's arguments, locals and evaluation stack, end to end. 16 bytes a slot on
// the PSP, so 256 KiB.
const value_stack_size = 16384

// One active method. Its arguments, locals and evaluation stack are consecutive ranges of
// `Vm.vals`: a call's arguments are already on the caller's evaluation stack, and they become
// the callee's argument slots where they lie, with no copy.
struct CallFrame {
mut:
	body   int // index into Vm.bodies
	pc     int // where the caller resumes; saved only while a callee runs
	args   int // slot of argument 0
	locals int // slot of local 0
	ret    int // where the result goes: `args`, or the delegate's slot for Invoke
	limit  int // one past the frame's last slot
}

struct Vm {
	image Image
mut:
	bodies             []Body
	globals            []Value
	heap               []HeapArray
	boxes              []Value
	allocated_elements int
	steps              int
	calls              int
	output             string
	field_types        []int
	field_offsets      []int
	field_owner        []int // the TypeDef row declaring each instance field
	type_fields        [][]int
	delegates          int
	vals               []Value
	frames             []CallFrame
	sites              []NativeSite
	literals           []Value
	texts              []string
	unsupported        []string
	// Compiled code (jit.v). Read by generated MIPS through fixed offsets, so these are plain
	// words and arrays whose buffers never move once compilation is done.
	jit_on       bool
	jit_table    []voidptr // entry address of each body's code
	jit_nparams  []int     // each body's parameter count, for Invoke's argument check
	jit_depth    int       // active compiled frames, less one
	jit_vals_end voidptr   // one past the last value slot, for the frame-size check
	jit_error    string    // the diagnostic of a failed compiled run
}

fn new_vm(im &Image) !Vm {
	mut vm := Vm{
		image: im
	}
	for i in 1 .. im.rows[4] + 1 {
		mut f := im.row(4, i)!
		flags := f.word()!
		_ = flags
		_ = f.index(im.string_width)!
		mut sig := Reader{
			data: im.blob(f.index(im.blob_width)!)!
		}
		if sig.byte()! != 6 {
			return error('invalid field signature')
		}
		vm.field_types << read_type(mut sig)!
		vm.field_offsets << -1
		vm.field_owner << 0
		vm.globals << Value{}
	}
	vm.type_fields = [][]int{len: im.rows[2] + 1}
	for ty in 1 .. im.rows[2] + 1 {
		mut row := im.row(2, ty)!
		_ = row.take(4 + 2 * im.string_width + im.coded(2, [2, 1, 27]))!
		first := row.index(im.table_width(4))!
		mut last := im.rows[4] + 1
		if ty < im.rows[2] {
			mut next := im.row(2, ty + 1)!
			_ = next.take(4 + 2 * im.string_width + im.coded(2, [2, 1, 27]))!
			last = next.index(im.table_width(4))!
		}
		if first < 1 || last < first || last > im.rows[4] + 1 {
			return error('invalid TypeDef field list')
		}
		for f in first .. last {
			mut field := im.row(4, f)!
			if field.word()! & 0x10 != 0 {
				continue
			}
			vm.field_offsets[f - 1] = vm.type_fields[ty].len
			vm.field_owner[f - 1] = ty
			vm.type_fields[ty] << f - 1
		}
	}
	for i, t in vm.field_types {
		vm.globals[i] = vm.zero(t, 0)!
	}
	for m in im.methods {
		body := im.body(m)!
		mut code := body.code.clone()
		for j, ins in code {
			mut cached := ins
			if ins.op >= 0x2b && ins.op <= 0x44 {
				cached = Instruction{
					...cached
					operand: instruction_index(code, ins.operand)!
				}
			}
			if ins.op in [0x28, 0x6f, 0x73] {
				if u32(ins.operand) >> 24 == 6 {
					cached.signature = im.signature(im.method(u32(ins.operand))!.signature)!
				} else {
					owner, name, sig := im.member(u32(ins.operand))!
					cached.owner = owner
					cached.name = name
					cached.signature = sig
					cached.key = native_key(owner, name, sig)
				}
			}
			if ins.op >= 0x7b && ins.op <= 0x80 {
				cached.field = im.field_index(u32(ins.operand))!
			}
			if ins.op == 0x72 {
				cached.literal = text_value(im.literal(u32(ins.operand))!)
			}
			if ins.op in [0x8c, 0xa5] {
				cached.text = im.type_name(u32(ins.operand))!
			}
			if ins.op == 0x8d {
				cached.field = im.array_element(u32(ins.operand))!
			}
			code[j] = cached
		}
		fast := vm.translate(code, body) or { return error('${m.name}: ${err}') }
		mut local_init := []Value{}
		for t in body.locals {
			local_init << vm.zero(t, 0)!
		}
		vm.bodies << Body{
			...body
			code:         code
			fast:         remap(fast)
			local_init:   local_init
			scratch_base: body.locals.len
		}
	}
	fold_constants(mut vm.bodies)
	// Inlining and fusion come after folding, so a folded call takes part in both: `node *
	// Ui->stride()` becomes one mul_imm, and a method that only called constants is a leaf.
	inline_calls(mut vm.bodies)
	for mut b in vm.bodies {
		b.fast = fuse(b.fast)
	}
	for i, b in vm.bodies {
		_ = verify_stack(b.fast, b, im.methods[i].name, vm.bodies)!
	}
	return vm
}

fn (vm &Vm) zero(t int, depth int) !Value {
	if depth > 32 {
		return error('recursive or excessively nested struct')
	}
	if t & 0xfff00000 != 0x100000 {
		return default_value(t)
	}
	row := t & 0xfffff
	if row < 1 || row >= vm.type_fields.len {
		return error('invalid struct type')
	}
	mut fields := []Value{}
	for f in vm.type_fields[row] {
		fields << vm.zero(vm.field_types[f], depth + 1)!
	}
	return record_value(row, fields)
}

fn (im &Image) array_element(token u32) !int {
	if token >> 24 == 2 {
		return 0x100000 | int(token & 0xffffff)
	}
	ty := im.type_name(token)!
	return match ty {
		'System.Int32' { 8 }
		'System.UInt32' { 9 }
		'System.Boolean' { 2 }
		'System.Char' { 3 }
		'System.Byte' { 5 }
		'System.String' { 14 }
		'System.Object' { 28 }
		else { return error('unsupported array element ${ty}') }
	}
}

fn (mut vm Vm) new_array(element int, count int) !Value {
	if count < 0 || count > max_array_elements - vm.allocated_elements {
		return error('array heap limit exceeded')
	}
	vm.allocated_elements += count
	mut values := []Value{cap: count}
	for _ in 0 .. count {
		values << vm.zero(element, 0)!
	}
	vm.heap << HeapArray{
		element: element
		values:  values
	}
	return Value{
		kind: .array_ref
		n:    vm.heap.len - 1
	}
}

fn (vm &Vm) array_index(a Value, index int) !int {
	if a.kind != .array_ref || a.n < 0 || a.n >= vm.heap.len {
		return error('expected non-null array')
	}
	if index < 0 || index >= vm.heap[a.n].values.len {
		return error('array index out of bounds')
	}
	return a.n
}

fn (mut vm Vm) run(args []string) ! {
	_ = vm.image.method(vm.image.entry)!
	entry := int(vm.image.entry & 0xffffff) - 1
	signature := vm.bodies[entry].signature
	if vm.vals.len == 0 {
		vm.vals = []Value{len: value_stack_size}
		vm.frames = []CallFrame{len: max_depth}
	}
	mut count := 0
	if signature.params.len == 1 && signature.params[0] == 0x8e {
		a := vm.new_array(0x0e, args.len)!
		for i, s in args {
			vm.heap[a.n].values[i] = text_value(utf16(s))
		}
		vm.vals[0] = a
		count = 1
	} else if signature.params.len != 0 {
		return error('entry point must take no arguments or string[]')
	}
	if count != signature.params.len {
		return error('argument count mismatch: ${vm.image.methods[entry].name}')
	}
	if vm.jit_on {
		vm.run_compiled(entry) or {
			vm.clear_values()
			return err
		}
	} else {
		_ = vm.execute(entry, count) or {
			vm.clear_values()
			return err
		}
	}
	// Compiled code clears only the pointer word of each slot it releases (see jit.v). Leave the
	// value stack as the interpreter would, wholly empty, whichever engine ran.
	vm.clear_values()
}

fn (mut vm Vm) clear_values() {
	unsafe { C.memset(vm.vals.data, 0, usize(vm.vals.len) * usize(sizeof(Value))) }
}

// Runs a method and everything it calls, with an explicit frame stack rather than recursion.
//
// The decoder has proved the evaluation stack depth at every instruction (see verify_stack),
// so nothing here checks for underflow or for overflowing the frame. Values are still checked
// for kind wherever the old interpreter checked them, with the same diagnostics.
//
// Only the frequent operations are handled here, inline; everything else goes to `exec_slow`,
// which the compiled code (jit.v) uses too, so a rare operation has one implementation.
@[direct_array_access]
fn (mut vm Vm) execute(entry int, nargs int) !Value {
	// Aliases, not copies: these share the VM's buffers, and stay in registers.
	mut vals := unsafe { vm.vals }
	mut frames := unsafe { vm.frames }
	mut steps := vm.steps
	mut depth := 0
	mut fr := CallFrame{
		body: entry
	}
	vm.calls++
	fr.locals = nargs
	fr.limit = vm.enter(entry, fr.locals)!
	mut code := unsafe { vm.bodies[entry].fast }
	mut sp := fr.locals + vm.bodies[entry].locals.len
	mut pc := 0
	for {
		steps++
		if steps > max_steps {
			return error('instruction budget exceeded')
		}
		f := code[pc]
		pc++
		match f.op {
			.ldarg {
				vals[sp] = vals[fr.args + f.a]
				sp++
			}
			.ldarg_fld {
				// The common case inline: a struct value of the right type. A managed address,
				// or anything invalid, goes the long way.
				obj := vals[fr.args + f.a]
				if obj.kind == .record && obj.n == f.c && f.b >= 0 && f.b < obj.data.fields.len {
					vals[sp] = obj.data.fields[f.b]
					sp++
				} else {
					sp = vm.exec_slow(unsafe { &code[pc - 1] }, fr.args, fr.locals, sp)!
				}
			}
			.add_imm, .sub_imm, .mul_imm {
				a := vals[sp - 1]
				if a.kind != .integer {
					return error('expected an int32 value')
				}
				vals[sp - 1] = integer(match f.op {
					.add_imm { int(u32(a.n) + u32(f.a)) }
					.sub_imm { int(u32(a.n) - u32(f.a)) }
					else { int(u32(a.n) * u32(f.a)) }
				})
			}
			.clt_imm, .cgt_imm, .cge_imm, .cle_imm {
				a := vals[sp - 1]
				if a.kind != .integer {
					return error('expected an int32 value')
				}
				yes := match f.op {
					.clt_imm { a.n < f.a }
					.cgt_imm { a.n > f.a }
					.cge_imm { a.n >= f.a }
					else { a.n <= f.a }
				}
				vals[sp - 1] = integer(if yes { 1 } else { 0 })
			}
			.ceq_imm {
				a := vals[sp - 1]
				vals[sp - 1] = integer(if a.kind == .integer && a.n == f.a { 1 } else { 0 })
			}
			.cge, .cle {
				a := vals[sp - 2]
				b := vals[sp - 1]
				if a.kind != .integer || b.kind != .integer {
					return error('expected an int32 value')
				}
				sp--
				yes := if f.op == .cge { a.n >= b.n } else { a.n <= b.n }
				vals[sp - 1] = integer(if yes { 1 } else { 0 })
			}
			.ldelem_imm {
				a := vals[sp - 1]
				if a.kind != .array_ref || a.n < 0 || a.n >= vm.heap.len {
					return error('expected non-null array')
				}
				if f.a < 0 || f.a >= vm.heap[a.n].values.len {
					return error('array index out of bounds')
				}
				vals[sp - 1] = vm.heap[a.n].values[f.a]
			}
			.ldc {
				vals[sp] = Value{
					kind: .integer
					n:    f.a
				}
				sp++
			}
			.call, .invoke {
				mut callee := f.a
				mut ret := sp - f.b
				mut base := ret
				if f.op == .invoke {
					d := vals[ret]
					if d.kind != .delegate_value {
						return error('Invoke on a null or invalid delegate')
					}
					callee = int(u32(d.n) & 0xffffff) - 1
					if callee < 0 || callee >= vm.bodies.len {
						return error('invalid MethodDef token 0x${u32(d.n):08x}')
					}
					base = ret + 1
					if f.b - 1 != vm.bodies[callee].signature.params.len {
						return error('argument count mismatch: ${vm.image.methods[callee].name}')
					}
				}
				if depth + 1 >= max_depth {
					return error('call depth limit exceeded')
				}
				vm.calls++
				fr.pc = pc
				frames[depth] = fr
				depth++
				fr = CallFrame{
					body:   callee
					args:   base
					locals: sp
					ret:    ret
				}
				fr.limit = vm.enter(callee, sp)!
				code = unsafe { vm.bodies[callee].fast }
				sp += vm.bodies[callee].locals.len
				pc = 0
			}
			.ret {
				returns := vm.bodies[fr.body].signature.returns != 1
				result := if returns { vals[sp - 1] } else { Value{} }
				slot := fr.ret
				// Clear the frame so its dead values do not keep garbage alive.
				unsafe {
					C.memset(&vals[slot], 0, usize(fr.limit - slot) * usize(sizeof(Value)))
				}
				if depth == 0 {
					vm.steps = steps
					return result
				}
				depth--
				fr = frames[depth]
				code = unsafe { vm.bodies[fr.body].fast }
				pc = fr.pc
				sp = slot
				if returns {
					vals[sp] = result
					sp++
				}
			}
			.ldloc {
				vals[sp] = vals[fr.locals + f.a]
				sp++
			}
			.add, .sub, .mul, .and_, .or_ {
				a := vals[sp - 2]
				b := vals[sp - 1]
				if a.kind != .integer || b.kind != .integer {
					return error('expected an int32 value')
				}
				sp--
				vals[sp - 1] = integer(match f.op {
					.add { int(u32(a.n) + u32(b.n)) }
					.sub { int(u32(a.n) - u32(b.n)) }
					.mul { int(u32(a.n) * u32(b.n)) }
					.and_ { a.n & b.n }
					else { a.n | b.n }
				})
			}
			.ldfld {
				obj := vals[sp - 1]
				if obj.kind == .record && obj.n == f.c && f.b >= 0 && f.b < obj.data.fields.len {
					vals[sp - 1] = obj.data.fields[f.b]
				} else {
					sp = vm.exec_slow(unsafe { &code[pc - 1] }, fr.args, fr.locals, sp)!
				}
			}
			.clt, .cgt, .clt_un, .cgt_un {
				a := vals[sp - 2]
				b := vals[sp - 1]
				if a.kind != .integer || b.kind != .integer {
					return error('expected an int32 value')
				}
				sp--
				yes := match f.op {
					.clt { a.n < b.n }
					.cgt { a.n > b.n }
					.clt_un { u32(a.n) < u32(b.n) }
					else { u32(a.n) > u32(b.n) }
				}
				vals[sp - 1] = integer(if yes { 1 } else { 0 })
			}
			.pop {
				sp--
			}
			.ldelem {
				i := vals[sp - 1]
				a := vals[sp - 2]
				if i.kind != .integer {
					return error('expected an int32 value')
				}
				if a.kind != .array_ref || a.n < 0 || a.n >= vm.heap.len {
					return error('expected non-null array')
				}
				if i.n < 0 || i.n >= vm.heap[a.n].values.len {
					return error('array index out of bounds')
				}
				mut v := vm.heap[a.n].values[i.n]
				if f.a != 0 {
					x := v.int_value()!
					v = integer(match f.a {
						1 { int(i8(x)) }
						2 { int(u8(x)) }
						3 { int(i16(x)) }
						else { int(u16(x)) }
					})
				}
				sp--
				vals[sp - 1] = v
			}
			.ceq {
				a := vals[sp - 2]
				b := vals[sp - 1]
				if a.kind == .integer && b.kind == .integer {
					sp--
					vals[sp - 1] = integer(if a.n == b.n { 1 } else { 0 })
				} else {
					sp = vm.exec_slow(unsafe { &code[pc - 1] }, fr.args, fr.locals, sp)!
				}
			}
			.brfalse, .brtrue {
				sp--
				v := vals[sp]
				truth := v.kind != .null_value && (v.kind != .integer || v.n != 0)
				if truth == (f.op == .brtrue) {
					pc = f.a
				}
			}
			.stloc {
				sp--
				vals[fr.locals + f.a] = vals[sp]
			}
			.ldnull {
				vals[sp] = Value{}
				sp++
			}
			.stelem {
				mut v := vals[sp - 1]
				index := vals[sp - 2].int_value()!
				a := vals[sp - 3]
				sp -= 3
				h := vm.array_index(a, index)!
				if f.a == 1 {
					v = integer(int(u8(v.int_value()!)))
				} else if f.a == 2 {
					v = integer(int(u16(v.int_value()!)))
				}
				vm.heap[h].values[index] = v
			}
			.dup {
				vals[sp] = vals[sp - 1]
				sp++
			}
			.br {
				pc = f.a
			}
			.beq, .bne_un {
				a := vals[sp - 2]
				b := vals[sp - 1]
				sp -= 2
				same := if a.kind == .integer && b.kind == .integer {
					a.n == b.n
				} else {
					equal_values(a, b)
				}
				if same == (f.op == .beq) {
					pc = f.a
				}
			}
			.bge, .bgt, .ble, .blt, .bge_un, .bgt_un, .ble_un, .blt_un {
				a := vals[sp - 2]
				b := vals[sp - 1]
				if a.kind != .integer || b.kind != .integer {
					return error('expected an int32 value')
				}
				sp -= 2
				x := a.n
				y := b.n
				take := match f.op {
					.bge { x >= y }
					.bgt { x > y }
					.ble { x <= y }
					.blt { x < y }
					.bge_un { u32(x) >= u32(y) }
					.bgt_un { u32(x) > u32(y) }
					.ble_un { u32(x) <= u32(y) }
					else { u32(x) < u32(y) }
				}
				if take {
					pc = f.a
				}
			}
			.ldstr {
				vals[sp] = vm.literals[f.a]
				sp++
			}
			.starg {
				sp--
				vals[fr.args + f.a] = vals[sp]
			}
			.ldsfld {
				vals[sp] = vm.globals[f.a]
				sp++
			}
			.stsfld {
				sp--
				vm.globals[f.a] = vals[sp]
			}
			else {
				// A native call may swap buffers, which reads and resets the step count.
				vm.steps = steps
				sp = vm.exec_slow(unsafe { &code[pc - 1] }, fr.args, fr.locals, sp)!
				steps = vm.steps
			}
		}
	}
	return error('unreachable')
}

// Performs one operation that the interpreter loop and the compiled code do not handle inline:
// the rare ones, and the unusual cases of a few frequent ones (`ldfld` through a managed address,
// `ceq` on anything but two ints). It is the only implementation of those, shared by both
// engines, so they cannot disagree. `args` and `locals` are the frame's first argument and local
// slots, `sp` one past the top of its evaluation stack; it returns the new `sp`.
//
// Control flow (calls, returns, branches) never comes here: it is all inline in both engines.
@[direct_array_access]
fn (mut vm Vm) exec_slow(f &Fast, args int, locals int, sp int) !int {
	mut vals := unsafe { vm.vals }
	match f.op {
		.ldarg_fld, .ldfld, .ldflda {
			obj := if f.op == .ldarg_fld { vals[args + f.a] } else { vals[sp - 1] }
			record := if obj.kind == .address { dereference(obj)! } else { obj }
			if record.kind != .record || record.n != f.c || f.b < 0
				|| f.b >= record.data.fields.len {
				return error('invalid struct field access')
			}
			if f.op == .ldarg_fld {
				vals[sp] = record.data.fields[f.b]
				return sp + 1
			}
			vals[sp - 1] = if f.op == .ldfld {
				record.data.fields[f.b]
			} else {
				field_address(obj, f.b)!
			}
		}
		.ceq {
			a := vals[sp - 2]
			b := vals[sp - 1]
			mut yes := false
			if a.kind == .integer && b.kind == .integer {
				yes = a.n == b.n
			} else {
				if a.kind == .text && b.kind == .text {
					return error('string reference identity comparison is unsupported')
				}
				yes = equal_values(a, b)
			}
			vals[sp - 2] = integer(if yes { 1 } else { 0 })
			return sp - 1
		}
		.div, .div_un, .rem, .rem_un, .xor, .shl, .shr, .shr_un {
			b := vals[sp - 1].int_value()!
			a := vals[sp - 2].int_value()!
			vals[sp - 2] = integer(binary(f.b, a, b)!)
			return sp - 1
		}
		.neg, .not {
			a := vals[sp - 1].int_value()!
			vals[sp - 1] = integer(if f.op == .neg { int(u32(0) - u32(a)) } else { ~a })
		}
		.conv_i1, .conv_i2, .conv_u1, .conv_u2, .conv_i4 {
			a := vals[sp - 1].int_value()!
			vals[sp - 1] = integer(match f.op {
				.conv_i1 { int(i8(a)) }
				.conv_i2 { int(i16(a)) }
				.conv_u1 { int(u8(a)) }
				.conv_u2 { int(u16(a)) }
				else { a }
			})
		}
		.ldlen {
			a := vals[sp - 1]
			if a.kind != .array_ref || a.n < 0 || a.n >= vm.heap.len {
				return error('ldlen on non-array')
			}
			vals[sp - 1] = integer(vm.heap[a.n].values.len)
		}
		.newarr {
			vals[sp - 1] = vm.new_array(f.a, vals[sp - 1].int_value()!)!
		}
		.ldelema {
			index := vals[sp - 1].int_value()!
			h := vm.array_index(vals[sp - 2], index)!
			vals[sp - 2] = address_of(mut vm.heap[h].values, index)!
			return sp - 1
		}
		.ldsflda {
			vals[sp] = address_of(mut vm.globals, f.a)!
			return sp + 1
		}
		.stfld {
			v := vals[sp - 1]
			obj := vals[sp - 2]
			record := if obj.kind == .address { dereference(obj)! } else { obj }
			if record.kind != .record || record.n != f.c || f.b < 0
				|| f.b >= record.data.fields.len {
				return error('invalid struct field access')
			}
			if obj.kind != .address {
				return error('stfld needs a struct address')
			}
			mut fields := record.data.fields.clone()
			fields[f.b] = v
			write_address(obj, record_value(record.n, fields))!
			return sp - 2
		}
		.ldarga, .ldloca {
			vals[sp] = address_of(mut vals, if f.op == .ldarga { args } else { locals } + f.a)!
			return sp + 1
		}
		.ldobj {
			vals[sp - 1] = copy_value(dereference(vals[sp - 1])!)
		}
		.stobj {
			v := vals[sp - 1]
			addr := vals[sp - 2]
			_ = dereference(addr)!
			write_address(addr, v)!
			return sp - 2
		}
		.initobj {
			addr := vals[sp - 1]
			_ = dereference(addr)!
			write_address(addr, vm.zero(0x100000 | f.a, 0)!)!
			return sp - 1
		}
		.ldftn {
			vals[sp] = Value{
				kind: .function
				n:    f.a
			}
			return sp + 1
		}
		.newobj_delegate {
			fn_value := vals[sp - 1]
			target := vals[sp - 2]
			if target.kind != .null_value || fn_value.kind != .function {
				return error('only capture-free static delegates are supported')
			}
			if vm.delegates == 2147483647 {
				return error('delegate identity limit exceeded')
			}
			vm.delegates++
			vals[sp - 2] = Value{
				kind: .delegate_value
				n:    fn_value.n
				id:   vm.delegates
			}
			return sp - 1
		}
		.newobj_unsupported {
			return error('unsupported constructor ${vm.unsupported[f.a]}')
		}
		.box, .unbox {
			ty := vm.texts[f.a]
			if ty !in ['System.Int32', 'System.UInt32', 'System.Boolean', 'System.Char',
				'System.Byte', 'System.SByte', 'System.Int16', 'System.UInt16'] {
				return error('unsupported box/unbox type ${ty}')
			}
			v := vals[sp - 1]
			if f.op == .box {
				_ = v.int_value()!
				if vm.boxes.len >= max_array_elements {
					return error('boxed object limit exceeded')
				}
				vm.boxes << Value{
					...v
					data: &ValueData{
						text: ty
					}
				}
				vals[sp - 1] = Value{
					kind: .boxed
					n:    vm.boxes.len - 1
				}
			} else {
				if v.kind != .boxed || v.n < 0 || v.n >= vm.boxes.len
					|| vm.boxes[v.n].data.text != ty {
					return error('unbox.any type mismatch')
				}
				vals[sp - 1] = integer(vm.boxes[v.n].n)
			}
		}
		.call_native {
			base := sp - f.b
			result := vm.native_call(&vm.sites[f.a], vals[base..sp])!
			if f.c == 1 {
				vals[base] = result
				return base + 1
			}
			return base
		}
		else {
			return error('internal: ${f.op} has no general implementation')
		}
	}
	return sp
}

// Prepares the frame of `body` whose locals start at `locals`, and returns its limit.
@[direct_array_access]
fn (mut vm Vm) enter(body int, locals int) !int {
	b := &vm.bodies[body]
	limit := locals + b.locals.len + b.max_stack
	if limit > vm.vals.len {
		return error('value stack exhausted')
	}
	for i, v in b.local_init {
		vm.vals[locals + i] = v
	}
	return limit
}

fn binary(op int, a int, b int) !int {
	if op in [0x5b, 0x5c, 0x5d, 0x5e] && b == 0 {
		return error('division by zero')
	}
	if op == 0x5b && a == -2147483647 - 1 && b == -1 {
		return error('int32 division overflow')
	}
	return match op {
		0x58 {
			int(u32(a) + u32(b))
		}
		0x59 {
			int(u32(a) - u32(b))
		}
		0x5a {
			int(u32(a) * u32(b))
		}
		0x5b {
			a / b
		}
		0x5c {
			int(u32(a) / u32(b))
		}
		0x5d {
			if a == -2147483647 - 1 && b == -1 {
				0
			} else {
				a % b
			}
		}
		0x5e {
			int(u32(a) % u32(b))
		}
		0x5f {
			a & b
		}
		0x60 {
			a | b
		}
		0x61 {
			a ^ b
		}
		0x62 {
			int(u32(a) << u32(b & 31))
		}
		0x63 {
			a >> u32(b & 31)
		}
		0x64 {
			int(u32(a) >> u32(b & 31))
		}
		else {
			return error('unsupported arithmetic opcode')
		}
	}
}
