#!/usr/bin/env bash
set -euo pipefail

gate_script="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/evaluate-pr-gate.sh"

assert_passes() {
  local scenario="$1"
  shift

  if ! bash "${gate_script}" "$@"; then
    echo "Expected PR gate to pass for ${scenario}." >&2
    exit 1
  fi
}

assert_fails() {
  local scenario="$1"
  shift

  if bash "${gate_script}" "$@"; then
    echo "Expected PR gate to fail for ${scenario}." >&2
    exit 1
  fi
}

assert_passes "all mandatory jobs succeed" success success success success
assert_fails "build fails and dependents skip" failure skipped skipped skipped
assert_fails "unit tests fail" success failure success success
assert_fails "integration tests fail" success success failure success
assert_fails "quality verification fails" success success success failure
assert_fails "mandatory job is cancelled" success cancelled success success
assert_fails "mandatory job is skipped" success success skipped success
assert_fails "mandatory job result is absent" success success success

echo "PR gate result truth table passed."
