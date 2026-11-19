# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Restore first so the package layer is cached across source-only changes.
COPY Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/BuildingBlocks/OppOcr.BuildingBlocks.Application/*.csproj src/BuildingBlocks/OppOcr.BuildingBlocks.Application/
COPY src/Modules/Ocr/Modules.Ocr.Domain/*.csproj src/Modules/Ocr/Modules.Ocr.Domain/
COPY src/Modules/Ocr/Modules.Ocr.Application/*.csproj src/Modules/Ocr/Modules.Ocr.Application/
COPY src/Modules/Ocr/Modules.Ocr.Infrastructure/*.csproj src/Modules/Ocr/Modules.Ocr.Infrastructure/
COPY src/Hosts/Web/*.csproj src/Hosts/Web/
RUN dotnet restore src/Hosts/Web/OppOcr.Web.csproj

COPY src/ src/
RUN dotnet publish src/Hosts/Web/OppOcr.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "OppOcr.Web.dll"]
