#!/bin/bash
set -e

echo ">> Starting Sonar Scanner"

COVERAGE_REPORT_PATH="merged-coverage/opencover.xml"

#
# Pull Request Mode
#
if [ -n "${PULL_REQUEST_ID}" ] && [ "${PULL_REQUEST_ID}" != "0" ]; then
    echo ">> Running in Pull Request mode for PR #${PULL_REQUEST_ID}"

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
        /d:sonar.coverage.exclusions="**/Test[s]/**/*" \
        /d:sonar.cs.opencover.reportsPaths="${COVERAGE_REPORT_PATH}" \
        /d:sonar.pullrequest.provider="github" \
        /d:sonar.pullrequest.github.repository="Allmantool/h-budget.HomeBudget.AccountingApi" \
        /d:sonar.pullrequest.github.endpoint="https://api.github.com/"

#
# Normal branch analysis
#
else
    echo ">> Running for normal branch: ${GITHUB_REF_NAME}"

    dotnet-sonarscanner begin \
        /o:"allmantool" \
        /k:"Allmantool_h-budget-accounting" \
        /n:"h-budget-accounting" \
        /v:"${GITHUB_RUN_ID}" \
        /d:sonar.branch.name="${GITHUB_REF_NAME}" \
        /d:sonar.login="${SONAR_TOKEN}" \
        /d:sonar.host.url="https://sonarcloud.io" \
        /d:sonar.cs.opencover.reportsPaths="${COVERAGE_REPORT_PATH}" \
        /d:sonar.coverage.exclusions="**/Test[s]/**/*"
fi
