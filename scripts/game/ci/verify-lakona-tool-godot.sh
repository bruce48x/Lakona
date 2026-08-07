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
SERVER_LOG_PREFIX="$LOG_DIR/server"
CLIENT_LOG="$LOG_DIR/client.log"
GODOT_STDOUT_LOG="$LOG_DIR/godot.stdout.log"
CLUSTER_PEERS='[{"Id":"godot-gateway","Endpoint":"tcp://127.0.0.1:21001"},{"Id":"godot-world-a","Endpoint":"tcp://127.0.0.1:21002"},{"Id":"godot-world-b","Endpoint":"tcp://127.0.0.1:21003"}]'

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
  terminate_process "${GATEWAY_PID:-}" "gateway server"
  terminate_process "${WORLD_A_PID:-}" "world-a server"
  terminate_process "${WORLD_B_PID:-}" "world-b server"
}

trap cleanup EXIT

print_logs() {
  for log in "$SERVER_LOG_PREFIX".*.log "$CLIENT_LOG" "$GODOT_STDOUT_LOG"; do
    if [[ -f "$log" ]]; then
      echo "===== $log =====" >&2
      cat "$log" >&2
    fi
  done
}

wait_for_server_ready() {
  local management_port="$1"
  local server_pid="$2"
  local server_name="$3"
  local readiness_url="http://127.0.0.1:${management_port}/_lakona/health/ready"

  for ((i = 0; i < 60; i++)); do
    if curl --fail --silent --show-error --output /dev/null "$readiness_url" 2>/dev/null; then
      return 0
    fi

    if ! kill -0 "$server_pid" 2>/dev/null; then
      echo "$server_name process exited before application readiness." >&2
      return 1
    fi

    sleep 1
  done

  echo "Timed out waiting for application readiness at $readiness_url." >&2
  return 1
}

start_cluster_node() {
  local node_id="$1"
  local actor_hosts="$2"
  local client_port="$3"
  local management_port="$4"
  local cluster_port="$5"
  local log_file="$SERVER_LOG_PREFIX.$node_id.log"

  echo "Starting $node_id (client=$client_port, management=$management_port, cluster=$cluster_port)" >&2
  env \
    LAKONA__Node__Id="$node_id" \
    LAKONA__ActorHosts="$actor_hosts" \
    LAKONA__Cluster__Endpoint="tcp://127.0.0.1:$cluster_port" \
    LAKONA__Cluster__Peers="$CLUSTER_PEERS" \
    LAKONA__Endpoints__0__Host="127.0.0.1" \
    LAKONA__Endpoints__0__Port="$client_port" \
    LAKONA__Management__Http__Host="127.0.0.1" \
    LAKONA__Management__Http__Port="$management_port" \
    LAKONA__Health__ClusterDiagnosticsEnabled=true \
    dotnet run --project "$SERVER_PROJECT" -c Release --no-build >"$log_file" 2>&1 &
  echo $!
}

cluster_is_ready() {
  local cluster_id=""

  for management_port in 20080 20081 20082; do
    local cluster_json
    cluster_json="$(curl --fail --silent --show-error "http://127.0.0.1:${management_port}/_lakona/health/cluster")" || return 1

    local observed_cluster
    observed_cluster="$(sed -n 's/.*"cluster":"\([^"]*\)".*/\1/p' <<<"$cluster_json")"
    if [[ -z "$observed_cluster" ]]; then
      echo "Cluster diagnostics from management port $management_port did not include a cluster id: $cluster_json" >&2
      return 1
    fi

    if [[ -z "$cluster_id" ]]; then
      cluster_id="$observed_cluster"
    elif [[ "$cluster_id" != "$observed_cluster" ]]; then
      echo "Nodes formed different clusters: $cluster_id and $observed_cluster." >&2
      return 1
    fi

    local ready_members
    ready_members="$(grep -o '"state":"ready"' <<<"$cluster_json" | wc -l | tr -d ' ')"
    if [[ "$ready_members" != 3 ]]; then
      return 1
    fi
  done

  echo "Three-node cluster is Ready (cluster=$cluster_id)."
}

wait_for_three_node_cluster() {
  for ((i = 0; i < 90; i++)); do
    if cluster_is_ready; then
      return 0
    fi

    for server_pid in "$GATEWAY_PID" "$WORLD_A_PID" "$WORLD_B_PID"; do
      if ! kill -0 "$server_pid" 2>/dev/null; then
        echo "A cluster node exited before the three-node membership became Ready." >&2
        return 1
      fi
    done

    sleep 1
  done

  echo "Timed out waiting for three Ready nodes in one cluster." >&2
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

echo "Starting generated three-node server cluster"
GATEWAY_PID="$(start_cluster_node "godot-gateway" '[]' 20000 20080 21001)"
WORLD_A_PID="$(start_cluster_node "godot-world-a" '["gameWorld"]' 20001 20081 21002)"
WORLD_B_PID="$(start_cluster_node "godot-world-b" '["gameWorld"]' 20002 20082 21003)"

if ! wait_for_server_ready 20080 "$GATEWAY_PID" "Gateway server" || \
   ! wait_for_server_ready 20081 "$WORLD_A_PID" "World-a server" || \
   ! wait_for_server_ready 20082 "$WORLD_B_PID" "World-b server" || \
   ! wait_for_three_node_cluster; then
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
