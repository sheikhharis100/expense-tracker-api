FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ExpenseTracker.Api.csproj .
RUN dotnet restore ExpenseTracker.Api.csproj
COPY . .
RUN dotnet publish ExpenseTracker.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "ExpenseTracker.Api.dll"]
