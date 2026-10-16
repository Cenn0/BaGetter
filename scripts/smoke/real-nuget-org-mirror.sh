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
    # Record mirror fetches so the concurrency test can assert single-flight.
    export Logging__Console__LogLevel__BaGetter=Information
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
# Resolve the database-published immutable revision, not a stale legacy path.
# The revision in SQLite is the visibility boundary for package content.
committed_nupkg() {
  local package_id="$1" package_version="$2"
  python3 - "$TMP/bagetter.db" "$STORAGE" "$package_id" "$package_version" <<'PY'
import pathlib
import re
import sqlite3
import sys

database, storage, package_id, package_version = sys.argv[1:]
with sqlite3.connect(database, timeout=10) as conn:
    row = conn.execute(
        "SELECT StorageRevision FROM Packages WHERE Id = ? COLLATE NOCASE "
        "AND NormalizedVersionString = ? COLLATE NOCASE",
        (package_id, package_version),
    ).fetchone()

if row is None:
    raise SystemExit(f"No committed package row: {package_id} {package_version}")
revision = row[0]
if not revision or not re.fullmatch(r"[0-9a-f]{32}", revision):
    raise SystemExit(f"Missing/invalid storage revision: {package_id} {package_version}")
package_id, package_version = package_id.lower(), package_version.lower()
path = (pathlib.Path(storage) / "packages" / package_id / package_version /
        "revisions" / revision / f"{package_id}.{package_version}.nupkg")
if not path.is_file() or path.stat().st_size == 0:
    raise SystemExit(f"Published revision has no package bytes: {path}")
print(path)
PY
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
PKG="$(committed_nupkg microsoft.extensions.logging 9.0.10)"
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
pids=()
for i in $(seq 1 32); do
  curl --noproxy '*' --fail --silent --show-error --max-time 120 \
    "$COLD_URL" -o "$TMP/parallel-$i.nupkg" &
  pids+=("$!")
done
# Bare 'wait' returns success even if a background curl failed. Inspect each.
for pid in "${pids[@]}"; do
  wait "$pid"
done
for i in $(seq 1 32); do
  test -s "$TMP/parallel-$i.nupkg"
  cmp "$TMP/parallel-1.nupkg" "$TMP/parallel-$i.nupkg"
done
NEWTONSOFT="$(committed_nupkg newtonsoft.json 13.0.3)"
test -s "$NEWTONSOFT"
cmp "$NEWTONSOFT" "$TMP/parallel-1.nupkg"
curl --fail --silent --show-error --retry 3 --retry-delay 2 \
  "$FLAT_BASE/newtonsoft.json/13.0.3/newtonsoft.json.13.0.3.nupkg" \
  -o "$TMP/upstream-newtonsoft.nupkg"
cmp "$TMP/upstream-newtonsoft.nupkg" "$NEWTONSOFT"
FETCH_COUNT="$(grep -ic 'Package newtonsoft.json 13.0.3 does not exist locally' "$LOG" || true)"
if [[ "$FETCH_COUNT" != "1" ]]; then
  echo "Expected exactly one cold origin fetch for Newtonsoft.Json, saw $FETCH_COUNT" >&2
  exit 1
fi
echo "All 32 concurrent cold requests returned identical upstream bytes with one cold fetch."

echo "== Warm server cache / empty client cache =="
COLD_FETCHES="$(grep -ic 'does not exist locally' "$LOG" || true)"
restore_from_mirror_only
WARM_FETCHES="$(grep -ic 'does not exist locally' "$LOG" || true)"
if [[ "$WARM_FETCHES" != "$COLD_FETCHES" ]]; then
  echo "Warm restore triggered additional package downloads ($COLD_FETCHES -> $WARM_FETCHES)" >&2
  exit 1
fi

echo "== GET / HEAD and genuine not-found response =="
curl --noproxy '*' --fail --silent --show-error --head "$COLD_URL" -o "$TMP/newtonsoft-head.txt"
grep -q '^HTTP/1.1 200' "$TMP/newtonsoft-head.txt"
MISSING_URL="http://127.0.0.1:$PORT/v3/package/bagetter.mirror-smoke.nonexistent.20261016/1.0.0/bagetter.mirror-smoke.nonexistent.20261016.1.0.0.nupkg"
MISSING_STATUS="$(curl --noproxy '*' --silent --show-error --max-time 60 -o /dev/null -w '%{http_code}' "$MISSING_URL")"
if [[ "$MISSING_STATUS" != "404" ]]; then
  echo "Expected a confirmed upstream package absence to be HTTP 404, got $MISSING_STATUS" >&2
  exit 1
fi

echo "== Repair missing mirrored file without republishing a stale revision =="
rm "$NEWTONSOFT"
curl --noproxy '*' --fail --silent --show-error --max-time 120 \
  "$COLD_URL" -o "$TMP/repaired-newtonsoft.nupkg"
REPAIRED_NEWTONSOFT="$(committed_nupkg newtonsoft.json 13.0.3)"
if [[ "$REPAIRED_NEWTONSOFT" == "$NEWTONSOFT" ]]; then
  echo "Repair did not publish a new immutable package revision" >&2
  exit 1
fi
cmp "$TMP/repaired-newtonsoft.nupkg" "$TMP/upstream-newtonsoft.nupkg"
cmp "$REPAIRED_NEWTONSOFT" "$TMP/upstream-newtonsoft.nupkg"

echo "== Offline: restart with an intentionally unavailable upstream =="
stop_server
start_server "http://127.0.0.1:1/v3/index.json"
restore_from_mirror_only
cmp "$TMP/upstream.nupkg" "$PKG"
curl --noproxy '*' --fail --silent --show-error \
  "$COLD_URL" -o "$TMP/offline-newtonsoft.nupkg"
cmp "$TMP/offline-newtonsoft.nupkg" "$TMP/parallel-1.nupkg"
UNAVAILABLE_STATUS="$(curl --noproxy '*' --silent --show-error --max-time 30 -o /dev/null -w '%{http_code}' "$MISSING_URL")"
if [[ "$UNAVAILABLE_STATUS" != "503" ]]; then
  echo "Unreachable upstream must yield HTTP 503 for an uncached package, got $UNAVAILABLE_STATUS" >&2
  exit 1
fi
echo "PASS: cold/warm/offline restores, published revision bytes, repair, HTTP 404/503 and 32 concurrent cold requests."
