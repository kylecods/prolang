#!/bin/sh
# Build the C# library the example calls into, then compile the ProLang program.
set -e

dotnet build test_lib/CSharpFibonacci.csproj -c Release --nologo -v q
dotnet run --project ../../src/ProLang/ProLang.csproj -- 05_dotnet_interop.prl -o bin/interop.dll
