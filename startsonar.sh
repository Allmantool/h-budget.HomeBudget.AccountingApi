#!/bin/bash

if [ ${PULL_REQUEST_ID} ];
    then
        /tools/dotnet-sonarscanner begin \
        /o:"allmantool" \
        /k:"Allmantool_h-budget-accounting" \
        /n:"h-budget-accounting" \
        /v:"${GITHUB_RUN_ID}" \
        /d:sonar.login="${SONAR_TOKEN}" \
        /d:sonar.host.url="https://sonarcloud.io" \
        /d:sonar.pullrequest.key="${PULL_REQUEST_ID}" \
        /d:sonar.pullrequest.branch="${PULL_REQUEST_SOURCE_BRANCH:11}" \
        /d:sonar.pullrequest.base="${PULL_REQUEST_TARGET_BRANCH:11}" \
        /d:sonar.coverage.exclusions="**/Test[s]/**/*" \
        /d:sonar.coverageReportPaths="/app/testresults/coverage/reports/SonarQube.xml" \
        /d:sonar.cs.opencover.reportsPaths="/app/testresults/coverage/accounting.coverage.xml" \
        /d:sonar.pullrequest.provider="github" \
        /d:sonar.pullrequest.github.repository="Allmantool/h-budget.HomeBudget.AccountingApi" \
        /d:sonar.pullrequest.github.endpoint="https://api.github.com/"; \
    else \
        if [ ${PULL_REQUEST_SOURCE_BRANCH:11}=="master" ];
        then 
            PULL_REQUEST_SOURCE_BRANCH=""
        fi

        /tools/dotnet-sonarscanner begin \
        /k:"Allmantool_h-budget-accounting" \
        /o:"allmantool" \
        /n:"h-budget-accounting" \
        /v:"${GITHUB_RUN_ID}" \
        /d:sonar.branch.name="master" \
        /d:sonar.login="${SONAR_TOKEN}" \
        /d:sonar.host.url="https://sonarcloud.io" \
        /d:sonar.coverageReportPaths="/app/testresults/coverage/reports/SonarQube.xml" \
        /d:sonar.cs.opencover.reportsPaths="/app/testresults/coverage/accounting.coverage.xml" \
        /d:sonar.coverage.exclusions="**/Test[s]/**/*"; \
    fi;