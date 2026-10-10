module main

import os

fn main() {
	if os.args.len < 2 {
		eprintln('usage: prolang-msil assembly.dll [program arguments]')
		exit(2)
	}
	C.prl_host_init()
	status := run_file(os.args[1], os.args[2..])
	exit(status)
}
