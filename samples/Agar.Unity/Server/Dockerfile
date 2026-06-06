# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/runtime:10.0.7 AS runtime
WORKDIR /app
ENV DOTNET_EnableDiagnostics=0

FROM mcr.microsoft.com/dotnet/sdk:10.0.203 AS build
WORKDIR /src

COPY NuGet.config Directory.Build.props ./
COPY src/ULinkGame.Abstractions/ULinkGame.Abstractions.csproj src/ULinkGame.Abstractions/
COPY src/ULinkGame.Server/ULinkGame.Server.csproj src/ULinkGame.Server/
COPY samples/Agar.Unity/Shared/Shared.csproj samples/Agar.Unity/Shared/
COPY samples/Agar.Unity/Shared/Directory.Build.props samples/Agar.Unity/Shared/
COPY samples/Agar.Unity/Server/State.Contracts/State.Contracts.csproj samples/Agar.Unity/Server/State.Contracts/
COPY samples/Agar.Unity/Server/State/State.csproj samples/Agar.Unity/Server/State/
COPY samples/Agar.Unity/Server/Gateway/Gateway.csproj samples/Agar.Unity/Server/Gateway/

RUN dotnet restore samples/Agar.Unity/Server/Gateway/Gateway.csproj

COPY src/ULinkGame.Abstractions/ src/ULinkGame.Abstractions/
COPY src/ULinkGame.Server/ src/ULinkGame.Server/
COPY samples/Agar.Unity/Shared/ samples/Agar.Unity/Shared/
COPY samples/Agar.Unity/Server/ samples/Agar.Unity/Server/

FROM build AS publish-gateway
RUN dotnet publish samples/Agar.Unity/Server/Gateway/Gateway.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish/gateway

FROM runtime AS gateway
COPY --from=publish-gateway /app/publish/gateway ./
EXPOSE 20000 20001
USER $APP_UID
ENTRYPOINT ["dotnet", "Gateway.dll"]
