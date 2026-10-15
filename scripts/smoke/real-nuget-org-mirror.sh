#!/usr/bin/env bash
# Real nuget.org read-through mirror acceptance. Requires Linux runner with egress.
set -Eeuo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
TMP="$(mktemp -d)"
PORT=50871
SERVER_PID=""
PROJ="$TMP/client/Smoke.csproj"
STORAGE="$TMP/storage"
LOG="$TMP/bagetter.log"
ORIGIN="https://api.nuget.org/v3/index.json"

cleanup() {
  if [[ -n "$SERVER_PID" ]]; then
    kill "$SERVER_PID" 2>/dev/null || true
    wait "$SERVER_PID" 2>/dev/null || true
  fi
  if [[ "$1" != "0" ]]; then
    echo "----- BaGetter log (last 100 lines) -----" >&2
    tail -n 100 "$LOG" >&2 || true
  fi
  rm -rf "$TMP"
}
trap 'cleanup $?' EXIT

command -v dotnet >/dev/null
command -v curl >/dev/null
command -v python3 >/dev/null
mkdir -p "$TMP/client" "$TMP/storage" "$TMP/cli"
cat > "$PROJ" <<'XML'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup>
    <!-- Includes transitive dependencies; fixed version on nuget.org -->
    <PackageReference Include="Microsoft.Extensions.Logging" Version="9.0.10" />
  </ItemGroup>
</Project>
XML
cat > "$TMP/NuGet.Config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="BaGetterOnly" value="http://127.0.0.1:$PORT/v3/index.json" protocolVersion="3" allowInsecureConnections="true"/>
  </packageSources>
</configuration>
EOF
export NUGET_PACKAGES="$TMP/client-packages"
export NUGET_HTTP_CACHE_PATH="$TMP/http-cache"
export DOTNET_CLI_HOME="$TMP/cli"
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

start_server() {
  local source="$1"
  : > "$LOG"
  (
    cd "$ROOT"
    export ASPNETCORE_URLS="http://127.0.0.1:$PORT"
    export ASPNETCORE_ENVIRONMENT=Production
    export Database__Type=Sqlite
    export Database__ConnectionString="Data Source=$TMP/bagetter.db"
    export Storage__Type=FileSystem
    export Storage__Path="$STORAGE"
    export Search__Type=Database
    export Mirror__Enabled=true
    export Mirror__PackageSource="$source"
    export Mirror__PackageDownloadTimeoutSeconds=30
    export ApiKey=smoke-test-key
    exec dotnet run --no-launch-profile --project src/BaGetter --configuration Release --no-build --no-restore
  ) >"$LOG" 2>&1 &
  SERVER_PID=$!
  local up=0
  for i in $(seq 1 75); do
    if curl --noproxy '*' --silent --fail "http://127.0.0.1:$PORT/v3/index.json" >/dev/null; then
      up=1
      break
    fi
    if ! kill -0 "$SERVER_PID" 2>/dev/null; then break; fi
    sleep 1
  done
  if [[ "$up" != "1" ]]; then
    echo "BaGetter did not start with upstream $source" >&2
    return 1
  fi
}

stop_server() {
  if [[ -n "$SERVER_PID" ]]; then
    kill "$SERVER_PID" 2>/dev/null || true
    wait "$SERVER_PID" 2>/dev/null || true
    SERVER_PID=""
  fi
}
restore_from_mirror_only() {
  rm -rf "$NUGET_PACKAGES" "$NUGET_HTTP_CACHE_PATH" "$TMP/client/obj"
  dotnet restore "$PROJ" --configfile "$TMP/NuGet.Config" \
    --packages "$NUGET_PACKAGES" --no-cache --force -v minimal
  test -f "$NUGET_PACKAGES/microsoft.extensions.logging/9.0.10/microsoft.extensions.logging.9.0.10.nupkg"
}
echo "== Cold: actual nuget.org through BaGetter as the only feed =="
start_server "$ORIGIN"
restore_from_mirror_only
PKG="$STORAGE/packages/microsoft.extensions.logging/9.0.10/microsoft.extensions.logging.9.0.10.nupkg"
test -s "$PKG"
NPKGS="$(find "$STORAGE/packages" -name '*.nupkg' -type f | wc -l)"
if (( NPKGS < 2 )); then
  echo "Expected direct and transitive dependencies in server cache; found $NPKGS" >&2
  exit 1
fi
echo "Cached nupkg count: $NPKGS"

# Discover the nuget.org flat-container resource rather than hard-coding its endpoint.
FLAT_BASE="$(curl --fail --silent --show-error "$ORIGIN" | python3 -c 'import json,sys; j=json.load(sys.stdin); print(next(r["@id"].rstrip("/") for r in j["resources"] if r["@type"]=="PackageBaseAddress/3.0.0"))')"
curl --fail --silent --show-error --retry 3 --retry-delay 2 \
  "$FLAT_BASE/microsoft.extensions.logging/9.0.10/microsoft.extensions.logging.9.0.10.nupkg" \
  -o "$TMP/upstream.nupkg"
cmp "$TMP/upstream.nupkg" "$PKG"
echo "Byte-identical cached package."

echo "== 32 concurrent cold downloads of an uncached package =="
COLD_URL="http://127.0.0.1:$PORT/v3/package/newtonsoft.json/13.0.3/newtonsoft.json.13.0.3.nupkg"
for i in $(seq 1 32); do
  curl --noproxy '*' --fail --silent --show-error --max-time 120 \
    "$COLD_URL" -o "$TMP/parallel-$i.nupkg" &
done
wait
for i in $(seq 1 32); do
  test -s "$TMP/parallel-$i.nupkg"
  cmp "$TMP/parallel-1.nupkg" "$TMP/parallel-$i.nupkg"
done
test -s "$STORAGE/packages/newtonsoft.json/13.0.3/newtonsoft.json.13.0.3.nupkg"
echo "All 32 concurrent cold requests received identical bytes."

echo "== Warm server cache / empty client cache =="
restore_from_mirror_only

echo "== Offline: restart with an intentionally unavailable upstream =="
stop_server
start_server "http://127.0.0.1:1/v3/index.json"
restore_from_mirror_only
cmp "$TMP/upstream.nupkg" "$PKG"
curl --noproxy '*' --fail --silent --show-error \
  "$COLD_URL" -o "$TMP/offline-newtonsoft.nupkg"
cmp "$TMP/offline-newtonsoft.nupkg" "$TMP/parallel-1.nupkg"
echo "PASS: cold/warm/offline restores, byte identity, and 32 concurrent cold requests."
