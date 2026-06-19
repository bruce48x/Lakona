# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/runtime:10.0.7 AS runtime
WORKDIR /app
ENV DOTNET_EnableDiagnostics=0

FROM mcr.microsoft.com/dotnet/sdk:10.0.203 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/ src/
COPY samples/Game.Unity.Agar/Shared/Shared.csproj samples/Game.Unity.Agar/Shared/
COPY samples/Game.Unity.Agar/Shared/Directory.Build.props samples/Game.Unity.Agar/Shared/
COPY samples/Game.Unity.Agar/Server/App/Server.App.csproj samples/Game.Unity.Agar/Server/App/
COPY samples/Game.Unity.Agar/Server/Hotfix/Server.Hotfix.csproj samples/Game.Unity.Agar/Server/Hotfix/

RUN dotnet restore samples/Game.Unity.Agar/Server/App/Server.App.csproj

COPY samples/Game.Unity.Agar/Shared/ samples/Game.Unity.Agar/Shared/
COPY samples/Game.Unity.Agar/Server/ samples/Game.Unity.Agar/Server/

FROM build AS publish-gateway
RUN dotnet publish samples/Game.Unity.Agar/Server/App/Server.App.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish/gateway

FROM runtime AS gateway
COPY --from=publish-gateway /app/publish/gateway ./
EXPOSE 20000 20001
USER $APP_UID
ENTRYPOINT ["dotnet", "Server.App.dll"]
