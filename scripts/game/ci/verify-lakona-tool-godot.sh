#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../../.." && pwd)"
WORK_DIR="$ROOT_DIR/.tmp/lakona-tool-godot-daily"
GENERATED_ROOT="$WORK_DIR/generated"
TOOLS_DIR="$WORK_DIR/tools"
LOG_DIR="$WORK_DIR/logs"
LOCAL_FEED="$ROOT_DIR/artifacts/ci-nuget"
CI_NUGET_CONFIG="$WORK_DIR/NuGet.config"

TRANSPORT="${LAKONA_TOOL_TRANSPORT:-kcp}"
SERIALIZER="${LAKONA_TOOL_SERIALIZER:-memorypack}"
TRANSPORT_LABEL="$(tr '[:lower:]' '[:upper:]' <<< "${TRANSPORT:0:1}")${TRANSPORT:1}"
SERIALIZER_LABEL="$(tr '[:lower:]' '[:upper:]' <<< "${SERIALIZER:0:1}")${SERIALIZER:1}"
PROJECT_NAME="LakonaGodot${TRANSPORT_LABEL}${SERIALIZER_LABEL}"
PROJECT_DIR="$GENERATED_ROOT/$PROJECT_NAME"
CLIENT_DIR="$PROJECT_DIR/Client"
CLIENT_PROJECT=""
SERVER_SOLUTION="$PROJECT_DIR/Server/Server.slnx"
SERVER_PROJECT="$PROJECT_DIR/Server/App/Server.App.csproj"
SERVER_LOG="$LOG_DIR/server.log"
CLIENT_LOG="$LOG_DIR/client.log"
GODOT_STDOUT_LOG="$LOG_DIR/godot.stdout.log"

if [[ -z "${GODOT_BIN:-}" || -z "${GODOT_NUPKGS:-}" ]]; then
  echo "GODOT_BIN and GODOT_NUPKGS must be set." >&2
  exit 1
fi

case "$TRANSPORT" in
  tcp|websocket|kcp) ;;
  *)
    echo "Unsupported LAKONA_TOOL_TRANSPORT: $TRANSPORT" >&2
    exit 1
    ;;
esac

case "$SERIALIZER" in
  json|memorypack) ;;
  *)
    echo "Unsupported LAKONA_TOOL_SERIALIZER: $SERIALIZER" >&2
    exit 1
    ;;
esac

terminate_process() {
  local pid="${1:-}"
  local name="${2:-process}"

  if [[ -z "$pid" ]] || ! kill -0 "$pid" 2>/dev/null; then
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

cleanup() {
  terminate_process "${GODOT_PID:-}" "godot"
  terminate_process "${SERVER_PID:-}" "server"
}

trap cleanup EXIT

print_logs() {
  for log in "$SERVER_LOG" "$CLIENT_LOG" "$GODOT_STDOUT_LOG"; do
    if [[ -f "$log" ]]; then
      echo "===== $log =====" >&2
      cat "$log" >&2
    fi
  done
}

wait_for_server_ready() {
  local readiness_url="http://127.0.0.1:20080/_lakona/health/ready"

  for ((i = 0; i < 60; i++)); do
    if curl --fail --silent --show-error --output /dev/null "$readiness_url" 2>/dev/null; then
      return 0
    fi

    if [[ -n "${SERVER_PID:-}" ]] && ! kill -0 "$SERVER_PID" 2>/dev/null; then
      echo "Server process exited before application readiness." >&2
      return 1
    fi

    sleep 1
  done

  echo "Timed out waiting for application readiness at $readiness_url." >&2
  return 1
}

resolve_single_project() {
  local search_dir="$1"
  local label="$2"
  local projects=()

  if [[ ! -d "$search_dir" ]]; then
    echo "$label directory does not exist: $search_dir" >&2
    return 1
  fi

  mapfile -t projects < <(find "$search_dir" -maxdepth 1 -type f -name "*.csproj" | sort)
  case "${#projects[@]}" in
    1)
      printf '%s\n' "${projects[0]}"
      ;;
    0)
      echo "No $label project file found in $search_dir." >&2
      return 1
      ;;
    *)
      echo "Multiple $label project files found in $search_dir:" >&2
      printf '  %s\n' "${projects[@]}" >&2
      return 1
      ;;
  esac
}

resolve_godot_main_scene() {
  local project_file="$CLIENT_DIR/project.godot"
  local scene=""
  local scene_file=""

  if [[ ! -f "$project_file" ]]; then
    echo "Godot project file not found: $project_file" >&2
    return 1
  fi

  scene="$(awk -F'"' '/^[[:space:]]*run\/main_scene[[:space:]]*=/ { print $2; exit }' "$project_file")"
  if [[ -z "$scene" ]]; then
    echo "Godot project does not declare application run/main_scene: $project_file" >&2
    return 1
  fi

  if [[ "$scene" != res://* ]]; then
    echo "Unsupported Godot main scene path in $project_file: $scene" >&2
    return 1
  fi

  scene_file="$CLIENT_DIR/${scene#res://}"
  if [[ ! -f "$scene_file" ]]; then
    echo "Godot main scene does not exist: $scene ($scene_file)" >&2
    return 1
  fi

  printf '%s\n' "$scene"
}

pack_local_package() {
  local project_path="$1"
  dotnet pack "$project_path" -c Release -o "$LOCAL_FEED" --nologo
}

rm -rf "$WORK_DIR" "$LOCAL_FEED"
mkdir -p "$GENERATED_ROOT" "$TOOLS_DIR" "$LOG_DIR" "$LOCAL_FEED"

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

echo "Packing local Lakona packages into $LOCAL_FEED"
pack_local_package "$ROOT_DIR/src/Lakona.Rpc.Core/Lakona.Rpc.Core.csproj"
pack_local_package "$ROOT_DIR/src/Lakona.Rpc.Client/Lakona.Rpc.Client.csproj"
pack_local_package "$ROOT_DIR/src/Lakona.Rpc.Server/Lakona.Rpc.Server.csproj"
pack_local_package "$ROOT_DIR/src/Lakona.Rpc.Transport.WebSocket/Lakona.Rpc.Transport.WebSocket.csproj"
pack_local_package "$ROOT_DIR/src/Lakona.Rpc.Transport.Tcp/Lakona.Rpc.Transport.Tcp.csproj"
pack_local_package "$ROOT_DIR/src/Lakona.Rpc.Transport.Kcp/Lakona.Rpc.Transport.Kcp.csproj"
pack_local_package "$ROOT_DIR/src/Lakona.Rpc.Serializer.Json/Lakona.Rpc.Serializer.Json.csproj"
pack_local_package "$ROOT_DIR/src/Lakona.Rpc.Serializer.MemoryPack/Lakona.Rpc.Serializer.MemoryPack.csproj"
pack_local_package "$ROOT_DIR/src/Lakona.Game.Abstractions/Lakona.Game.Abstractions.csproj"
pack_local_package "$ROOT_DIR/src/Lakona.Game.Client/Lakona.Game.Client.csproj"
pack_local_package "$ROOT_DIR/src/Lakona.Game.Server/Lakona.Game.Server.csproj"
pack_local_package "$ROOT_DIR/src/Lakona.Tool/Lakona.Tool.csproj"

echo "Generating Lakona Godot project at $PROJECT_DIR ($TRANSPORT + $SERIALIZER)"
dotnet run --project "$ROOT_DIR/src/Lakona.Tool/Lakona.Tool.csproj" -- \
  new \
  --name "$PROJECT_NAME" \
  --output "$GENERATED_ROOT" \
  --client-engine godot \
  --transport "$TRANSPORT" \
  --serializer "$SERIALIZER"

CLIENT_PROJECT="$(resolve_single_project "$CLIENT_DIR" "Godot client")"
GODOT_MAIN_SCENE="$(resolve_godot_main_scene)"
echo "Using generated Godot client project: $CLIENT_PROJECT"
echo "Using generated Godot main scene: $GODOT_MAIN_SCENE"

echo "Restoring and building generated server solution"
dotnet restore "$SERVER_SOLUTION" --configfile "$CI_NUGET_CONFIG"
dotnet build "$SERVER_SOLUTION" -c Release --no-restore

echo "Restoring and building generated Godot client"
dotnet restore "$CLIENT_PROJECT" --configfile "$CI_NUGET_CONFIG"
dotnet build "$CLIENT_PROJECT" -c Debug --no-restore

echo "Starting generated server"
dotnet run --project "$SERVER_PROJECT" -c Release --no-build >"$SERVER_LOG" 2>&1 &
SERVER_PID=$!

if ! wait_for_server_ready; then
  print_logs
  exit 1
fi

echo "Running generated Godot client headless"
export LAKONA_GODOT_SMOKE=1
export LAKONA_GODOT_SMOKE_NAME="godot-${TRANSPORT:0:3}-${SERIALIZER:0:3}"
"$GODOT_BIN" \
  --headless \
  --path "$CLIENT_DIR" \
  --scene "$GODOT_MAIN_SCENE" \
  --log-file "$CLIENT_LOG" \
  --verbose \
  --no-header >"$GODOT_STDOUT_LOG" 2>&1 &
GODOT_PID=$!

for ((i = 0; i < 90; i++)); do
  if grep -Fq "Request failed:" "$GODOT_STDOUT_LOG" "$CLIENT_LOG" 2>/dev/null || \
     grep -Fq "Connect failed:" "$GODOT_STDOUT_LOG" "$CLIENT_LOG" 2>/dev/null; then
    echo "Godot client reported a network failure." >&2
    print_logs
    exit 1
  fi

  if grep -Fq "Arena smoke ok:" "$GODOT_STDOUT_LOG" "$CLIENT_LOG" 2>/dev/null; then
    echo "Lakona Tool Godot $TRANSPORT + $SERIALIZER verification passed."
    exit 0
  fi

  if ! kill -0 "$GODOT_PID" 2>/dev/null; then
    if wait "$GODOT_PID"; then
      godot_exit_code=0
    else
      godot_exit_code=$?
    fi
    echo "Godot exited before producing a successful arena smoke log. Exit code: $godot_exit_code" >&2
    print_logs
    exit 1
  fi

  sleep 1
done

echo "Timed out waiting for successful arena smoke login from generated Godot client." >&2
print_logs
exit 1
