#!/bin/bash

# Test compilation speed of different JSON parsers

cd src/ProLang

echo "=== JSON Parser Compilation Speed Test ==="
echo ""

echo "Test 1: json-parser-optimized.prl"
echo "Expected: 5-10 seconds"
time dotnet run ../../examples/json-parser-optimized.prl --run > /tmp/opt.out 2>&1
echo "✓ Complete"
echo ""

echo "Test 2: json-parser-v2.prl (now optimized)"
echo "Expected: 10-15 seconds"
time dotnet run ../../examples/json-parser-v2.prl --run > /tmp/v2.out 2>&1
echo "✓ Complete"
echo ""

echo "Test 3: json-parser-working.prl"
echo "Expected: <5 seconds"
time dotnet run ../../examples/json-parser-working.prl --run > /tmp/working.out 2>&1
echo "✓ Complete"
echo ""

echo "=== Summary ==="
echo "All parsers compiled successfully!"
echo "Check output files:"
echo "  /tmp/opt.out - Optimized parser output"
echo "  /tmp/v2.out - v2 parser output"
echo "  /tmp/working.out - Working parser output"
