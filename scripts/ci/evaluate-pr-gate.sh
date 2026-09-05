#!/usr/bin/env bash
set -euo pipefail

readonly mandatory_jobs=(
  "Validate Application Build"
  "Run Unit And Component Tests"
  "Run API Integration Tests"
  "Analyze Master Quality"
)

if [[ "$#" -ne "${#mandatory_jobs[@]}" ]]; then
  echo "Expected ${#mandatory_jobs[@]} mandatory job results, received $#." >&2
  exit 2
fi

all_succeeded=true

for index in "${!mandatory_jobs[@]}"; do
  job_result="${@:$((index + 1)):1}"

  if [[ "${job_result}" == "success" ]]; then
    echo "${mandatory_jobs[index]}: success"
    continue
  fi

  echo "${mandatory_jobs[index]}: ${job_result:-missing} (mandatory result is not success)" >&2
  all_succeeded=false
done

if [[ "${all_succeeded}" != "true" ]]; then
  exit 1
fi

echo "All mandatory CI jobs succeeded."
