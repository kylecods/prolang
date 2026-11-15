module main

const max_image = 8 * 1024 * 1024
const max_rows = 65535

struct Section {
	rva  u32
	raw  u32
	size u32
}

struct Method {
	name      string
	owner     string
	rva       u32
	flags     u16
	signature int
}

struct Signature {
	params   []int
	returns  int
	instance bool
}

struct Image {
	data []u8
mut:
	sections     []Section
	entry        u32
	strings      []u8
	blobs        []u8
	user_strings []u8
	rows         [64]int
	offsets      [64]int
	sizes        [64]int
	string_width int = 2
	blob_width   int = 2
	guid_width   int = 2
	tables       []u8
	methods      []Method
}

fn (im &Image) rva_offset(rva u32, size int) !int {
	for s in im.sections {
		if rva >= s.rva && u64(rva) + u64(size) <= u64(s.rva) + u64(s.size) {
			off := u64(s.raw) + u64(rva - s.rva)
			if off + u64(size) <= u64(im.data.len) {
				return int(off)
			}
		}
	}
	return error('RVA 0x${rva:08x} does not map to file data')
}

fn load_image(data []u8) !Image {
	if data.len > max_image {
		return error('assembly exceeds 8 MiB image limit')
	}
	mut im := Image{
		data: data
	}
	mut r := Reader{
		data: data
	}
	if r.word()! != 0x5a4d {
		return error('expected a PE assembly (MZ header)')
	}
	r.pos = 0x3c
	pe := r.dword()!
	if pe > u32(data.len) {
		return error('invalid PE header offset')
	}
	r.pos = int(pe)
	if r.dword()! != 0x4550 {
		return error('invalid PE signature')
	}
	_ = r.word()!
	sections := int(r.word()!)
	if sections == 0 || sections > 96 {
		return error('invalid section count')
	}
	_ = r.take(12)!
	opt_size := int(r.word()!)
	_ = r.word()!
	opt := r.take(opt_size)!
	mut o := Reader{
		data: opt
	}
	magic := o.word()!
	if magic != 0x10b && magic != 0x20b {
		return error('unsupported PE optional header')
	}
	directory := if magic == 0x10b { 96 } else { 112 }
	o.pos = directory - 4
	if o.dword()! < 15 {
		return error('PE has no CLI directory')
	}
	o.pos = directory + 14 * 8
	cli_rva := o.dword()!
	if o.dword()! < 72 {
		return error('invalid CLI header size')
	}
	for _ in 0 .. sections {
		_ = r.take(12)!
		va := r.dword()!
		sz := r.dword()!
		raw := r.dword()!
		_ = r.take(16)!
		im.sections << Section{
			rva:  va
			raw:  raw
			size: sz
		}
	}
	mut cli := Reader{
		data: data
		pos:  im.rva_offset(cli_rva, 72)!
	}
	_ = cli.take(8)!
	md_rva := cli.dword()!
	md_size := cli.dword()!
	flags := cli.dword()!
	im.entry = cli.dword()!
	if flags & 1 == 0 || flags & 0x10 != 0 {
		return error('only managed IL entry points are supported')
	}
	if im.entry >> 24 != 6 {
		return error('assembly has no MethodDef entry point')
	}
	if md_size > u32(data.len) {
		return error('invalid metadata size')
	}
	start := im.rva_offset(md_rva, int(md_size))!
	metadata := data[start..start + int(md_size)]
	mut md := Reader{
		data: metadata
	}
	if md.dword()! != 0x424a5342 {
		return error('invalid CLI metadata signature')
	}
	_ = md.take(8)!
	ver_size := md.dword()!
	if ver_size > u32(metadata.len) {
		return error('invalid metadata version')
	}
	_ = md.take(int(ver_size))!
	_ = md.word()!
	streams := int(md.word()!)
	if streams > 16 {
		return error('too many metadata streams')
	}
	for _ in 0 .. streams {
		off := md.dword()!
		size := md.dword()!
		name := md.zstring()!
		md.pos = align4(md.pos)
		if u64(off) + u64(size) > u64(metadata.len) {
			return error('metadata stream out of bounds')
		}
		stream := metadata[int(off)..int(off + size)]
		match name {
			'#Strings' { im.strings = stream }
			'#Blob' { im.blobs = stream }
			'#US' { im.user_strings = stream }
			'#~' { im.tables = stream }
			'#-' { return error('unoptimized metadata tables are unsupported') }
			else {}
		}
	}
	im.read_tables()!
	return im
}

fn (im &Image) table_width(table int) int {
	return if im.rows[table] < 65536 { 2 } else { 4 }
}

fn (im &Image) coded(bits int, tables []int) int {
	for t in tables {
		if im.rows[t] >= (1 << (16 - bits)) {
			return 4
		}
	}
	return 2
}

// ECMA-335 II.22: table row widths, including tables we do not execute.
// Skipping a table still requires its exact width to locate the following one.
fn (im &Image) row_size(t int) !int {
	s := im.string_width
	b := im.blob_width
	g := im.guid_width
	td := im.coded(2, [2, 1, 27])
	mr := im.coded(3, [2, 1, 26, 6, 27])
	has_custom := im.coded(5, [6, 4, 1, 2, 8, 9, 10, 0, 14, 23, 20, 17, 26, 27, 32, 35, 38, 39,
		40, 42, 44, 43])
	return match t {
		0 { 2 + s + 3 * g }
		1 { im.coded(2, [0, 26, 35, 1]) + 2 * s }
		2 { 4 + 2 * s + td + im.table_width(4) + im.table_width(6) }
		3 { im.table_width(4) }
		4 { 2 + s + b }
		5 { im.table_width(6) }
		6 { 8 + s + b + im.table_width(8) }
		7 { im.table_width(8) }
		8 { 4 + s }
		9 { im.table_width(2) + td }
		10 { mr + s + b }
		11 { 2 + im.coded(2, [4, 8, 23]) + b }
		12 { has_custom + im.coded(3, [6, 10]) + b }
		13 { im.coded(1, [4, 8]) + b }
		14 { 2 + im.coded(2, [2, 6, 32]) + b }
		15 { 6 + im.table_width(2) }
		16 { 4 + im.table_width(4) }
		17 { b }
		18 { im.table_width(2) + im.table_width(20) }
		19 { im.table_width(20) }
		20 { 2 + s + td }
		21 { im.table_width(2) + im.table_width(23) }
		22 { im.table_width(23) }
		23 { 2 + s + b }
		24 { 2 + im.table_width(6) + im.coded(1, [20, 23]) }
		25 { im.table_width(2) + 2 * im.coded(1, [6, 10]) }
		26 { s }
		27 { b }
		28 { 2 + im.coded(1, [4, 6]) + s + im.table_width(26) }
		29 { 4 + im.table_width(4) }
		30 { 8 }
		31 { 4 }
		32 { 16 + b + 2 * s }
		33 { 4 }
		34 { 12 }
		35 { 12 + 2 * b + 2 * s }
		36 { 4 + im.table_width(35) }
		37 { 12 + im.table_width(35) }
		38 { 4 + s + b }
		39 { 8 + 2 * s + im.coded(2, [38, 35, 39]) }
		40 { 8 + s + im.coded(2, [38, 35, 39]) }
		41 { 2 * im.table_width(2) }
		42 { 4 + im.coded(1, [2, 6]) + s }
		43 { im.coded(1, [6, 10]) + b }
		44 { im.table_width(42) + td }
		else { return error('unsupported metadata table ${t}') }
	}
}

fn (mut im Image) read_tables() ! {
	mut r := Reader{
		data: im.tables
	}
	_ = r.take(6)!
	heap_flags := r.byte()!
	_ = r.byte()!
	valid := r.qword()!
	_ = r.qword()!
	if heap_flags & 1 != 0 {
		im.string_width = 4
	}
	if heap_flags & 2 != 0 {
		im.guid_width = 4
	}
	if heap_flags & 4 != 0 {
		im.blob_width = 4
	}
	for t in 0 .. 64 {
		if valid & (u64(1) << t) != 0 {
			count := r.dword()!
			if count > max_rows {
				return error('metadata table ${t} exceeds row limit')
			}
			im.rows[t] = int(count)
		}
	}
	for t in 0 .. 64 {
		if im.rows[t] == 0 {
			continue
		}
		size := im.row_size(t)!
		im.offsets[t] = r.pos
		im.sizes[t] = size
		_ = r.take(size * im.rows[t])!
	}
	for row in 1 .. im.rows[6] + 1 {
		mut m := im.row(6, row)!
		rva := m.dword()!
		impl := m.word()!
		flags := m.word()!
		name := im.text(m.index(im.string_width)!)!
		sig := m.index(im.blob_width)!
		if impl & 3 != 0 {
			return error('native or runtime methods are unsupported: ${name}')
		}
		im.methods << Method{
			name:      name
			rva:       rva
			flags:     flags
			signature: sig
		}
	}
	for row in 1 .. im.rows[2] + 1 {
		mut ty := im.row(2, row)!
		_ = ty.dword()!
		name := im.text(ty.index(im.string_width)!)!
		ns := im.text(ty.index(im.string_width)!)!
		_ = ty.index(im.coded(2, [2, 1, 27]))!
		_ = ty.index(im.table_width(4))!
		first := ty.index(im.table_width(6))!
		mut last := im.rows[6] + 1
		if row < im.rows[2] {
			mut next := im.row(2, row + 1)!
			_ = next.take(4 + 2 * im.string_width + im.coded(2, [2, 1, 27]) + im.table_width(4))!
			last = next.index(im.table_width(6))!
		}
		if first < 1 || last < first || last > im.rows[6] + 1 {
			return error('invalid TypeDef method list')
		}
		for i in first .. last {
			m := im.methods[i - 1]
			im.methods[i - 1] = Method{
				...m
				owner: if ns == '' { name } else { ns + '.' + name }
			}
		}
	}
	_ = im.method(im.entry)!
}

fn (im &Image) row(t int, index int) !Reader {
	if t < 0 || t >= 64 || index < 1 || index > im.rows[t] {
		return error('invalid table ${t} row ${index}')
	}
	off := im.offsets[t] + (index - 1) * im.sizes[t]
	return Reader{
		data: im.tables[off..off + im.sizes[t]]
	}
}

fn (im &Image) text(index int) !string {
	mut r := Reader{
		data: im.strings
		pos:  index
	}
	return r.zstring()!
}

fn (im &Image) blob(index int) ![]u8 {
	mut r := Reader{
		data: im.blobs
		pos:  index
	}
	size := r.compressed()!
	return r.take(size)!
}

fn (im &Image) method(token u32) !Method {
	row := int(token & 0xffffff)
	if token >> 24 != 6 || row < 1 || row > im.methods.len {
		return error('invalid MethodDef token 0x${token:08x}')
	}
	return im.methods[row - 1]
}

// Primitive types keep their CLI element code. Primitive arrays use bit 0x80;
// struct types encode 0x100000 | TypeDef row, and struct arrays add 0x40000000.
// Generic parameter indices (0x1000 | index) are substituted for delegate calls.
fn read_type(mut r Reader) !int {
	return read_type_depth(mut r, 0)!
}

fn read_type_depth(mut r Reader, depth int) !int {
	if depth > 32 {
		return error('signature nesting limit exceeded')
	}
	t := r.byte()!
	match t {
		0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0e, 0x1c {
			return int(t)
		}
		0x11 {
			coded := r.compressed()!
			if coded & 3 != 0 {
				return error('external value types are unsupported')
			}
			return 0x100000 | (coded >> 2)
		}
		0x12 {
			_ = r.compressed()!
			return 0x12
		}
		0x15 {
			if r.byte()! != 0x12 {
				return error('generic value types are unsupported')
			}
			_ = r.compressed()!
			count := r.compressed()!
			if count > 16 {
				return error('generic type arity exceeds limit')
			}
			for _ in 0 .. count {
				_ = read_type_depth(mut r, depth + 1)!
			}
			return 0x12
		}
		0x13 {
			return 0x1000 | r.compressed()!
		}
		0x18 {
			return 0x18
		}
		0x1d {
			element := read_type_depth(mut r, depth + 1)!
			if element == 1 || element & 0x40000080 != 0 {
				return error('unsupported array signature')
			}
			return if element < 0x80 { 0x80 | element } else { 0x40000000 | element }
		}
		else {
			return error('unsupported signature type 0x${t:02x}')
		}
	}
}

fn (im &Image) signature(index int) !Signature {
	mut r := Reader{
		data: im.blob(index)!
	}
	flags := r.byte()!
	if flags & 0x1f != 0 {
		return error('generic/vararg method signatures are unsupported')
	}
	count := r.compressed()!
	if count > 64 {
		return error('too many method parameters')
	}
	ret := read_type(mut r)!
	mut params := []int{}
	for _ in 0 .. count {
		params << read_type(mut r)!
	}
	return Signature{
		params:   params
		returns:  ret
		instance: flags & 0x20 != 0
	}
}

fn (im &Image) type_name(token u32) !string {
	return im.type_name_depth(token, 0)!
}

fn (im &Image) type_name_depth(token u32, depth int) !string {
	if depth > 32 {
		return error('TypeSpec nesting limit exceeded')
	}
	table := int(token >> 24)
	mut r := im.row(table, int(token & 0xffffff))!
	if table == 27 {
		mut sig := Reader{
			data: im.blob(r.index(im.blob_width)!)!
		}
		if sig.byte()! != 0x15 || sig.byte()! != 0x12 {
			return error('unsupported TypeSpec')
		}
		return im.type_name_depth(type_token(sig.compressed()!), depth + 1)!
	}
	if table == 1 {
		_ = r.index(im.coded(2, [0, 26, 35, 1]))!
	} else if table == 2 {
		_ = r.dword()!
	} else {
		return error('unsupported type token 0x${token:08x}')
	}
	name := im.text(r.index(im.string_width)!)!
	ns := im.text(r.index(im.string_width)!)!
	return if ns == '' { name } else { ns + '.' + name }
}

fn (im &Image) member(token u32) !(string, string, Signature) {
	if token >> 24 != 10 {
		return error('unsupported call token 0x${token:08x}')
	}
	mut r := im.row(10, int(token & 0xffffff))!
	parent := r.index(im.coded(3, [2, 1, 26, 6, 27]))!
	table := match parent & 7 {
		0 { 2 }
		1 { 1 }
		4 { 27 }
		else { return error('unsupported MemberRef parent') }
	}
	owner := im.type_name((u32(table) << 24) | u32(parent >> 3))!
	name := im.text(r.index(im.string_width)!)!
	mut signature := im.signature(r.index(im.blob_width)!)!
	if table == 27 {
		mut row := im.row(27, parent >> 3)!
		mut ts := Reader{
			data: im.blob(row.index(im.blob_width)!)!
		}
		_ = ts.byte()!
		_ = ts.byte()!
		_ = ts.compressed()!
		count := ts.compressed()!
		if count > 16 {
			return error('generic type arity exceeds limit')
		}
		mut types := []int{}
		for _ in 0 .. count {
			types << read_type(mut ts)!
		}
		mut params := signature.params.clone()
		for i, p in params {
			if p & 0xfffff000 == 0x1000 {
				if (p & 0xfff) >= types.len {
					return error('invalid generic parameter')
				}
				params[i] = types[p & 0xfff]
			}
		}
		mut ret := signature.returns
		if ret & 0xfffff000 == 0x1000 {
			if (ret & 0xfff) >= types.len {
				return error('invalid generic return parameter')
			}
			ret = types[ret & 0xfff]
		}
		signature = Signature{
			params:   params
			returns:  ret
			instance: signature.instance
		}
	}
	return owner, name, signature
}

fn type_token(coded int) u32 {
	return (u32(match coded & 3 {
		0 { 2 }
		1 { 1 }
		else { 27 }
	}) << 24) | u32(coded >> 2)
}

fn (im &Image) literal(token u32) !string {
	if token >> 24 != 0x70 {
		return error('invalid user string token')
	}
	mut r := Reader{
		data: im.user_strings
		pos:  int(token & 0xffffff)
	}
	size := r.compressed()!
	if size < 1 || size % 2 != 1 {
		return error('invalid user string size')
	}
	bytes := r.take(size)!
	// Keep UTF-16 in the VM; V strings may contain embedded zero bytes.
	return bytes[..size - 1].bytestr()
}

fn (im &Image) field_index(token u32) !int {
	if token >> 24 == 4 {
		row := int(token & 0xffffff)
		_ = im.row(4, row)!
		return row - 1
	}
	if token >> 24 != 10 {
		return error('invalid static field token')
	}
	mut r := im.row(10, int(token & 0xffffff))!
	parent := r.index(im.coded(3, [2, 1, 26, 6, 27]))!
	// Cecil can emit a MemberRef even for a field in this module.
	if parent & 7 != 0 {
		return error('external static fields are unsupported (parent ${parent})')
	}
	owner := parent >> 3
	name := im.text(r.index(im.string_width)!)!
	sig := im.blob(r.index(im.blob_width)!)!
	mut ty := im.row(2, owner)!
	_ = ty.take(4 + 2 * im.string_width + im.coded(2, [2, 1, 27]))!
	first := ty.index(im.table_width(4))!
	mut last := im.rows[4] + 1
	if owner < im.rows[2] {
		mut next := im.row(2, owner + 1)!
		_ = next.take(4 + 2 * im.string_width + im.coded(2, [2, 1, 27]))!
		last = next.index(im.table_width(4))!
	}
	for i in first .. last {
		mut field := im.row(4, i)!
		_ = field.word()!
		nm := im.text(field.index(im.string_width)!)!
		blob := im.blob(field.index(im.blob_width)!)!
		if name == nm && sig == blob {
			return i - 1
		}
	}
	return error('unresolved static field ${name}')
}
