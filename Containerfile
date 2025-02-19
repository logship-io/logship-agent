ARG BASE_IMAGE=mcr.microsoft.com/dotnet/sdk:9.0-jammy
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/runtime-deps:9.0-jammy

FROM $BASE_IMAGE AS build-env
WORKDIR /app
RUN apt-get update && apt-get install clang zlib1g-dev git -y && apt-get autoclean && apt-get autoremove
COPY . ./
RUN git submodule update --init --recursive
RUN dotnet publish src/ConsoleHost/Logship.Agent.ConsoleHost.csproj -c Release -o out

FROM $RUNTIME_IMAGE
RUN apt-get update && apt-get install libsystemd-dev -y && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build-env /app/out .
ENTRYPOINT ["./Logship.Agent.ConsoleHost"]