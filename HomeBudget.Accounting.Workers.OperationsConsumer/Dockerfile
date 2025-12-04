FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Ensure shared runtime exists for SDK (some CI cases need this)
COPY --from=mcr.microsoft.com/dotnet/sdk:10.0 /usr/share/dotnet/shared /usr/share/dotnet/shared

ARG BUILD_CONFIGURATION=Release
ARG BUILD_VERSION=local
ENV BUILD_VERSION=${BUILD_VERSION}

# Install deps (cached)
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
# .NET Tools manifest
# -------------------------
RUN dotnet new tool-manifest
RUN dotnet tool restore

COPY HomeBudget.Accounting.Workers.OperationsConsumer/*.csproj HomeBudget.Accounting.Workers.OperationsConsumer/

RUN dotnet restore "HomeBudget.Accounting.Workers.OperationsConsumer/HomeBudget.Accounting.Workers.OperationsConsumer.csproj"

# -------------------------
# Copy full source
# -------------------------
COPY . .

WORKDIR "/src/HomeBudget.Accounting.Workers.OperationsConsumer"
RUN dotnet build "HomeBudget.Accounting.Workers.OperationsConsumers.csproj" -c $BUILD_CONFIGURATION -o /app/build


FROM build AS publish
ARG BUILD_CONFIGURATION=Release
WORKDIR "/src/HomeBudget.Accounting.Workers.OperationsConsumer"
RUN dotnet publish "HomeBudget.Accounting.Workers.OperationsConsumer.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    /p:UseAppHost=false

FROM base AS final
WORKDIR /app

LABEL build_version="${BUILD_VERSION}"
LABEL service="OperationConsumerWorker"

COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "HomeBudget.Accounting.Workers.OperationsConsumer.dll"]
