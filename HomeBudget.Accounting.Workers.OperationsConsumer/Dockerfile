FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /scr

COPY --from=mcr.microsoft.com/dotnet/sdk:10.0 /usr/share/dotnet/shared /usr/share/dotnet/shared

ARG BUILD_CONFIGURATION=Release
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

# Not compatible with .net 10.0 (will be updated later)
# RUN dotnet tool install snitch --tool-path /tools --version 2.0.0

RUN dotnet tool restore

RUN echo "##vso[task.prependpath]$HOME/.dotnet/tools"
RUN export PATH="$PATH:/root/.dotnet/tools"
COPY ["Directory.Packages.props", "."]
COPY ["Directory.Build.props", "."]
COPY ["Directory.Build.targets", "."]
COPY ["HomeBudget.Accounting.Workers.OperationsConsumer/HomeBudget.Accounting.Workers.OperationsConsumer.csproj", "HomeBudget.Accounting.Workers.OperationsConsumer/"]
RUN dotnet restore "./HomeBudget.Accounting.Workers.OperationsConsumer/HomeBudget.Accounting.Workers.OperationsConsumer.csproj"
COPY . .
WORKDIR "/src/HomeBudget.Accounting.Workers.OperationsConsumer"
RUN dotnet build "./HomeBudget.Accounting.Workers.OperationsConsumer.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./HomeBudget.Accounting.Workers.OperationsConsumer.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
LABEL build_version="${BUILD_VERSION}"
LABEL service=OperationConsumerWorker
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "HomeBudget.Accounting.Workers.OperationsConsumer.dll"]