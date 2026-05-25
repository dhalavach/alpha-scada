FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Floyd.Scada.Api/Floyd.Scada.Api.csproj src/Floyd.Scada.Api/
RUN dotnet restore src/Floyd.Scada.Api/Floyd.Scada.Api.csproj

COPY src/Floyd.Scada.Api src/Floyd.Scada.Api
RUN dotnet publish src/Floyd.Scada.Api/Floyd.Scada.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Floyd.Scada.Api.dll"]
