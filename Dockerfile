FROM mcr.microsoft.com/dotnet/sdk:10.0 AS sdk
WORKDIR /app
ARG config=Release

COPY src ./src

RUN dotnet publish -c $config --no-self-contained src/*.sln

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=sdk /app/_output/net10.0/publish/. ./

# Docker Entry
ENTRYPOINT ["dotnet", "ServarrAPI.dll"]
