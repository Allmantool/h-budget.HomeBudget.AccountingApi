FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /scr

COPY --from=mcr.microsoft.com/dotnet/sdk:6.0 /usr/share/dotnet/shared /usr/share/dotnet/shared

ARG SONAR_TOKEN
ARG PULL_REQUEST_ID
ARG PULL_REQUEST_SOURCE_BRANCH
ARG PULL_REQUEST_TARGET_BRANCH
ARG GITHUB_RUN_ID

ENV SONAR_TOKEN=${SONAR_TOKEN}
ENV PULL_REQUEST_ID=${PULL_REQUEST_ID}
ENV PULL_REQUEST_SOURCE_BRANCH=${PULL_REQUEST_SOURCE_BRANCH}
ENV PULL_REQUEST_TARGET_BRANCH=${PULL_REQUEST_TARGET_BRANCH}
ENV GITHUB_RUN_ID=${GITHUB_RUN_ID}

RUN wget https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
RUN wget https://download.oracle.com/java/21/latest/jdk-21_linux-x64_bin.deb

RUN dpkg -i packages-microsoft-prod.deb
RUN dpkg -i jdk-21_linux-x64_bin.deb

RUN apt-get updates
RUN apt show default-jdk

RUN --mount=type=cache,target=/var/cache/apt \ 
    apt-get install -y --quiet --no-install-recommends \
    apt-transport-https && \
    apt-get -y autoremove && \
    apt-get clean autoclean && \
    apt-get clean autoclean

RUN --mount=type=cache,target=/var/cache/apt \      
    apt-get install -y --quiet --no-install-recommends \
    openjdk-21-jdk ant dos2unix ca-certificates-java dotnet-sdk-7.0 && \
    apt-get -y autoremove && \
    apt-get clean autoclean && \
    apt-get clean autoclean

RUN rm packages-microsoft-prod.deb
RUN rm jdk-21_linux-x64_bin.deb

# Fix certificate issues
RUN update-ca-certificates -f

ENV JAVA_HOME /usr/lib/jvm/java-21-openjdk-amd64/
RUN export JAVA_HOME
RUN export PATH=$PATH:$JAVA_HOME/bin

RUN dotnet new tool-manifest
RUN dotnet tool install dotnet-sonarscanner --tool-path /tools --version 5.13.1
RUN dotnet tool install dotnet-reportgenerator-globaltool --tool-path /tools --version 5.1.24
RUN dotnet tool install snitch --tool-path /tools --version 1.12.0

RUN dotnet tool restore

RUN echo "##vso[task.prependpath]$HOME/.dotnet/tools"
RUN export PATH="$PATH:/root/.dotnet/tools"

RUN echo '--->PULL_REQUEST_ID:' ${PULL_REQUEST_ID}
RUN echo '--->GITHUB_RUN_ID:' ${GITHUB_RUN_ID}
RUN echo '--->PULL_REQUEST_SOURCE_BRANCH:' ${PULL_REQUEST_SOURCE_BRANCH}
RUN echo '--->PULL_REQUEST_TARGET_BRANCH:' ${PULL_REQUEST_TARGET_BRANCH}

COPY ["HomeBudget.Accounting.Domain/*.csproj", "HomeBudget.Accounting.Domain/"]
COPY ["HomeBudget.Accounting.Api/*.csproj", "HomeBudget.Accounting.Api/"]

COPY . .

RUN dotnet restore ./HomeBudgetAccountingApi.sln

RUN dos2unix ./startsonar.sh
RUN chmod +x ./startsonar.sh
RUN ./startsonar.sh; exit 0;

RUN dotnet build HomeBudgetAccountingApi.sln --no-restore -c Release -o /app/build

LABEL build_version="${BUILD_VERSION}"
LABEL service=AccountingService

RUN dotnet test HomeBudgetAccountingApi.sln \
    --logger "trx" \
    --results-directory "/app/testresults/coverage" \
    --collect:"XPlat Code Coverage" \
    /p:CollectCoverage=true \
    /p:CoverletOutputFormat=opencover \
    /p:CoverletOutput="/app/testresults/coverage/accounting.coverage.xml"

RUN dotnet dev-certs https --trust

RUN /tools/reportgenerator \
    -reports:'/app/testresults/coverage/**/coverage.cobertura.xml' \
    -targetdir:'/app/testresults/coverage/reports' \
    -reporttypes:'SonarQube;Cobertura'; \
    exit 0;

RUN /tools/dotnet-sonarscanner end /d:sonar.login="${SONAR_TOKEN}"; exit 0;

RUN /tools/snitch

FROM build AS publish
RUN dotnet publish "HomeBudgetAccountingApi.sln" \
    --no-dependencies \
    --no-restore \
    --framework net7.0 \
    -c Release \
    -v Diagnostic \
    -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "HomeBudget.Accounting.Api.dll"]