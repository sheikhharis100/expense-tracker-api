# 1) Build the React app.
FROM node:22-slim AS web
WORKDIR /web
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

# 2) Build the API.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY global.json ./
COPY backend/ExpenseTracker.Api.csproj backend/
COPY backend/ExpenseTracker.Formulas/ExpenseTracker.Formulas.csproj backend/ExpenseTracker.Formulas/
RUN dotnet restore backend/ExpenseTracker.Api.csproj -r linux-x64 -p:PublishReadyToRun=true
COPY backend/ backend/
# ReadyToRun precompiles the app so it wakes up faster on small free-tier machines.
RUN dotnet publish backend/ExpenseTracker.Api.csproj -c Release -o /app/publish --no-restore -r linux-x64 --self-contained false -p:PublishReadyToRun=true

# 3) One container serves the API, the live-update hub and the app.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=web /web/dist ./wwwroot
EXPOSE 8080
ENV ASPNETCORE_ENVIRONMENT=Production
# Workstation GC keeps memory well inside a 512 MB free instance.
ENV DOTNET_gcServer=0
ENTRYPOINT ["dotnet", "ExpenseTracker.Api.dll"]
