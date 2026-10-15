#!/usr/bin/env bash
# PostgreSQL + filesystem production-host acceptance with real migrations.
# Requires a running PostgreSQL service and a prebuilt Release application.
set -Eeuo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
FIXTURE="$ROOT/tests/BaGetter.Tests/TestData/TestData.1.2.3.nupkg"
TMP="$(mktemp -d)"
PORT=50883
BASE="http://127.0.0.1:$PORT"
PID=""
API_KEY="postgres-smoke-only"
PG_CONNECTION="${PG_CONNECTION:-Host=127.0.0.1;Port=5432;Database=bagetter_ci;Username=bagetter;Password=postgres_ci_test}"

cleanup() {
  local code=$?
  if [[ -n "$PID" ]]; then kill "$PID" 2>/dev/null || true; wait "$PID" 2>/dev/null || true; fi
  if (( code != 0 )); then
    echo "PostgreSQL acceptance failed; server log:" >&2
    tail -n 100 "$TMP/server.log" >&2 || true
  fi
  rm -rf "$TMP"
}
trap cleanup EXIT
test -s "$FIXTURE"
mkdir -p "$TMP/storage"

start() {
  (
    cd "$ROOT"
    export ASPNETCORE_ENVIRONMENT=Production
    export ASPNETCORE_URLS="$BASE"
    export Database__Type=PostgreSql
    export Database__ConnectionString="$PG_CONNECTION"
    export Storage__Type=FileSystem
    export Storage__Path="$TMP/storage"
    export Search__Type=Database
    export Mirror__Enabled=false
    export ApiKey="$API_KEY"
    exec dotnet run --no-launch-profile --project src/BaGetter \
      --configuration Release --no-build --no-restore
  ) >"$TMP/server.log" 2>&1 &
  PID=$!

  for _ in $(seq 1 90); do
    if curl --noproxy '*' --fail --silent "$BASE/v3/index.json" >/dev/null 2>&1 \
        && curl --noproxy '*' --fail --silent "$BASE/health" >/dev/null 2>&1; then
      return 0
    fi
    if ! kill -0 "$PID" 2>/dev/null; then break; fi
    sleep 1
  done
  echo "PostgreSQL server was not ready" >&2
  return 1
}

stop() {
  kill "$PID" 2>/dev/null || true
  wait "$PID" 2>/dev/null || true
  PID=""
}

start
# A real package upload exercises the PostgreSQL schema and citext columns.
code="$(curl --noproxy '*' --silent --show-error -o "$TMP/upload.txt" -w '%{http_code}' \
  --request PUT --header "X-NuGet-ApiKey: $API_KEY" \
  --header "Content-Type: application/octet-stream" \
  --data-binary "@$FIXTURE" "$BASE/api/v2/package")"
if [[ "$code" != 201 ]]; then
  echo "Expected 201 from authenticated PostgreSQL-backed upload, got $code" >&2
  cat "$TMP/upload.txt" >&2 || true
  exit 1
fi

PACKAGE_PATH="v3/package/testdata/1.2.3/testdata.1.2.3.nupkg"
curl --noproxy '*' --fail --silent --show-error "$BASE/$PACKAGE_PATH" -o "$TMP/download.nupkg"
cmp "$FIXTURE" "$TMP/download.nupkg"

# Verify citext case-insensitive ID lookup on a real PostgreSQL instance.
curl --noproxy '*' --fail --silent --show-error \
  "$BASE/v3/package/TESTDATA/index.json" -o "$TMP/versions.json"
python3 - "$TMP/versions.json" <<'PY'
import json, sys
with open(sys.argv[1], encoding="utf-8") as handle:
    data = json.load(handle)
assert "1.2.3" in data["versions"], data
PY

stop
start
curl --noproxy '*' --fail --silent --show-error "$BASE/$PACKAGE_PATH" -o "$TMP/restarted.nupkg"
cmp "$FIXTURE" "$TMP/restarted.nupkg"

# DB migrations must be idempotent across process restarts.
echo "PASS: PostgreSQL migrations, authenticated publish, case-insensitive lookup, byte identity and restart"
