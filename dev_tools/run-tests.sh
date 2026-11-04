#!/usr/bin/env bash
# Run every shell test and preserve any failure (including missing prerequisites).
set -u
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." || exit 1
shopt -s nullglob
tests=(dev_tools/test-*.sh)
(( ${#tests[@]} > 0 )) || { echo "No tests found" >&2; exit 1; }
failed=0
for test in "${tests[@]}"; do
    if bash "$test"; then
        printf 'PASS %s\n' "$test"
    else
        result=$?
        printf 'FAIL/UNVERIFIED %s (exit %s)\n' "$test" "$result" >&2
        failed=$((failed + 1))
    fi
done
printf '%s tests, %s failed or unverified\n' "${#tests[@]}" "$failed"
(( failed == 0 ))
