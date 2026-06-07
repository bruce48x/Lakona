#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../../.." && pwd)"
WORK_DIR="$ROOT_DIR/.tmp/starter-godot-daily"
GENERATED_ROOT="$WORK_DIR/generated"
TRANSPORT="${STARTER_TRANSPORT:-websocket}"
SERIALIZER="${STARTER_SERIALIZER:-memorypack}"
TRANSPORT_LABEL="$(tr '[:lower:]' '[:upper:]' <<< "${TRANSPORT:0:1}")${TRANSPORT:1}"
SERIALIZER_LABEL="$(tr '[:lower:]' '[:upper:]' <<< "${SERIALIZER:0:1}")${SERIALIZER:1}"
PROJECT_NAME="StarterGodot${TRANSPORT_LABEL}${SERIALIZER_LABEL}"
PROJECT_DIR="$GENERATED_ROOT/$PROJECT_NAME"
CLIENT_DIR="$PROJECT_DIR/Client"
SERVER_PROJECT="$PROJECT_DIR/Server/Server/Server.csproj"
CLIENT_PROJECT="$CLIENT_DIR/Client.csproj"
LOG_DIR="$WORK_DIR/logs"
SERVER_LOG="$LOG_DIR/server.log"
CLIENT_LOG="$LOG_DIR/client.log"
GODOT_STDOUT_LOG="$LOG_DIR/godot.stdout.log"
LOCAL_FEED="$ROOT_DIR/artifacts/ci-nuget"
CI_NUGET_CONFIG="$WORK_DIR/NuGet.config"

if [[ -z "${GODOT_BIN:-}" || -z "${GODOT_NUPKGS:-}" ]]; then
  echo "GODOT_BIN and GODOT_NUPKGS must be set." >&2
  exit 1
fi

case "$TRANSPORT" in
  tcp|websocket|kcp)
    ;;
  *)
    echo "Unsupported STARTER_TRANSPORT: $TRANSPORT" >&2
    exit 1
    ;;
esac

case "$SERIALIZER" in
  json|memorypack)
    ;;
  *)
    echo "Unsupported STARTER_SERIALIZER: $SERIALIZER" >&2
    exit 1
    ;;
esac

rm -rf "$WORK_DIR" "$LOCAL_FEED"
mkdir -p "$GENERATED_ROOT" "$LOG_DIR" "$LOCAL_FEED"

cleanup() {
  terminate_process "${GODOT_PID:-}" "godot"
  terminate_process "${SERVER_PID:-}" "server"
}

trap cleanup EXIT

pack_local_package() {
  local project_path="$1"
  dotnet pack "$project_path" -c Release -o "$LOCAL_FEED" --nologo
}

print_logs() {
  if [[ -f "$SERVER_LOG" ]]; then
    echo "===== server.log =====" >&2
    cat "$SERVER_LOG" >&2
  fi

  if [[ -f "$CLIENT_LOG" ]]; then
    echo "===== client.log =====" >&2
    cat "$CLIENT_LOG" >&2
  fi

  if [[ -f "$GODOT_STDOUT_LOG" ]]; then
    echo "===== godot.stdout.log =====" >&2
    cat "$GODOT_STDOUT_LOG" >&2
  fi
}

wait_for_log() {
  local pattern="$1"
  local file_path="$2"
  local attempts="${3:-30}"

  for ((i = 0; i < attempts; i++)); do
    if grep -Fq "$pattern" "$file_path" 2>/dev/null; then
      return 0
    fi

    sleep 1
  done

  return 1
}

terminate_process() {
  local pid="${1:-}"
  local name="${2:-process}"

  if [[ -z "$pid" ]]; then
    return 0
  fi

  if ! kill -0 "$pid" 2>/dev/null; then
    return 0
  fi

  kill "$pid" 2>/dev/null || true

  for ((i = 0; i < 10; i++)); do
    if ! kill -0 "$pid" 2>/dev/null; then
      wait "$pid" 2>/dev/null || true
      return 0
    fi

    sleep 1
  done

  echo "Force killing lingering $name process $pid." >&2
  kill -9 "$pid" 2>/dev/null || true
  wait "$pid" 2>/dev/null || true
}

wait_for_port() {
  local host="$1"
  local port="$2"
  local attempts="${3:-60}"

  for ((i = 0; i < attempts; i++)); do
    if bash -c "</dev/tcp/$host/$port" >/dev/null 2>&1; then
      return 0
    fi

    if [[ -n "${SERVER_PID:-}" ]] && ! kill -0 "$SERVER_PID" 2>/dev/null; then
      echo "Server process exited before port $port became ready." >&2
      return 1
    fi

    sleep 1
  done

  echo "Timed out waiting for $host:$port." >&2
  return 1
}

wait_for_server_ready() {
  local attempts="${1:-60}"
  local expected_log=""

  case "$TRANSPORT" in
    websocket)
      expected_log="listening on ws://"
      ;;
    tcp)
      expected_log="listening on tcp://"
      ;;
    kcp)
      expected_log="listening on udp://"
      ;;
  esac

  if [[ -n "$expected_log" ]]; then
    for ((i = 0; i < attempts; i++)); do
      if grep -Fq "$expected_log" "$SERVER_LOG" 2>/dev/null; then
        return 0
      fi

      if [[ -n "${SERVER_PID:-}" ]] && ! kill -0 "$SERVER_PID" 2>/dev/null; then
        echo "Server process exited before readiness log appeared." >&2
        return 1
      fi

      sleep 1
    done
  fi

  echo "Timed out waiting for server readiness log: $expected_log" >&2
  return 1
}

cat > "$CI_NUGET_CONFIG" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$LOCAL_FEED" />
    <add key="godot-local" value="$GODOT_NUPKGS" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF

echo "Packing local packages into $LOCAL_FEED"
pack_local_package "$ROOT_DIR/src/Lakona.Rpc.Analyzers/Lakona.Rpc.Analyzers.csproj"
pack_local_package "$ROOT_DIR/src/Lakona.Rpc.Core/Lakona.Rpc.Core.csproj"
pack_local_package "$ROOT_DIR/src/Lakona.Rpc.Client/Lakona.Rpc.Client.csproj"
pack_local_package "$ROOT_DIR/src/Lakona.Rpc.Server/Lakona.Rpc.Server.csproj"

case "$TRANSPORT" in
  websocket)
    pack_local_package "$ROOT_DIR/src/Lakona.Rpc.Transport.WebSocket/Lakona.Rpc.Transport.WebSocket.csproj"
    ;;
  tcp)
    pack_local_package "$ROOT_DIR/src/Lakona.Rpc.Transport.Tcp/Lakona.Rpc.Transport.Tcp.csproj"
    ;;
  kcp)
    pack_local_package "$ROOT_DIR/src/Lakona.Rpc.Transport.Kcp/Lakona.Rpc.Transport.Kcp.csproj"
    ;;
esac

case "$SERIALIZER" in
  json)
    pack_local_package "$ROOT_DIR/src/Lakona.Rpc.Serializer.Json/Lakona.Rpc.Serializer.Json.csproj"
    ;;
  memorypack)
    pack_local_package "$ROOT_DIR/src/Lakona.Rpc.Serializer.MemoryPack/Lakona.Rpc.Serializer.MemoryPack.csproj"
    ;;
esac

export ULINKRPC_GODOT_NUPKGS="$GODOT_NUPKGS"

echo "Generating starter project at $PROJECT_DIR ($TRANSPORT + $SERIALIZER)"
dotnet run --project "$ROOT_DIR/src/Lakona.Rpc.Starter/Lakona.Rpc.Starter.csproj" -- \
  --name "$PROJECT_NAME" \
  --output "$GENERATED_ROOT" \
  --client-engine godot \
  --transport "$TRANSPORT" \
  --serializer "$SERIALIZER"

echo "Restoring and building generated server"
dotnet restore "$SERVER_PROJECT" --configfile "$CI_NUGET_CONFIG"
dotnet build "$SERVER_PROJECT" -c Release --no-restore

echo "Restoring and building generated Godot client"
dotnet restore "$CLIENT_PROJECT" --configfile "$CI_NUGET_CONFIG"
dotnet build "$CLIENT_PROJECT" -c Debug --no-restore

echo "Starting generated server"
dotnet run --project "$SERVER_PROJECT" -c Release --no-build >"$SERVER_LOG" 2>&1 &
SERVER_PID=$!

if [[ "$TRANSPORT" == "tcp" || "$TRANSPORT" == "websocket" ]]; then
  if ! wait_for_port 127.0.0.1 20000; then
    print_logs
    exit 1
  fi
fi

if ! wait_for_server_ready; then
  print_logs
  exit 1
fi

echo "Running generated Godot client headless"
"$GODOT_BIN" \
  --headless \
  --path "$CLIENT_DIR" \
  --scene "res://Main.tscn" \
  --log-file "$CLIENT_LOG" \
  --verbose \
  --no-header >"$GODOT_STDOUT_LOG" 2>&1 &
GODOT_PID=$!

if ! wait_for_log "RpcConnectionTester entered tree." "$CLIENT_LOG" 15; then
  echo "Godot loaded the project but the generated C# test node never entered the scene tree." >&2
  print_logs
  exit 1
fi

for ((i = 0; i < 90; i++)); do
  if grep -Fq "Connect failed:" "$CLIENT_LOG" 2>/dev/null; then
    echo "Godot client reported a connection failure." >&2
    print_logs
    exit 1
  fi

  if grep -Fq "Ping ok:" "$CLIENT_LOG" 2>/dev/null; then
    wait "$GODOT_PID"
    godot_exit_code=$?

    if [[ "$godot_exit_code" -ne 0 ]]; then
      echo "Godot client reached ping success but exited with code $godot_exit_code." >&2
      print_logs
      exit 1
    fi

    echo "Starter Godot $TRANSPORT + $SERIALIZER verification passed."
    exit 0
  fi

  if ! kill -0 "$GODOT_PID" 2>/dev/null; then
    if wait "$GODOT_PID"; then
      godot_exit_code=0
    else
      godot_exit_code=$?
    fi
    echo "Godot process exited before producing a successful ping log. Exit code: $godot_exit_code" >&2
    print_logs
    exit 1
  fi

  sleep 1
done

echo "Timed out waiting for successful ping from Godot client." >&2
print_logs
exit 1
