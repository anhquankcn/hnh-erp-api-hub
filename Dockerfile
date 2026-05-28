FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src

COPY ERPApiHub.sln Directory.Build.props ./
COPY src/ERPApiHub/ERPApiHub.API/ERPApiHub.API.csproj src/ERPApiHub/ERPApiHub.API/
COPY src/ERPApiHub/ERPApiHub.Application/ERPApiHub.Application.csproj src/ERPApiHub/ERPApiHub.Application/
COPY src/ERPApiHub/ERPApiHub.Domain/ERPApiHub.Domain.csproj src/ERPApiHub/ERPApiHub.Domain/
COPY src/ERPApiHub/ERPApiHub.Infrastructure/ERPApiHub.Infrastructure.csproj src/ERPApiHub/ERPApiHub.Infrastructure/
COPY src/ERPApiHub/ERPApiHub.Worker/ERPApiHub.Worker.csproj src/ERPApiHub/ERPApiHub.Worker/
COPY src/ERPApiHub/ERPApiHub.Gateway/ERPApiHub.Gateway.csproj src/ERPApiHub/ERPApiHub.Gateway/
COPY tests/ERPApiHub.Tests/ERPApiHub.Tests.csproj tests/ERPApiHub.Tests/
RUN dotnet restore ERPApiHub.sln

COPY . .
RUN dotnet publish src/ERPApiHub/ERPApiHub.API/ERPApiHub.API.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS runtime
WORKDIR /app

RUN apk add --no-cache curl

COPY --from=build /app/publish .

EXPOSE 8008

HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 CMD curl -f http://localhost:8008/health || exit 1

USER app

ENTRYPOINT ["dotnet", "ERPApiHub.API.dll"]
