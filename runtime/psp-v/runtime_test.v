module main

fn test_reader_truncation_and_compressed_integers() {
	mut r := Reader{
		data: [u8(0x7f), 0x80, 0x80, 0xc0, 0, 0x40, 0]
	}
	assert r.compressed()! == 127
	assert r.compressed()! == 128
	assert r.compressed()! == 16384
	r.dword() or {
		assert err.msg().contains('truncated')
		return
	}
	assert false, 'truncated integer accepted'
}

fn test_reject_invalid_compressed_integer() {
	mut r := Reader{
		data: [u8(0xe0), 0]
	}
	r.compressed() or {
		assert err.msg().contains('invalid')
		return
	}
	assert false
}

fn test_loader_handles_every_truncated_prefix_without_panicking() {
	data := read_assembly(@VMODROOT + '/../../artifacts/msil-smoke/program.dll')!
	// A load is allowed once all CLI tables and raw sections it uses are present;
	// execution must still reject truncated method bodies.
	for n in 0 .. data.len {
		im := load_image(data[..n]) or { continue }
		_ = new_vm(im) or { continue }
		// PE padding after the used sections may be absent without invalidating
		// the CLI payload. Prefixes missing even the PE headers cannot load.
		assert n >= 512
	}
}

fn test_branch_target_must_be_an_instruction_boundary() {
	code := [Instruction{
		offset: 0
		next:   5
	}, Instruction{
		offset: 5
		next:   6
	}]
	assert instruction_index(code, 5)! == 1
	instruction_index(code, 1) or {
		assert err.msg().contains('invalid branch')
		return
	}
	assert false
}

fn test_int32_wraparound_and_masked_shifts() {
	assert binary(0x58, 2147483647, 1)! == -2147483647 - 1
	assert binary(0x59, -2147483647 - 1, 1)! == 2147483647
	assert binary(0x5a, 65536, 65536)! == 0
	assert binary(0x62, 1, 33)! == 2
	assert binary(0x64, -1, 1)! == 2147483647
	assert binary(0x63, -8, 1)! == -4
	assert binary(0x5c, -1, 2)! == 2147483647
}

fn test_division_failures_are_errors() {
	binary(0x5b, 10, 0) or { assert err.msg() == 'division by zero' }
	binary(0x5b, -2147483647 - 1, -1) or {
		assert err.msg().contains('overflow')
		return
	}
	assert false
}

fn test_utf16_round_trip_and_code_unit_length() {
	s := utf16('A\0é😀Z')
	assert s.len / 2 == 6
	assert utf8(s) == 'A\0é😀Z'
	assert utf8([u8(0), 0xd8].bytestr()) == '�'
}

fn test_array_aliasing_bounds_and_budget() {
	mut vm := Vm{}
	a := vm.new_array(8, 2)!
	b := a
	vm.heap[a.n].values[0] = integer(42)
	h := vm.array_index(b, 0)!
	assert vm.heap[h].values[0].n == 42
	vm.array_index(a, 2) or { assert err.msg().contains('bounds') }
	vm.new_array(8, max_array_elements) or {
		assert err.msg().contains('heap limit')
		return
	}
	assert false
}

fn test_boxed_reference_identity_and_formatting() {
	a := Value{
		kind: .boxed
		n:    0
	}
	b := Value{
		kind: .boxed
		n:    1
	}
	assert equal_values(a, a)
	assert !equal_values(a, b)
	vm := Vm{
		boxes: [
			Value{
				kind: .integer
				n:    1
				data: &ValueData{
					text: 'System.Boolean'
				}
			},
		]
	}
	assert utf8(vm.format(a, 28)!) == 'True'
}

fn test_struct_snapshots_and_nested_managed_addresses() {
	inner := record_value(1, [integer(4)])
	original := record_value(2, [inner, Value{
		kind: .array_ref
		n:    3
	}])
	mut locals := [original]
	root := address_of(mut locals, 0)!
	child := field_address(root, 0)!
	leaf := field_address(child, 0)!
	write_address(leaf, integer(9))!
	assert dereference(leaf)!.n == 9
	assert original.data.fields[0].data.fields[0].n == 4
	assert locals[0].data.fields[1].n == 3
	// A live field address follows subsequent assignments to its root slot.
	write_address(root, original)!
	assert dereference(leaf)!.n == 4
	assert !equal_values(root, child)
}

fn test_reject_excessive_signature_nesting() {
	mut data := []u8{len: 40, init: u8(0x1d)}
	data << u8(8)
	mut r := Reader{
		data: data
	}
	read_type(mut r) or {
		assert err.msg().contains('nesting limit')
		return
	}
	assert false
}

fn test_value_stack_is_cleared_and_reused_across_runs() {
	im := load_image(read_assembly(@VMODROOT + '/../../artifacts/msil-structs/program.dll')!)!
	mut vm := new_vm(im)!
	vm.run([]string{})!
	// Returning clears each frame, so nothing a finished call held stays reachable.
	for v in vm.vals {
		assert v.kind == .null_value && v.data == unsafe { nil }
	}
	buffer := vm.vals.data
	// Running the program again reuses the same slots. Its assertions exercise struct
	// results, nested addresses and callbacks after sibling calls reuse slots.
	vm.run([]string{})!
	assert vm.vals.data == buffer
}

fn test_constant_methods_are_folded_into_their_callers() {
	im := load_image(read_assembly(@VMODROOT + '/../../artifacts/msil-arithmetic/program.dll')!)!
	vm := new_vm(im)!
	for b in vm.bodies {
		for f in b.fast {
			if f.op == .call {
				assert constant_result(vm.bodies[f.a].fast, vm.bodies[f.a], map[int]int{}) == none
			}
		}
	}
}

fn test_stack_depth_is_verified_before_execution() {
	body := Body{
		signature: Signature{
			returns: 8
		}
		max_stack: 2
	}
	verify_stack([Fast{ op: .add }, Fast{ op: .ret }], body, 'm', []) or {
		assert err.msg().contains('underflow')
		verify_stack([Fast{ op: .ldc }, Fast{ op: .brtrue, a: 3 }, Fast{ op: .ldc }, Fast{ op: .ret }],
			body, 'm', []) or {
			assert err.msg().contains('inconsistent')
			return
		}
		assert false, 'unbalanced branch accepted'
		return
	}
	assert false, 'underflow accepted'
}

fn test_fusion_merges_sequences_but_never_across_a_branch_target() {
	fused := fuse([Fast{ op: .ldc, a: 7 }, Fast{ op: .add }, Fast{ op: .ret }])
	assert fused.len == 2 && fused[0].op == .add_imm && fused[0].a == 7 && fused[1].op == .ret
	// A branch lands on the add, so the ldc must stay separate.
	kept := fuse([Fast{ op: .ldc, a: 7 }, Fast{ op: .add }, Fast{ op: .br, a: 1 }])
	assert kept.len == 3 && kept[0].op == .ldc && kept[1].op == .add && kept[2].a == 1
	// `x >= 0` as compiled: ldc 0; clt; ldc 0; ceq.
	ge := fuse([Fast{ op: .ldc }, Fast{ op: .clt }, Fast{ op: .ldc }, Fast{ op: .ceq },
		Fast{ op: .ret }])
	assert ge.len == 2 && ge[0].op == .cge_imm && ge[0].a == 0
	// A branch to a removed `ldnull; pop` lands on what follows it.
	skip := fuse([Fast{ op: .br, a: 1 }, Fast{ op: .ldnull }, Fast{ op: .pop }, Fast{ op: .ret }])
	assert skip.len == 2 && skip[0].a == 1 && skip[1].op == .ret
}
