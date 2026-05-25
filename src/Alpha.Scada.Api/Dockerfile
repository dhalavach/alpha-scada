FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Alpha.Scada.Api/Alpha.Scada.Api.csproj src/Alpha.Scada.Api/
RUN dotnet restore src/Alpha.Scada.Api/Alpha.Scada.Api.csproj

COPY src/Alpha.Scada.Api src/Alpha.Scada.Api
RUN dotnet publish src/Alpha.Scada.Api/Alpha.Scada.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Alpha.Scada.Api.dll"]
