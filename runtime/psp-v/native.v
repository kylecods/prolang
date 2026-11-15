module main

fn (mut vm Vm) append_output(s string) ! {
	if s.len > max_output - vm.output.len {
		return error('output buffer limit exceeded')
	}
	vm.output += s
}

fn (mut vm Vm) flush() {
	host_write(utf8(vm.output))
	vm.output = ''
}

fn native_key(owner string, name string, sig Signature) string {
	// Match the whole supported signature, not just a member's spelling.
	mut key := owner + '.' + name + ':'
	if sig.instance {
		key += 'this,'
	}
	for p in sig.params {
		key += '${p},'
	}
	key += '->${sig.returns}'
	return key
}

fn is_delegate(owner string) bool {
	return owner == 'System.Action' || owner.starts_with('System.Action`')
		|| owner.starts_with('System.Func`')
}

// Runs a supported runtime or BCL call. The site was matched to its implementation once, by
// `native_id` at decode time, rather than by comparing key strings on every call.
fn (mut vm Vm) native_call(site &NativeSite, args []Value) !Value {
	match site.id {
		.psp_init {
			C.prl_host_psp_init()
		}
		.psp_clear {
			C.prl_host_clear(args[0].int_value()!)
		}
		.psp_fill_rect {
			C.prl_host_rect(args[0].int_value()!, args[1].int_value()!, args[2].int_value()!,
				args[3].int_value()!, args[4].int_value()!)
		}
		.psp_draw_line {
			C.prl_host_line(args[0].int_value()!, args[1].int_value()!, args[2].int_value()!,
				args[3].int_value()!, args[4].int_value()!)
		}
		.psp_draw_text {
			s := utf8(args[2].string_value()!)
			C.prl_host_text(args[0].int_value()!, args[1].int_value()!, s.str, s.len,
				args[3].int_value()!)
		}
		.psp_swap {
			C.prl_host_perf(vm.steps, vm.calls)
			C.prl_host_swap()
			C.prl_host_gc_stats()
			vm.steps = 0
			vm.calls = 0
		}
		.psp_vsync {
			C.prl_host_vsync()
		}
		.psp_buttons_held {
			return integer(C.prl_host_buttons())
		}
		.psp_button_pressed {
			return integer(if C.prl_host_buttons() & args[0].int_value()! != 0 { 1 } else { 0 })
		}
		.out_init {
			vm.output = ''
			return Value{}
		}
		.out_flush {
			vm.flush()
			return Value{}
		}
		.out_write {
			vm.append_output(vm.format(args[0], site.sig.params[0])! + utf16('\n'))!
			return Value{}
		}
		.str_from {
			return text_value(vm.format(args[0], site.sig.params[0])!)
		}
		.str_concat {
			return text_value(vm.format(args[0], 28)! + vm.format(args[1], 28)!)
		}
		.str_equality, .str_inequality {
			yes := equal_values(args[0], args[1])
			return integer(if yes == (site.id == .str_equality) { 1 } else { 0 })
		}
		.str_length {
			return integer(args[0].string_value()!.len / 2)
		}
		.str_char {
			s := args[0].string_value()!
			i := args[1].int_value()!
			if i < 0 || i >= s.len / 2 {
				return error('string index out of bounds')
			}
			return integer(int(u16(s[i * 2]) | (u16(s[i * 2 + 1]) << 8)))
		}
		.str_substring {
			s := args[0].string_value()!
			start := args[1].int_value()!
			end := args[2].int_value()!
			if start < 0 || end < start || end > s.len / 2 {
				return error('substring range out of bounds')
			}
			return text_value(s[start * 2..end * 2])
		}
		.str_index_of {
			s := args[0].string_value()!
			needle := args[1].string_value()!
			if needle.len > s.len {
				return integer(-1)
			}
			for i := 0; i <= s.len - needle.len; i += 2 {
				if s[i..i + needle.len] == needle {
					return integer(i / 2)
				}
			}
			return integer(-1)
		}
		.math_min, .math_max {
			a := args[0].int_value()!
			b := args[1].int_value()!
			return integer(if (a < b) == (site.id == .math_min) { a } else { b })
		}
		.assert {
			if args[0].int_value()! == 0 {
				vm.flush()
				return error('assertion failed: ${utf8(args[1].string_value()!)}')
			}
			return Value{}
		}
		.console_write {
			vm.flush()
			host_write(utf8(args[0].string_value()!))
			return Value{}
		}
		.sleep {
			ms := args[0].int_value()!
			if ms < 0 || ms > 60000 {
				return error('sleep must be between 0 and 60000 ms')
			}
			C.prl_host_sleep(ms)
			return Value{}
		}
		.millis {
			return integer(C.prl_host_millis())
		}
		.unknown {
			return error('unsupported native method ${site.key}')
		}
	}
	return Value{}
}

fn (vm &Vm) format(value Value, declared int) !string {
	mut v := value
	if v.kind == .boxed {
		if v.n < 0 || v.n >= vm.boxes.len {
			return error('invalid boxed reference')
		}
		v = vm.boxes[v.n]
	}
	return format_value(v, boxed_type(v, declared))!
}

fn boxed_type(v Value, declared int) int {
	if declared != 28 || v.kind != .integer {
		return declared
	}
	if v.data == unsafe { nil } {
		return 8
	}
	return match v.data.text {
		'System.Boolean' { u8(2) }
		'System.Char' { u8(3) }
		'System.UInt32' { u8(9) }
		else { u8(8) }
	}
}
