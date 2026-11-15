module main

enum Kind {
	null_value
	integer
	text
	array_ref
	boxed
	record
	address
	function
	delegate_value
}

struct Value {
	kind Kind
	n    int
	id   int
	data &ValueData = unsafe { nil }
}

// Integers and handles stay in a small slot. Only strings, structs, boxed type
// tags and managed addresses need this immutable, collector-owned payload.
struct ValueData {
	text   string // UTF-16LE bytes: matches CLI string indexing and System.Char.
	fields []Value
	ptr    &Value = unsafe { nil }
	path   []int
}

struct HeapArray {
	element int
mut:
	values []Value
}

fn integer(n int) Value {
	return Value{
		kind: .integer
		n:    n
	}
}

fn text_value(s string) Value {
	return Value{
		kind: .text
		data: &ValueData{
			text: s
		}
	}
}

fn record_value(ty int, fields []Value) Value {
	return Value{
		kind: .record
		n:    ty
		data: &ValueData{
			fields: fields
		}
	}
}

fn utf16(s string) string {
	mut bytes := []u8{}
	for c in s.runes() {
		if c <= 0xffff {
			bytes << u8(c)
			bytes << u8(c >> 8)
		} else {
			n := u32(c) - 0x10000
			hi := u16(0xd800 | (n >> 10))
			lo := u16(0xdc00 | (n & 0x3ff))
			bytes << [u8(hi), u8(hi >> 8), u8(lo), u8(lo >> 8)]
		}
	}
	return bytes.bytestr()
}

fn utf8(s string) string {
	mut runes := []rune{}
	mut i := 0
	for i + 1 < s.len {
		mut c := u32(s[i]) | (u32(s[i + 1]) << 8)
		i += 2
		if c >= 0xd800 && c <= 0xdbff && i + 1 < s.len {
			low := u32(s[i]) | (u32(s[i + 1]) << 8)
			if low >= 0xdc00 && low <= 0xdfff {
				c = 0x10000 + ((c - 0xd800) << 10) + low - 0xdc00
				i += 2
			} else {
				c = 0xfffd
			}
		} else if c >= 0xd800 && c <= 0xdfff {
			c = 0xfffd
		}
		runes << rune(c)
	}
	return runes.string()
}

fn (v Value) int_value() !int {
	if v.kind != .integer {
		return error('expected an int32 value')
	}
	return v.n
}

fn (v Value) string_value() !string {
	if v.kind != .text {
		return error('expected a non-null string')
	}
	return v.data.text
}

fn default_value(t int) Value {
	return if t in [0x0e, 0x12, 0x1c] || t >= 0x80 { Value{} } else { integer(0) }
}

fn format_value(v Value, t int) !string {
	if v.kind == .null_value {
		return ''
	}
	if v.kind == .text {
		return v.data.text
	}
	if v.kind != .integer {
		return error('array-to-string conversion is unsupported')
	}
	if t == 2 {
		return utf16(if v.n == 0 { 'False' } else { 'True' })
	}
	if t == 3 {
		return [u8(v.n), u8(v.n >> 8)].bytestr()
	}
	if t == 9 {
		return utf16(u32(v.n).str())
	}
	return utf16(v.n.str())
}

fn equal_values(a Value, b Value) bool {
	if a.kind != b.kind {
		return false
	}
	return match a.kind {
		.null_value { true }
		.integer, .array_ref, .boxed, .function { a.n == b.n }
		.delegate_value { a.id == b.id }
		.text { a.data.text == b.data.text }
		.address { a.data.ptr == b.data.ptr && a.data.path == b.data.path }
		.record { false }
	}
}

// Struct payloads are immutable snapshots. A field write copies only the path
// being changed, so ordinary CLI value loads can share the payload safely.
fn copy_value(v Value) Value {
	return v
}

fn address_of(mut values []Value, index int) !Value {
	if index < 0 || index >= values.len {
		return error('address index out of bounds')
	}
	return Value{
		kind: .address
		data: &ValueData{
			ptr: unsafe { &values[index] }
		}
	}
}

fn dereference(addr Value) !Value {
	if addr.kind != .address || addr.data == unsafe { nil } || addr.data.ptr == unsafe { nil } {
		return error('expected managed address')
	}
	mut v := unsafe { *addr.data.ptr }
	for i in addr.data.path {
		if v.kind != .record || i < 0 || i >= v.data.fields.len {
			return error('invalid field address')
		}
		v = v.data.fields[i]
	}
	return v
}

fn replace_path(root Value, path []int, level int, value Value) !Value {
	if level == path.len {
		return value
	}
	i := path[level]
	if root.kind != .record || i < 0 || i >= root.data.fields.len {
		return error('invalid field address')
	}
	mut fields := root.data.fields.clone()
	fields[i] = replace_path(fields[i], path, level + 1, value)!
	return record_value(root.n, fields)
}

fn write_address(addr Value, value Value) ! {
	_ = dereference(addr)!
	root := unsafe { *addr.data.ptr }
	updated := replace_path(root, addr.data.path, 0, value)!
	mut destination := unsafe { addr.data.ptr }
	unsafe {
		*destination = updated
	}
}

fn field_address(addr Value, field int) !Value {
	_ = dereference(addr)!
	if addr.data.path.len >= 32 {
		return error('field address nesting limit exceeded')
	}
	mut path := addr.data.path.clone()
	path << field
	return Value{
		...addr
		data: &ValueData{
			ptr:  addr.data.ptr
			path: path
		}
	}
}
