module main

// All file reads go through this cursor: malformed images return errors rather
// than indexing arbitrary memory. Offsets are file offsets, never host pointers.
struct Reader {
	data []u8
mut:
	pos int
}

fn (mut r Reader) take(n int) ![]u8 {
	if n < 0 || r.pos < 0 || r.pos > r.data.len || n > r.data.len - r.pos {
		return error('truncated assembly at ${r.pos}, need ${n} bytes')
	}
	result := r.data[r.pos..r.pos + n]
	r.pos += n
	return result
}

fn (mut r Reader) byte() !u8 {
	b := r.take(1)!
	return b[0]
}

fn (mut r Reader) word() !u16 {
	b := r.take(2)!
	return u16(b[0]) | (u16(b[1]) << 8)
}

fn (mut r Reader) dword() !u32 {
	b := r.take(4)!
	return u32(b[0]) | (u32(b[1]) << 8) | (u32(b[2]) << 16) | (u32(b[3]) << 24)
}

fn (mut r Reader) qword() !u64 {
	lo := r.dword()!
	hi := r.dword()!
	return u64(lo) | (u64(hi) << 32)
}

fn (mut r Reader) compressed() !int {
	a := r.byte()!
	if a < 0x80 {
		return int(a)
	}
	b := r.byte()!
	if a < 0xc0 {
		return int((u32(a & 0x3f) << 8) | u32(b))
	}
	if a >= 0xe0 {
		return error('invalid compressed metadata integer')
	}
	c := r.byte()!
	d := r.byte()!
	return int((u32(a & 0x1f) << 24) | (u32(b) << 16) | (u32(c) << 8) | u32(d))
}

fn (mut r Reader) index(width int) !int {
	if width == 2 {
		return int(r.word()!)
	}
	n := r.dword()!
	if n > 0x7fffffff {
		return error('metadata index too large')
	}
	return int(n)
}

fn (mut r Reader) zstring() !string {
	start := r.pos
	for r.byte()! != 0 {}
	return r.data[start..r.pos - 1].bytestr()
}

fn align4(n int) int {
	return (n + 3) & ~3
}
