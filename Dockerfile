FROM node:20-bookworm-slim AS frontend
WORKDIR /src/src/Talos.Web/ClientApp
COPY src/Talos.Web/ClientApp/package.json src/Talos.Web/ClientApp/package-lock.json ./
RUN npm ci
COPY src/Talos.Web/ClientApp/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Talos.Web/Talos.Web.csproj src/Talos.Web/
RUN dotnet restore src/Talos.Web/Talos.Web.csproj
COPY src/ src/
RUN rm -rf src/Talos.Web/wwwroot/*
COPY --from=frontend /src/src/Talos.Web/wwwroot/ src/Talos.Web/wwwroot/
RUN dotnet publish src/Talos.Web/Talos.Web.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish/ ./
RUN mkdir -p /app/data
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=8 \
    CMD curl --fail --silent --show-error http://127.0.0.1:8080/api/status | grep -q '"configured":true'
ENTRYPOINT ["dotnet", "Talos.Web.dll"]
