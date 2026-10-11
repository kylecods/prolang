module main

struct Instruction {
	offset  int
	next    int
	op      int
	operand int
mut:
	signature Signature
	owner     string
	name      string
	key       string
	text      string
	literal   Value
	field     int
}

struct Body {
	code      []Instruction
	locals    []int
	signature Signature
	max_stack int
mut:
	// The execution form; see decode.v.
	fast []Fast
	// Each local's zero value. Struct payloads are immutable snapshots, so one per body is
	// shared by every call rather than rebuilt on entry.
	local_init []Value
}

fn (im &Image) body(m Method) !Body {
	if m.flags & 0x10 == 0 {
		return error('instance MethodDefs are unsupported: ${m.name}')
	}
	mut r := Reader{
		data: im.data
		pos:  im.rva_offset(m.rva, 1)!
	}
	first := r.byte()!
	mut length := 0
	mut max_stack := 8
	mut local_token := u32(0)
	if first & 3 == 2 {
		length = int(first >> 2)
	} else if first & 3 == 3 {
		second := r.byte()!
		flags := int(u16(first) | (u16(second) << 8))
		if flags & 8 != 0 {
			return error('exception sections are unsupported: ${m.name}')
		}
		header_size := (flags >> 12) * 4
		if header_size < 12 {
			return error('invalid fat method header')
		}
		max_stack = int(r.word()!)
		n := r.dword()!
		if n > max_image {
			return error('method body too large')
		}
		length = int(n)
		local_token = r.dword()!
		_ = r.take(header_size - 12)!
	} else {
		return error('invalid method header: ${m.name}')
	}
	if max_stack > 1024 {
		return error('method exceeds 1024 stack slots: ${m.name}')
	}
	_ = im.rva_offset(m.rva, r.pos - im.rva_offset(m.rva, 1)! + length)!
	bytes := r.take(length)!
	mut locals := []int{}
	if local_token != 0 {
		if local_token >> 24 != 17 {
			return error('invalid local signature token')
		}
		mut row := im.row(17, int(local_token & 0xffffff))!
		mut sig := Reader{
			data: im.blob(row.index(im.blob_width)!)!
		}
		if sig.byte()! != 7 {
			return error('invalid local variable signature')
		}
		count := sig.compressed()!
		if count > 1024 {
			return error('too many local variables')
		}
		for _ in 0 .. count {
			locals << read_type(mut sig)!
		}
	}
	mut il := Reader{
		data: bytes
	}
	mut code := []Instruction{}
	for il.pos < bytes.len {
		offset := il.pos
		mut op := int(il.byte()!)
		if op == 0xfe {
			op = 0xfe00 | int(il.byte()!)
		}
		mut operand := 0
		match op {
			0x0e...0x13 {
				operand = int(il.byte()!)
			}
			0x1f, 0x2b...0x37 {
				operand = int(i8(il.byte()!))
			}
			0x20, 0x28, 0x38...0x44, 0x6f, 0x71...0x73, 0x7b...0x81, 0x8c, 0x8d, 0x8f, 0xa3, 0xa4,
			0xa5, 0xfe06, 0xfe15 {
				operand = int(il.dword()!)
			}
			0xfe09...0xfe0e {
				operand = int(il.word()!)
			}
			0x00, 0x02...0x0d, 0x14...0x1e, 0x25, 0x26, 0x2a, 0x58...0x66, 0x67...0x69, 0x6d, 0x8e,
			0x90...0x95, 0x9a, 0x9b...0x9e, 0xa2, 0xd1, 0xd2, 0xd3, 0xe0, 0xfe01...0xfe05 {}
			else {
				return error('${m.name}: unsupported opcode 0x${op:04x} at IL_${offset:04x}')
			}
		}
		if op >= 0x2b && op <= 0x44 {
			operand += il.pos
		}
		code << Instruction{
			offset:  offset
			next:    il.pos
			op:      op
			operand: operand
		}
	}
	for ins in code {
		if ins.op >= 0x2b && ins.op <= 0x44 {
			_ = instruction_index(code, ins.operand)!
		}
	}
	return Body{
		code:      code
		locals:    locals
		signature: im.signature(m.signature)!
		max_stack: max_stack
	}
}

fn instruction_index(code []Instruction, offset int) !int {
	// Binary search also rejects branches into the middle of an instruction.
	mut lo := 0
	mut hi := code.len
	for lo < hi {
		mid := lo + (hi - lo) / 2
		if code[mid].offset < offset {
			lo = mid + 1
		} else {
			hi = mid
		}
	}
	if lo == code.len || code[lo].offset != offset {
		return error('invalid branch target IL_${offset:04x}')
	}
	return lo
}
