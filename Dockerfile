# Build from workspace parent directory so sibling ../Davids.Shared is in build context:
#   docker build -f Davids.Services.Payments/Dockerfile -t ghcr.io/davids/davids-services-payments:1.0.0 .
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /workspace

COPY Davids.Shared/Directory.Build.props Davids.Shared/Directory.Packages.props Davids.Shared/global.json ./Davids.Shared/
COPY Davids.Shared/eng/ ./Davids.Shared/eng/
COPY Davids.Shared/src/Davids.ServiceDefaults/Davids.ServiceDefaults.csproj Davids.Shared/src/Davids.ServiceDefaults/packages.lock.json ./Davids.Shared/src/Davids.ServiceDefaults/
COPY Davids.Services.Payments/Directory.Build.props Davids.Services.Payments/Directory.Packages.props Davids.Services.Payments/global.json ./Davids.Services.Payments/
COPY Davids.Services.Payments/src/Davids.Services.Payments.Core/Davids.Services.Payments.Core.csproj Davids.Services.Payments/src/Davids.Services.Payments.Core/packages.lock.json ./Davids.Services.Payments/src/Davids.Services.Payments.Core/
COPY Davids.Services.Payments/src/Davids.Services.Payments.Application/Davids.Services.Payments.Application.csproj Davids.Services.Payments/src/Davids.Services.Payments.Application/packages.lock.json ./Davids.Services.Payments/src/Davids.Services.Payments.Application/
COPY Davids.Services.Payments/src/Davids.Services.Payments.Infrastructure/Davids.Services.Payments.Infrastructure.csproj Davids.Services.Payments/src/Davids.Services.Payments.Infrastructure/packages.lock.json ./Davids.Services.Payments/src/Davids.Services.Payments.Infrastructure/
COPY Davids.Services.Payments/src/Davids.Services.Payments.Api/Davids.Services.Payments.Api.csproj Davids.Services.Payments/src/Davids.Services.Payments.Api/packages.lock.json ./Davids.Services.Payments/src/Davids.Services.Payments.Api/

RUN dotnet restore ./Davids.Services.Payments/src/Davids.Services.Payments.Api/Davids.Services.Payments.Api.csproj --locked-mode

COPY Davids.Shared/src/Davids.ServiceDefaults/ ./Davids.Shared/src/Davids.ServiceDefaults/
COPY Davids.Services.Payments/src/ ./Davids.Services.Payments/src/

RUN dotnet publish ./Davids.Services.Payments/src/Davids.Services.Payments.Api/Davids.Services.Payments.Api.csproj \
      --no-restore -c Release /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0

COPY --from=build --chown=10001:10001 /workspace/Davids.Services.Payments/artifacts/publish/Davids.Services.Payments.Api/release .
USER 10001:10001
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD ["dotnet", "--info"]

ENTRYPOINT ["dotnet", "Davids.Services.Payments.Api.dll"]
