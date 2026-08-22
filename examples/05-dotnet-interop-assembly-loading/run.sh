#!/bin/sh
set -e
./build.sh
dotnet bin/interop.dll
