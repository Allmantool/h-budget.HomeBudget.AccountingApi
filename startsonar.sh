#!/bin/bash
set -euo pipefail

SONAR_ORGANIZATION="${SONAR_ORGANIZATION:-allmantool}"
SONAR_PROJECT_KEY="${SONAR_PROJECT_KEY:-Allmantool_h-budget-accounting-api}"
SONAR_PROJECT_NAME="${SONAR_PROJECT_NAME:-h-budget-accounting-api}"
SONAR_GITHUB_REPOSITORY="${SONAR_GITHUB_REPOSITORY:-${GITHUB_REPOSITORY:-Allmantool/h-budget.HomeBudget.AccountingApi}}"
OPENCOVER_REPORTS_PATH="${OPENCOVER_REPORTS_PATH:-}"
COVERAGE_FILE="${COVERAGE_FILE:-}"
TEST_RESULTS_PATH="${TEST_RESULTS_PATH:-test-results/**/*.trx}"

sanitize_csv_property() {
    local raw_value="${1:-}"

    if [[ -z "${raw_value}" ]]; then
        return 0
    fi

    printf '%s' "${raw_value}" | awk '
        BEGIN {
            RS = ","
            ORS = ""
        }
        {
            value = $0
            gsub(/^[[:space:]]+|[[:space:]]+$/, "", value)
            gsub(/^"+|"+$/, "", value)

            if (value != "" && !seen[value]++) {
                values[++count] = value
            }
        }
        END {
            for (i = 1; i <= count; i++) {
                printf "%s%s", values[i], (i < count ? "," : "")
            }
        }'
}

SONAR_EXCLUSIONS_SANITIZED="$(sanitize_csv_property "${SONAR_EXCLUSIONS:-}")"
SONAR_COVERAGE_EXCLUSIONS_SANITIZED="$(sanitize_csv_property "${SONAR_COVERAGE_EXCLUSIONS:-}")"

echo "DEBUG: SONAR_PROJECT_KEY='${SONAR_PROJECT_KEY}'"
echo "DEBUG: SONAR_PROJECT_NAME='${SONAR_PROJECT_NAME}'"
echo "DEBUG: SONAR_GITHUB_REPOSITORY='${SONAR_GITHUB_REPOSITORY}'"
echo "DEBUG: OPENCOVER_REPORTS_PATH='${OPENCOVER_REPORTS_PATH}'"
echo "DEBUG: COVERAGE_FILE='${COVERAGE_FILE}'"
echo "DEBUG: TEST_RESULTS_PATH='${TEST_RESULTS_PATH}'"

scanner_args=(
    begin
    /o:"${SONAR_ORGANIZATION}"
    /k:"${SONAR_PROJECT_KEY}"
    /n:"${SONAR_PROJECT_NAME}"
    /v:"${GITHUB_RUN_ID}"
    /d:sonar.token="${SONAR_TOKEN}"
    /d:sonar.host.url="https://sonarcloud.io"
)

if [[ -n "${OPENCOVER_REPORTS_PATH}" ]]; then
    scanner_args+=("/d:sonar.cs.opencover.reportsPaths=${OPENCOVER_REPORTS_PATH}")
fi

if [[ -n "${COVERAGE_FILE}" ]]; then
    scanner_args+=("/d:sonar.coverageReportPaths=${COVERAGE_FILE}")
fi

if [[ -n "${TEST_RESULTS_PATH}" ]]; then
    scanner_args+=("/d:sonar.cs.vstest.reportsPaths=${TEST_RESULTS_PATH}")
fi

if [[ -n "${PULL_REQUEST_ID:-}" && "${PULL_REQUEST_ID}" != "0" ]]; then
    scanner_args+=(
        /d:sonar.pullrequest.key="${PULL_REQUEST_ID}"
        /d:sonar.pullrequest.branch="${PULL_REQUEST_SOURCE_BRANCH}"
        /d:sonar.pullrequest.base="${PULL_REQUEST_TARGET_BRANCH}"
        /d:sonar.pullrequest.provider="github"
        /d:sonar.pullrequest.github.repository="${SONAR_GITHUB_REPOSITORY}"
        /d:sonar.pullrequest.github.endpoint="https://api.github.com/"
    )
else
    BRANCH_NAME="${GITHUB_REF_NAME:-master}"

    if [[ "${BRANCH_NAME}" == "master" ]]; then
        scanner_args+=("/d:sonar.branch.name=master")
    else
        scanner_args+=("/d:sonar.branch.name=${BRANCH_NAME}")
    fi
fi

if [[ -n "${SONAR_EXCLUSIONS_SANITIZED}" ]]; then
    scanner_args+=("/d:sonar.exclusions=${SONAR_EXCLUSIONS_SANITIZED}")
fi

if [[ -n "${SONAR_COVERAGE_EXCLUSIONS_SANITIZED}" ]]; then
    scanner_args+=("/d:sonar.coverage.exclusions=${SONAR_COVERAGE_EXCLUSIONS_SANITIZED}")
fi

dotnet-sonarscanner "${scanner_args[@]}"
