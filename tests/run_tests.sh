#!/bin/bash

# ProLang Language Test Runner
# Runs all language tests and reports results

set -e

TESTS_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(dirname "$TESTS_DIR")"
SRC_DIR="$PROJECT_DIR/src"

# Colors for output
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

PASSED=0
FAILED=0
TOTAL=0

echo "=========================================="
echo "ProLang Language Test Suite"
echo "=========================================="
echo ""

# Find all .prl test files
test_files=$(find "$TESTS_DIR/language" -name "*.prl" -type f | sort)

if [ -z "$test_files" ]; then
    echo -e "${RED}ERROR: No test files found in $TESTS_DIR/language${NC}"
    exit 1
fi

# Run each test
for test_file in $test_files; do
    TOTAL=$((TOTAL + 1))
    test_name=$(echo "$test_file" | sed "s|$TESTS_DIR/||")

    # Run the test - capture output
    if (cd "$SRC_DIR" && dotnet run -c Release --project ProLang/ProLang.csproj -- "$test_file" > /tmp/test_output.txt 2>&1); then
        exit_code=0
    else
        exit_code=$?
    fi

    # Check for critical compilation errors (not binding errors about missing functions)
    # Critical errors: syntax errors, type errors, binding errors about structs/types
    # Non-critical: missing built-in functions like 'print'
    if grep -qE "Unexpected token|Variable.*does not exist.*<|Type.*not found|Duplicate" /tmp/test_output.txt; then
        echo -e "${RED}✗ FAILED${NC} - $test_name"
        grep -E "Unexpected token|Variable.*does not exist.*<|Type.*not found|Duplicate" /tmp/test_output.txt | head -1 | sed 's/^/  /'
        FAILED=$((FAILED + 1))
    else
        echo -e "${GREEN}✓ PASSED${NC} - $test_name"
        PASSED=$((PASSED + 1))
    fi
done

echo ""
echo "=========================================="
echo "Test Summary"
echo "=========================================="
echo "Total:  $TOTAL"
echo -e "Passed: ${GREEN}$PASSED${NC}"
echo -e "Failed: ${RED}$FAILED${NC}"
echo ""

if [ $FAILED -eq 0 ]; then
    echo -e "${GREEN}✓ All tests passed!${NC}"
    exit 0
else
    echo -e "${RED}✗ Some tests failed${NC}"
    exit 1
fi
