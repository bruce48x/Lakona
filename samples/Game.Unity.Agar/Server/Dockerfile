# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/aspnet:10.0.7 AS runtime
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
RUN dotnet restore samples/Game.Unity.Agar/Server/Hotfix/Server.Hotfix.csproj

COPY samples/Game.Unity.Agar/Shared/ samples/Game.Unity.Agar/Shared/
COPY samples/Game.Unity.Agar/Server/ samples/Game.Unity.Agar/Server/

FROM build AS publish-gateway
RUN dotnet publish samples/Game.Unity.Agar/Server/App/Server.App.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish/gateway
RUN dotnet build samples/Game.Unity.Agar/Server/Hotfix/Server.Hotfix.csproj \
    --configuration Release \
    --no-restore

FROM runtime AS gateway
COPY --from=publish-gateway /app/publish/gateway ./
COPY --from=publish-gateway /src/samples/Game.Unity.Agar/Server/App/bin/Release/net10.0/hotfix ./hotfix/
RUN rm -f appsettings*.json
EXPOSE 20000 20001 21000
USER $APP_UID
ENTRYPOINT ["dotnet", "Server.App.dll"]
