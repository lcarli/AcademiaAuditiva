# Multi-stage build for Academia Auditiva (.NET 10 ASP.NET Core MVC)
#
# - Stage 1: SDK image to restore + publish
# - Stage 2: ASP.NET Core runtime image (alpine, smaller surface)
# - Runs as non-root user for hardening
# - Listens on 8080 (Container Apps default ingress target)

ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION}-alpine AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy the shared build props (target framework, central package versions)
# and the csproj first to leverage layer caching for restore
COPY ["Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["AcademiaAuditiva/AcademiaAuditiva.csproj", "AcademiaAuditiva/"]
RUN dotnet restore "AcademiaAuditiva/AcademiaAuditiva.csproj"

# Copy the rest and publish
COPY AcademiaAuditiva/ AcademiaAuditiva/
WORKDIR /src/AcademiaAuditiva
RUN dotnet publish "AcademiaAuditiva.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION}-alpine AS runtime
ARG APP_VERSION=dev
WORKDIR /app

# ICU is required because we run with globalization enabled (PT-BR / FR-CA
# resources). The base alpine image only ships invariant culture data.
RUN apk add --no-cache icu-libs icu-data-full tzdata

# Non-root user — the aspnet:10.0-alpine base image already ships a non-root
# 'app' user/group, so we just chown the working directory and rely on it.
RUN chown -R app:app /app

USER app

COPY --from=build --chown=app:app /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    APP_VERSION=$APP_VERSION \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

EXPOSE 8080

ENTRYPOINT ["dotnet", "AcademiaAuditiva.dll"]
