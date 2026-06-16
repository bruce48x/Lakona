# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/runtime:10.0.7 AS runtime
WORKDIR /app
ENV DOTNET_EnableDiagnostics=0

FROM mcr.microsoft.com/dotnet/sdk:10.0.203 AS build
WORKDIR /src

COPY NuGet.config Directory.Build.props ./
COPY src/Lakona.Rpc.Core/Lakona.Rpc.Core.csproj src/Lakona.Rpc.Core/
COPY src/Lakona.Rpc.Server/Lakona.Rpc.Server.csproj src/Lakona.Rpc.Server/
COPY src/Lakona.Rpc.Analyzers/Lakona.Rpc.Analyzers.csproj src/Lakona.Rpc.Analyzers/
COPY src/Lakona.Rpc.Serializer.MemoryPack/Lakona.Rpc.Serializer.MemoryPack.csproj src/Lakona.Rpc.Serializer.MemoryPack/
COPY src/Lakona.Rpc.Transport.Kcp/Lakona.Rpc.Transport.Kcp.csproj src/Lakona.Rpc.Transport.Kcp/
COPY src/Lakona.Rpc.Transport.WebSocket/Lakona.Rpc.Transport.WebSocket.csproj src/Lakona.Rpc.Transport.WebSocket/
COPY src/Lakona.Game.Cluster/Lakona.Game.Cluster.csproj src/Lakona.Game.Cluster/
COPY src/Lakona.Game.Abstractions/Lakona.Game.Abstractions.csproj src/Lakona.Game.Abstractions/
COPY src/Lakona.Game.Server/Lakona.Game.Server.csproj src/Lakona.Game.Server/
COPY src/Lakona.Game.Server.Hotfix/Lakona.Game.Server.Hotfix.csproj src/Lakona.Game.Server.Hotfix/
COPY src/Lakona.Game.Server.Hotfix.Abstractions/Lakona.Game.Server.Hotfix.Abstractions.csproj src/Lakona.Game.Server.Hotfix.Abstractions/
COPY src/Lakona.Game.Server.Hotfix.Generators/Lakona.Game.Server.Hotfix.Generators.csproj src/Lakona.Game.Server.Hotfix.Generators/
COPY samples/Game.Unity.Agar/Shared/Shared.csproj samples/Game.Unity.Agar/Shared/
COPY samples/Game.Unity.Agar/Shared/Directory.Build.props samples/Game.Unity.Agar/Shared/
COPY samples/Game.Unity.Agar/Server/App/Server.App.csproj samples/Game.Unity.Agar/Server/App/
COPY samples/Game.Unity.Agar/Server/Hotfix/Server.Hotfix.csproj samples/Game.Unity.Agar/Server/Hotfix/

RUN dotnet restore samples/Game.Unity.Agar/Server/App/Server.App.csproj

COPY src/Lakona.Rpc.Core/ src/Lakona.Rpc.Core/
COPY src/Lakona.Rpc.Server/ src/Lakona.Rpc.Server/
COPY src/Lakona.Rpc.Analyzers/ src/Lakona.Rpc.Analyzers/
COPY src/Lakona.Rpc.Serializer.MemoryPack/ src/Lakona.Rpc.Serializer.MemoryPack/
COPY src/Lakona.Rpc.Transport.Kcp/ src/Lakona.Rpc.Transport.Kcp/
COPY src/Lakona.Rpc.Transport.WebSocket/ src/Lakona.Rpc.Transport.WebSocket/
COPY src/Lakona.Game.Cluster/ src/Lakona.Game.Cluster/
COPY src/Lakona.Game.Abstractions/ src/Lakona.Game.Abstractions/
COPY src/Lakona.Game.Server/ src/Lakona.Game.Server/
COPY src/Lakona.Game.Server.Hotfix/ src/Lakona.Game.Server.Hotfix/
COPY src/Lakona.Game.Server.Hotfix.Abstractions/ src/Lakona.Game.Server.Hotfix.Abstractions/
COPY src/Lakona.Game.Server.Hotfix.Generators/ src/Lakona.Game.Server.Hotfix.Generators/
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
