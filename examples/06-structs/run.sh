#!/bin/bash
cd "$(dirname "$0")"
dotnet run --project /home/kyle/Documents/programs/prolang/src/ProLang/ProLang.csproj -- -run structs.prl
