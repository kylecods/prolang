module main

#flag -I @VMODROOT
#include <host.h>

fn C.prl_host_init()
fn C.prl_host_write(s &char, length int)
fn C.prl_host_size(path &char) int
fn C.prl_host_read(path &char, data &u8, count int) int
fn C.prl_host_sleep(ms int)
fn C.prl_host_millis() int
fn C.prl_host_finish(status int)
fn C.prl_host_gc_init()
fn C.prl_host_gc_stats()
fn C.prl_host_perf(instructions int, calls int)
fn C.prl_host_psp_init()
fn C.prl_host_clear(c int)
fn C.prl_host_rect(x int, y int, w int, h int, c int)
fn C.prl_host_line(x int, y int, xx int, yy int, c int)
fn C.prl_host_text(x int, y int, s &char, len int, c int)
fn C.prl_host_swap()
fn C.prl_host_vsync()
fn C.prl_host_buttons() int

fn host_write(s string) {
	C.prl_host_write(s.str, s.len)
}

fn read_assembly(path string) ![]u8 {
	size := C.prl_host_size(path.str)
	if size < 1 || size > max_image {
		return error('cannot read assembly ${path}, or image exceeds 8 MiB')
	}
	mut data := []u8{len: size}
	if C.prl_host_read(path.str, data.data, size) != size {
		return error('assembly read failed: ${path}')
	}
	return data
}

fn run_file(path string, args []string) int {
	C.prl_host_gc_init()
	data := read_assembly(path) or {
		host_write('runtime: ${err}\n')
		return 1
	}
	im := load_image(data) or {
		host_write('loader: ${err}\n')
		return 1
	}
	mut vm := new_vm(im) or {
		host_write('runtime: ${err}\n')
		return 1
	}
	vm.run(args) or {
		vm.flush()
		host_write('runtime: ${err}\n')
		return 1
	}
	return 0
}
