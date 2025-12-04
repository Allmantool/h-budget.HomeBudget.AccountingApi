FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

ARG BUILD_CONFIGURATION=Release
ARG BUILD_VERSION=local
ENV BUILD_VERSION=${BUILD_VERSION}

# -------------------------
# Install dependencies
# -------------------------
RUN --mount=type=cache,target=/var/cache/apt \
    apt-get update && \
    apt-get install -y --quiet --no-install-recommends \
        apt-transport-https ca-certificates-java ant dos2unix && \
    apt-get -y autoremove && \
    apt-get clean autoclean

# -------------------------
# Install Java 21 (Oracle)
# -------------------------
RUN wget https://download.oracle.com/java/21/latest/jdk-21_linux-x64_bin.tar.gz -O /tmp/jdk.tar.gz && \
    mkdir -p /usr/lib/jvm && \
    tar -xvf /tmp/jdk.tar.gz -C /usr/lib/jvm && \
    rm /tmp/jdk.tar.gz

ENV JAVA_HOME=/usr/lib/jvm/jdk-21.0.1
ENV PATH="$JAVA_HOME/bin:${PATH}"

RUN update-ca-certificates -f

# -------------------------
# Copy csproj files first (for caching)
# -------------------------
COPY Directory.Build.props ./
COPY Directory.Packages.props ./
COPY *.sln ./
COPY HomeBudget.Accounting.Workers.OperationsConsumer/*.csproj HomeBudget.Accounting.Workers.OperationsConsumer/
COPY HomeBudget.Accounting.Domain/*.csproj HomeBudget.Accounting.Domain/
COPY HomeBudget.Accounting.Infrastructure/*.csproj HomeBudget.Accounting.Infrastructure/
COPY HomeBudget.Components.Contractors/*.csproj HomeBudget.Components.Contractors/
COPY HomeBudget.Components.Categories/*.csproj HomeBudget.Components.Categories/
COPY HomeBudget.Components.Operations/*.csproj HomeBudget.Components.Operations/
COPY HomeBudget.Core/*.csproj HomeBudget.Core/
COPY HomeBudget.Components.Accounts/*.csproj HomeBudget.Components.Accounts/

RUN dotnet restore "HomeBudget.Accounting.Workers.OperationsConsumer/HomeBudget.Accounting.Workers.OperationsConsumer.csproj"

COPY . .

WORKDIR "/src/HomeBudget.Accounting.Workers.OperationsConsumer"
RUN dotnet build "HomeBudget.Accounting.Workers.OperationsConsumer.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
WORKDIR "/src/HomeBudget.Accounting.Workers.OperationsConsumer"
RUN dotnet publish "HomeBudget.Accounting.Workers.OperationsConsumer.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    /p:UseAppHost=false

FROM base AS final
WORKDIR /app

LABEL build_version="${BUILD_VERSION}"
LABEL service="OperationConsumerWorker"

# Copy published output
COPY --from=publish /app/publish .

# Entry point
ENTRYPOINT ["dotnet", "HomeBudget.Accounting.Workers.OperationsConsumer.dll"]
