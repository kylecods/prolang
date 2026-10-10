module main

const max_steps = 10000000
const max_depth = 128
const max_array_elements = 262144
const max_output = 65536

struct Frame {
	args   []Value
	locals []Value
	stack  []Value
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
	type_fields        [][]int
	delegates          int
	frames             []Frame
}

fn new_vm(im &Image) !Vm {
	mut vm := Vm{
		image:  im
		frames: []Frame{len: max_depth}
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
		vm.bodies << Body{
			...body
			code: code
		}
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
	signature := vm.bodies[int(vm.image.entry & 0xffffff) - 1].signature
	mut values := []Value{}
	if signature.params.len == 1 && signature.params[0] == 0x8e {
		a := vm.new_array(0x0e, args.len)!
		for i, s in args {
			vm.heap[a.n].values[i] = text_value(utf16(s))
		}
		values << a
	} else if signature.params.len != 0 {
		return error('entry point must take no arguments or string[]')
	}
	_ = vm.execute(vm.image.entry, values, 0)!
}

fn (mut vm Vm) execute(token u32, arguments []Value, depth int) !Value {
	vm.calls++
	if depth >= max_depth {
		return error('call depth limit exceeded')
	}
	method := vm.image.method(token)!
	body := &vm.bodies[int(token & 0xffffff) - 1]
	if arguments.len != body.signature.params.len {
		return error('argument count mismatch: ${method.name}')
	}
	// Each call depth owns separate buffers. Calls borrow the caller's stack
	// slice, then copy into their own argument slots before running any IL.
	frame := vm.frames[depth]
	mut args := unsafe { frame.args }
	mut locals := unsafe { frame.locals }
	mut stack := unsafe { frame.stack }
	if stack.cap < body.max_stack + 1 {
		stack = []Value{cap: body.max_stack + 1}
	}
	defer {
		reset_frame(mut args)
		reset_frame(mut locals)
		reset_frame(mut stack)
		vm.frames[depth] = Frame{
			args:   args
			locals: locals
			stack:  stack
		}
	}
	args << arguments
	for t in body.locals {
		locals << vm.zero(t, 0)!
	}
	mut pc := 0
	for pc < body.code.len {
		vm.steps++
		if vm.steps > max_steps {
			return error('instruction budget exceeded')
		}
		ins := &body.code[pc]
		pc++
		op := ins.op
		n := ins.operand
		match op {
			0x00 {}
			0x02...0x05 {
				stack << slot(args, op - 2)!
			}
			0x06...0x09 {
				stack << slot(locals, op - 6)!
			}
			0x0a...0x0d {
				store(mut locals, op - 10, pop(mut stack)!)!
			}
			0x0e, 0xfe09 {
				stack << slot(args, n)!
			}
			0x10, 0xfe0b {
				store(mut args, n, pop(mut stack)!)!
			}
			0x11, 0xfe0c {
				stack << slot(locals, n)!
			}
			0x0f, 0xfe0a {
				stack << address_of(mut args, n)!
			}
			0x12, 0xfe0d {
				stack << address_of(mut locals, n)!
			}
			0x13, 0xfe0e {
				store(mut locals, n, pop(mut stack)!)!
			}
			0x14 {
				stack << Value{}
			}
			0x15...0x1e {
				stack << integer(op - 0x16)
			}
			0x1f, 0x20 {
				stack << integer(n)
			}
			0x25 {
				v := pop(mut stack)!
				stack << copy_value(v)
				stack << v
			}
			0x26 {
				_ = pop(mut stack)!
			}
			0x28, 0x6f {
				signature := ins.signature
				call_token := u32(n)
				count := signature.params.len + if signature.instance { 1 } else { 0 }
				if count > stack.len {
					return error('call stack underflow in ${method.name}')
				}
				call_args := unsafe { stack[stack.len - count..] }
				stack.trim(stack.len - count)
				value := if call_token >> 24 == 6 {
					vm.execute(call_token, call_args, depth + 1)!
				} else if ins.name == 'Invoke' && is_delegate(ins.owner) {
					if call_args[0].kind != .delegate_value {
						return error('Invoke on a null or invalid delegate')
					}
					vm.execute(u32(call_args[0].n), call_args[1..], depth + 1)!
				} else {
					vm.native_call(ins.key, ins.name, signature, call_args)!
				}
				if signature.returns != 1 {
					stack << value
				}
			}
			0x2a {
				if body.signature.returns == 1 {
					if stack.len != 0 {
						return error('non-empty stack at void return')
					}
					return Value{}
				}
				if stack.len != 1 {
					return error('invalid return stack')
				}
				return pop(mut stack)!
			}
			0x2b...0x44 {
				short_op := if op >= 0x38 { op - 13 } else { op }
				mut take := false
				if short_op == 0x2b {
					take = true
				} else if short_op == 0x2c || short_op == 0x2d {
					v := pop(mut stack)!
					truth := v.kind != .null_value && (v.kind != .integer || v.n != 0)
					take = if short_op == 0x2c { !truth } else { truth }
				} else {
					b := pop(mut stack)!
					a := pop(mut stack)!
					if short_op == 0x2e {
						take = equal_values(a, b)
					} else if short_op == 0x33 {
						take = !equal_values(a, b)
					} else {
						x := a.int_value()!
						y := b.int_value()!
						take = match short_op {
							0x2f { x >= y }
							0x30 { x > y }
							0x31 { x <= y }
							0x32 { x < y }
							0x34 { u32(x) >= u32(y) }
							0x35 { u32(x) > u32(y) }
							0x36 { u32(x) <= u32(y) }
							0x37 { u32(x) < u32(y) }
							else { false }
						}
					}
				}
				if take {
					pc = n
				}
			}
			0x58...0x64 {
				b := pop(mut stack)!.int_value()!
				a := pop(mut stack)!.int_value()!
				stack << integer(binary(op, a, b)!)
			}
			0x65, 0x66 {
				a := pop(mut stack)!.int_value()!
				stack << integer(if op == 0x65 { int(u32(0) - u32(a)) } else { ~a })
			}
			0x67, 0x68, 0x69, 0x6d, 0xd1, 0xd2, 0xd3, 0xe0 {
				a := pop(mut stack)!.int_value()!
				stack << integer(match op {
					0x67 { int(i8(a)) }
					0x68 { int(i16(a)) }
					0xd1 { int(u16(a)) }
					0xd2 { int(u8(a)) }
					else { a }
				})
			}
			0x72 {
				stack << ins.literal
			}
			0xfe06 {
				_ = vm.image.method(u32(n))!
				stack << Value{
					kind: .function
					n:    n
				}
			}
			0x73 {
				if !is_delegate(ins.owner) || ins.name != '.ctor'
					|| ins.signature.params != [28, 24] {
					return error('unsupported constructor ${ins.owner}.${ins.name}')
				}
				f := pop(mut stack)!
				target := pop(mut stack)!
				if target.kind != .null_value || f.kind != .function {
					return error('only capture-free static delegates are supported')
				}
				if vm.delegates == 2147483647 {
					return error('delegate identity limit exceeded')
				}
				vm.delegates++
				stack << Value{
					kind: .delegate_value
					n:    f.n
					id:   vm.delegates
				}
			}
			0xfe15 {
				addr := pop(mut stack)!
				_ = dereference(addr)!
				v := vm.zero(0x100000 | (n & 0xffffff), 0)!
				write_address(addr, v)!
			}
			0x71 {
				stack << copy_value(dereference(pop(mut stack)!)!)
			}
			0x81 {
				v := pop(mut stack)!
				addr := pop(mut stack)!
				_ = dereference(addr)!
				write_address(addr, v)!
			}
			0x7b...0x7d {
				mut v := Value{}
				if op == 0x7d {
					v = pop(mut stack)!
				}
				obj := pop(mut stack)!
				record := if obj.kind == .address { dereference(obj)! } else { obj }
				i := vm.field_offsets[ins.field]
				if record.kind != .record || i < 0 || i >= record.data.fields.len
					|| ins.field !in vm.type_fields[record.n] {
					return error('invalid struct field access')
				}
				if op == 0x7b {
					stack << copy_value(record.data.fields[i])
				} else if op == 0x7c {
					stack << field_address(obj, i)!
				} else {
					if obj.kind != .address {
						return error('stfld needs a struct address')
					}
					mut fields := record.data.fields.clone()
					fields[i] = v
					write_address(obj, record_value(record.n, fields))!
				}
			}
			0x7e, 0x80 {
				i := ins.field
				if op == 0x7e {
					stack << slot(vm.globals, i)!
				} else {
					store(mut vm.globals, i, pop(mut stack)!)!
				}
			}
			0x7f {
				stack << address_of(mut vm.globals, ins.field)!
			}
			0x8c, 0xa5 {
				ty := ins.text
				if ty !in ['System.Int32', 'System.UInt32', 'System.Boolean', 'System.Char',
					'System.Byte', 'System.SByte', 'System.Int16', 'System.UInt16'] {
					return error('unsupported box/unbox type ${ty}')
				}
				v := pop(mut stack)!
				if op == 0x8c {
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
					stack << Value{
						kind: .boxed
						n:    vm.boxes.len - 1
					}
				} else {
					if v.kind != .boxed || v.n < 0 || v.n >= vm.boxes.len
						|| vm.boxes[v.n].data.text != ty {
						return error('unbox.any type mismatch')
					}
					stack << integer(vm.boxes[v.n].n)
				}
			}
			0x8d {
				stack << vm.new_array(ins.field, pop(mut stack)!.int_value()!)!
			}
			0x8f {
				index := pop(mut stack)!.int_value()!
				a := pop(mut stack)!
				h := vm.array_index(a, index)!
				stack << address_of(mut vm.heap[h].values, index)!
			}
			0x8e {
				a := pop(mut stack)!
				if a.kind != .array_ref || a.n < 0 || a.n >= vm.heap.len {
					return error('ldlen on non-array')
				}
				stack << integer(vm.heap[a.n].values.len)
			}
			0x90...0x95, 0x9a, 0xa3 {
				index := pop(mut stack)!.int_value()!
				a := pop(mut stack)!
				h := vm.array_index(a, index)!
				mut v := copy_value(vm.heap[h].values[index])
				if op in [0x90, 0x91, 0x92, 0x93] {
					x := v.int_value()!
					v = integer(match op {
						0x90 { int(i8(x)) }
						0x91 { int(u8(x)) }
						0x92 { int(i16(x)) }
						else { int(u16(x)) }
					})
				}
				stack << v
			}
			0x9b...0x9e, 0xa2, 0xa4 {
				mut v := pop(mut stack)!
				index := pop(mut stack)!.int_value()!
				a := pop(mut stack)!
				h := vm.array_index(a, index)!
				if op == 0x9c {
					v = integer(int(u8(v.int_value()!)))
				}
				if op == 0x9d {
					v = integer(int(u16(v.int_value()!)))
				}
				vm.heap[h].values[index] = v
			}
			0xfe01...0xfe05 {
				b := pop(mut stack)!
				a := pop(mut stack)!
				mut yes := false
				if op == 0xfe01 {
					if a.kind == .text && b.kind == .text {
						return error('string reference identity comparison is unsupported')
					}
					yes = equal_values(a, b)
				} else {
					x := a.int_value()!
					y := b.int_value()!
					yes = match op {
						0xfe02 { x > y }
						0xfe03 { u32(x) > u32(y) }
						0xfe04 { x < y }
						else { u32(x) < u32(y) }
					}
				}
				stack << integer(if yes { 1 } else { 0 })
			}
			else {
				return error('${method.name}: opcode 0x${op:04x} at IL_${ins.offset:04x} has no handler')
			}
		}
		if stack.len > body.max_stack {
			return error('maxstack exceeded in ${method.name}')
		}
	}
	return error('method ${method.name} falls through without ret')
}

fn reset_frame(mut values []Value) {
	// Clearing capacity also removes dead values above the evaluation stack's
	// current length, so cached buffers do not keep temporary objects alive.
	if values.cap > 0 {
		unsafe { C.memset(values.data, 0, usize(values.cap) * usize(sizeof(Value))) }
	}
	values.clear()
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
