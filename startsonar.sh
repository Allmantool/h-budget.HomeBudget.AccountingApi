#!/bin/bash

if [ ${PULL_REQUEST_ID} ]; then
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
        /d:sonar.cs.dotcover.reportsPaths="test-results/accounting-coverage.html" \
        /d:sonar.pullrequest.provider="github" \
        /d:sonar.pullrequest.github.repository="Allmantool/h-budget.HomeBudget.AccountingApi" \
        /d:sonar.pullrequest.github.endpoint="https://api.github.com/"
else
    if [[ "${PULL_REQUEST_SOURCE_BRANCH}" =~ "master" ]]; then
        PULL_REQUEST_SOURCE_BRANCH=""
    fi

    dotnet-sonarscanner begin \
        /k:"Allmantool_h-budget-accounting" \
        /o:"allmantool" \
        /n:"h-budget-accounting" \
        /v:"${GITHUB_RUN_ID}" \
        /d:sonar.branch.name="master" \
        /d:sonar.login="${SONAR_TOKEN}" \
        /d:sonar.host.url="https://sonarcloud.io" \
        /d:sonar.cs.dotcover.reportsPaths="test-results/accounting-coverage.html" \
        /d:sonar.coverage.exclusions="**/Test[s]/**/*"
fi
