# syntax=docker/dockerfile:1

# ---- build ----
# Runs on the CI runner's native arch: the publish output is architecture-neutral
# (framework-dependent, no apphost), so only the runtime stage needs the target arch.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY BookingTracker.Api.csproj ./
RUN dotnet restore BookingTracker.Api.csproj

COPY . .
RUN dotnet publish BookingTracker.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true

COPY --from=build /app/publish ./

# Built-in non-root user of the .NET images
USER $APP_UID
EXPOSE 8080

ENTRYPOINT ["dotnet", "BookingTracker.Api.dll"]
