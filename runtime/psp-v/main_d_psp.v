module main

fn main() {
	C.prl_host_init()
	status := run_file('program.dll', []string{})
	C.prl_host_finish(status)
}
