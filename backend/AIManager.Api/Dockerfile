# Build context is the "backend" folder (see docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY AIManager.Api/AIManager.Api.csproj AIManager.Api/
RUN dotnet restore AIManager.Api/AIManager.Api.csproj
COPY . .
RUN dotnet publish AIManager.Api/AIManager.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "AIManager.Api.dll"]
