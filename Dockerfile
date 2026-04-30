# Multi-stage build for Academia Auditiva (.NET 8 ASP.NET Core MVC)
#
# - Stage 1: SDK image to restore + publish
# - Stage 2: ASP.NET Core runtime image (alpine, smaller surface)
# - Runs as non-root user for hardening
# - Listens on 8080 (Container Apps default ingress target)

ARG DOTNET_VERSION=8.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION}-alpine AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy csproj first to leverage layer caching for restore
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
WORKDIR /app

# Non-root user — the aspnet:8.0-alpine base image already ships a non-root
# 'app' user/group, so we just chown the working directory and rely on it.
RUN chown -R app:app /app

USER app

COPY --from=build --chown=app:app /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

EXPOSE 8080

ENTRYPOINT ["dotnet", "AcademiaAuditiva.dll"]
