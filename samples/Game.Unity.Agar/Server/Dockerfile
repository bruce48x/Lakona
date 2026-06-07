# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/runtime:10.0.7 AS runtime
WORKDIR /app
ENV DOTNET_EnableDiagnostics=0

FROM mcr.microsoft.com/dotnet/sdk:10.0.203 AS build
WORKDIR /src

COPY NuGet.config Directory.Build.props ./
COPY src/ULinkGame.Abstractions/ULinkGame.Abstractions.csproj src/ULinkGame.Abstractions/
COPY src/ULinkGame.Server/ULinkGame.Server.csproj src/ULinkGame.Server/
COPY samples/Game.Unity.Agar/Shared/Shared.csproj samples/Game.Unity.Agar/Shared/
COPY samples/Game.Unity.Agar/Shared/Directory.Build.props samples/Game.Unity.Agar/Shared/
COPY samples/Game.Unity.Agar/Server/State.Contracts/State.Contracts.csproj samples/Game.Unity.Agar/Server/State.Contracts/
COPY samples/Game.Unity.Agar/Server/State/State.csproj samples/Game.Unity.Agar/Server/State/
COPY samples/Game.Unity.Agar/Server/Gateway/Gateway.csproj samples/Game.Unity.Agar/Server/Gateway/

RUN dotnet restore samples/Game.Unity.Agar/Server/Gateway/Gateway.csproj

COPY src/ULinkGame.Abstractions/ src/ULinkGame.Abstractions/
COPY src/ULinkGame.Server/ src/ULinkGame.Server/
COPY samples/Game.Unity.Agar/Shared/ samples/Game.Unity.Agar/Shared/
COPY samples/Game.Unity.Agar/Server/ samples/Game.Unity.Agar/Server/

FROM build AS publish-gateway
RUN dotnet publish samples/Game.Unity.Agar/Server/Gateway/Gateway.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish/gateway

FROM runtime AS gateway
COPY --from=publish-gateway /app/publish/gateway ./
EXPOSE 20000 20001
USER $APP_UID
ENTRYPOINT ["dotnet", "Gateway.dll"]
