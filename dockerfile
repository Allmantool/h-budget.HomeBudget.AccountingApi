FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /scr

COPY --from=mcr.microsoft.com/dotnet/sdk:9.0 /usr/share/dotnet/shared /usr/share/dotnet/shared

ARG BUILD_VERSION
ENV BUILD_VERSION=${BUILD_VERSION}

RUN --mount=type=cache,target=/var/cache/apt \
    apt-get update && \
    apt-get install -y --quiet --no-install-recommends \
    apt-transport-https && \
    apt-get -y autoremove && \
    apt-get clean autoclean

RUN wget https://download.oracle.com/java/21/latest/jdk-21_linux-x64_bin.tar.gz -O jdk-21_linux-x64_bin.tar.gz
RUN mkdir /usr/lib/jvm && \
    tar -xvf jdk-21_linux-x64_bin.tar.gz -C /usr/lib/jvm

RUN --mount=type=cache,target=/var/cache/apt \
    apt-get update && \   
    apt-get install -f -y --quiet --no-install-recommends \
    ant dos2unix ca-certificates-java && \
    apt-get -y autoremove && \
    apt-get clean autoclean

# Fix certificate issues
RUN update-ca-certificates -f

ENV JAVA_HOME /usr/lib/jvm/jdk-21.0.1
RUN export JAVA_HOME=/usr/lib/jvm/jdk-21.0.1
RUN export PATH=$JAVA_HOME/bin:$PATH

RUN dotnet new tool-manifest

# Not compatible with .net 9.0 (will be updated later)
# RUN dotnet tool install snitch --tool-path /tools --version 2.0.0

RUN dotnet tool restore

RUN echo "##vso[task.prependpath]$HOME/.dotnet/tools"
RUN export PATH="$PATH:/root/.dotnet/tools"

COPY ["HomeBudget.Accounting.Domain/*.csproj", "HomeBudget.Accounting.Domain/"]
COPY ["HomeBudget.Accounting.Api/*.csproj", "HomeBudget.Accounting.Api/"]
COPY ["HomeBudget.Components.Categories/*.csproj", "HomeBudget.Components.Categories/"]
COPY ["HomeBudget.Components.Contractors/*.csproj", "HomeBudget.Components.Contractors/"]
COPY ["HomeBudget.Components.Operations/*.csproj", "HomeBudget.Components.Operations/"]
COPY ["HomeBudget.Components.Accounts/*.csproj", "HomeBudget.Components.Accounts/"]
COPY ["HomeBudget.Accounting.Infrastructure/*.csproj", "HomeBudget.Accounting.Infrastructure/"]

# Test project no need for final docker image release, but can be cause of dependency mismatch
# COPY ["HomeBudget.Accounting.Api.IntegrationTests/*.csproj", "HomeBudget.Accounting.Api.IntegrationTests/"]

COPY . .

# Clean artifacts from test projects
RUN dotnet sln HomeBudgetAccountingApi.sln remove \
    HomeBudget.Accounting.Api.IntegrationTests/HomeBudget.Accounting.Api.IntegrationTests.csproj \
    HomeBudget.Accounting.Api.Tests/HomeBudget.Accounting.Api.Tests.csproj \
    HomeBudget.Components.Categories.Tests/HomeBudget.Components.Categories.Tests.csproj \
    HomeBudget.Components.Operations.Tests/HomeBudget.Components.Operations.Tests.csproj

RUN dotnet build HomeBudgetAccountingApi.sln -c Release --no-incremental  --framework:net9.0 -maxcpucount:1 -o /app/build

# Not compatible with .net 9.0 (will be updated later)
# RUN /tools/snitch

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet clean "HomeBudgetAccountingApi.sln" -c Release
RUN dotnet publish "HomeBudgetAccountingApi.sln" \
    --no-dependencies \
    --no-restore \
    /maxcpucount:1 \
    --framework net9.0 \
    -c $BUILD_CONFIGURATION \
    -v Diagnostic \
    -o /app/publish

FROM base AS final
WORKDIR /app
LABEL build_version="${BUILD_VERSION}"
LABEL service=AccountingService
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "HomeBudget.Accounting.Api.dll"]