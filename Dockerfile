FROM mcr.microsoft.com/dotnet/sdk:10.0 AS sdk
WORKDIR /app

ARG VERSION=1.0.0
ARG BRANCH=unknown
ARG GIT_COMMIT=unknown
ARG GIT_TAG=
ARG REPO_URL=https://github.com/Cularr/Ocularr.Update.API
ARG AUTHOR=Cularr
ARG config=Release

COPY src ./src

RUN dotnet publish -c $config --no-self-contained src/*.sln \
    -p:Version=${VERSION} \
    -p:AssemblyVersion=${VERSION} \
    -p:FileVersion=${VERSION} \
    -p:Branch=${BRANCH} \
    -p:GitCommit=${GIT_COMMIT} \
    -p:GitTag=${GIT_TAG} \
    -p:RepoUrl=${REPO_URL} \
    -p:Author=${AUTHOR} \
    -p:CI=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=sdk /app/_output/net10.0/publish/. ./

# Expose port
EXPOSE 5000/tcp

# Docker Entry
ENTRYPOINT ["dotnet", "ServarrAPI.dll"]
