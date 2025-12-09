#!/bin/bash
set -e

echo ">> Starting Sonar Scanner"
echo ">> GitHub Ref: ${GITHUB_REF_NAME}"
echo ">> Coverage files: ${COVERAGE_FILES}"
echo ">> Test result files: ${TEST_RESULTS_FILES}"

# Check if we have coverage files
if [ -z "${COVERAGE_FILES}" ] || [ "${COVERAGE_FILES}" = "," ]; then
    echo ">> ERROR: No coverage files found!"
    echo ">> Searching for coverage files in raw-coverage-files:"
    find raw-coverage-files -name "*.cobertura.xml" -type f
    exit 1
fi

# Remove trailing comma if present
COVERAGE_FILES=${COVERAGE_FILES%,}
TEST_RESULTS_FILES=${TEST_RESULTS_FILES%,}

echo ">> Processed coverage files: ${COVERAGE_FILES}"
echo ">> Processed test result files: ${TEST_RESULTS_FILES}"

if [ -n "${PULL_REQUEST_ID}" ] && [ "${PULL_REQUEST_ID}" != "0" ]; then
    echo ">> Running in PR mode for PR #${PULL_REQUEST_ID}"

    dotnet-sonarscanner begin \
        /o:"allmantool" \
        /k:"Allmantool_h-budget-accounting" \
        /n:"h-budget-accounting" \
        /v:"${GITHUB_RUN_ID}" \
        /d:sonar.login="${SONAR_TOKEN}" \
        /d:sonar.host.url="https://sonarcloud.io" \
        /d:sonar.pullrequest.key="${PULL_REQUEST_ID}" \
        /d:sonar.pullrequest.branch="${PULL_REQUEST_SOURCE_BRANCH}" \
        /d:sonar.pullrequest.base="${PULL_REQUEST_TARGET_BRANCH}" \
        /d:sonar.cs.cobertura.reportsPaths="${COVERAGE_FILES}" \
        /d:sonar.cs.vstest.reportsPaths="${TEST_RESULTS_FILES}" \
        /d:sonar.exclusions="**/*.Tests/**" \
        /d:sonar.coverage.exclusions="**/*.Tests/**,**/*Test.cs,**/*Tests.cs" \
        /d:sonar.pullrequest.provider="github" \
        /d:sonar.pullrequest.github.repository="Allmantool/h-budget.HomeBudget.AccountingApi" \
        /d:sonar.pullrequest.github.endpoint="https://api.github.com/" \
        /d:sonar.verbose="true"

else
    echo ">> Running for branch: ${GITHUB_REF_NAME}"

    dotnet-sonarscanner begin \
        /o:"allmantool" \
        /k:"Allmantool_h-budget-accounting" \
        /n:"h-budget-accounting" \
        /v:"${GITHUB_RUN_ID}" \
        /d:sonar.branch.name="${GITHUB_REF_NAME}" \
        /d:sonar.login="${SONAR_TOKEN}" \
        /d:sonar.host.url="https://sonarcloud.io" \
        /d:sonar.cs.cobertura.reportsPaths="${COVERAGE_FILES}" \
        /d:sonar.cs.vstest.reportsPaths="${TEST_RESULTS_FILES}" \
        /d:sonar.exclusions="**/*.Tests/**" \
        /d:sonar.coverage.exclusions="**/*.Tests/**,**/*Test.cs,**/*Tests.cs" \
        /d:sonar.verbose="true"
fi