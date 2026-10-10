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

fn (mut vm Vm) native_call(key string, name string, sig Signature, args []Value) !Value {
	match key {
		'ProLang.Runtime.PspOps.Init:->1' {
			C.prl_host_psp_init()
		}
		'ProLang.Runtime.PspOps.Clear:8,->1' {
			C.prl_host_clear(args[0].int_value()!)
		}
		'ProLang.Runtime.PspOps.FillRect:8,8,8,8,8,->1' {
			C.prl_host_rect(args[0].int_value()!, args[1].int_value()!, args[2].int_value()!,
				args[3].int_value()!, args[4].int_value()!)
		}
		'ProLang.Runtime.PspOps.DrawLine:8,8,8,8,8,->1' {
			C.prl_host_line(args[0].int_value()!, args[1].int_value()!, args[2].int_value()!,
				args[3].int_value()!, args[4].int_value()!)
		}
		'ProLang.Runtime.PspOps.DrawText:8,8,14,8,->1' {
			s := utf8(args[2].string_value()!)
			C.prl_host_text(args[0].int_value()!, args[1].int_value()!, s.str, s.len,
				args[3].int_value()!)
		}
		'ProLang.Runtime.PspOps.SwapBuffers:->1' {
			C.prl_host_perf(vm.steps, vm.calls)
			C.prl_host_swap()
			C.prl_host_gc_stats()
			vm.steps = 0
			vm.calls = 0
		}
		'ProLang.Runtime.PspOps.Vsync:->1' {
			C.prl_host_vsync()
		}
		'ProLang.Runtime.PspOps.ButtonsHeld:->8' {
			return integer(C.prl_host_buttons())
		}
		'ProLang.Runtime.PspOps.ButtonPressed:8,->2' {
			return integer(if C.prl_host_buttons() & args[0].int_value()! != 0 { 1 } else { 0 })
		}
		'ProLang.Runtime.Output.Initialize:->1' {
			vm.output = ''
			return Value{}
		}
		'ProLang.Runtime.Output.Flush:->1' {
			vm.flush()
			return Value{}
		}
		'ProLang.Runtime.Output.Write:14,->1', 'ProLang.Runtime.Output.Write:28,->1',
		'ProLang.Runtime.Output.Write:8,->1', 'ProLang.Runtime.Output.Write:9,->1',
		'ProLang.Runtime.Output.Write:2,->1', 'ProLang.Runtime.Output.Write:3,->1' {
			vm.append_output(vm.format(args[0], sig.params[0])! + utf16('\n'))!
			return Value{}
		}
		'ProLang.Runtime.StringOps.From:8,->14', 'ProLang.Runtime.StringOps.From:9,->14',
		'ProLang.Runtime.StringOps.From:2,->14', 'ProLang.Runtime.StringOps.From:3,->14',
		'ProLang.Runtime.StringOps.From:28,->14' {
			return text_value(vm.format(args[0], sig.params[0])!)
		}
		'System.String.Concat:14,14,->14', 'System.String.Concat:28,28,->14' {
			return text_value(vm.format(args[0], 28)! + vm.format(args[1], 28)!)
		}
		'System.String.op_Equality:14,14,->2', 'System.String.op_Inequality:14,14,->2' {
			yes := equal_values(args[0], args[1])
			return integer(if yes == (name == 'op_Equality') { 1 } else { 0 })
		}
		'System.String.get_Length:this,->8' {
			return integer(args[0].string_value()!.len / 2)
		}
		'ProLang.Runtime.StringOps.CharAt:14,8,->3', 'ProLang.Runtime.StringOps.CharCode:14,8,->8' {
			s := args[0].string_value()!
			i := args[1].int_value()!
			if i < 0 || i >= s.len / 2 {
				return error('string index out of bounds')
			}
			return integer(int(u16(s[i * 2]) | (u16(s[i * 2 + 1]) << 8)))
		}
		'ProLang.Runtime.StringOps.Substring:14,8,8,->14' {
			s := args[0].string_value()!
			start := args[1].int_value()!
			end := args[2].int_value()!
			if start < 0 || end < start || end > s.len / 2 {
				return error('substring range out of bounds')
			}
			return text_value(s[start * 2..end * 2])
		}
		'System.String.IndexOf:this,14,->8' {
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
		'System.Math.Min:8,8,->8', 'System.Math.Max:8,8,->8' {
			a := args[0].int_value()!
			b := args[1].int_value()!
			return integer(if (a < b) == (name == 'Min') { a } else { b })
		}
		'ProLang.Runtime.TestOps.Assert:2,14,->1' {
			if args[0].int_value()! == 0 {
				vm.flush()
				return error('assertion failed: ${utf8(args[1].string_value()!)}')
			}
			return Value{}
		}
		'ProLang.Runtime.ConsoleOps.Write:14,->1' {
			vm.flush()
			host_write(utf8(args[0].string_value()!))
			return Value{}
		}
		'System.Threading.Thread.Sleep:8,->1' {
			ms := args[0].int_value()!
			if ms < 0 || ms > 60000 {
				return error('sleep must be between 0 and 60000 ms')
			}
			C.prl_host_sleep(ms)
			return Value{}
		}
		'ProLang.Runtime.TimeOps.Millis:->8' {
			return integer(C.prl_host_millis())
		}
		else {
			return error('unsupported native method ${key}')
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
