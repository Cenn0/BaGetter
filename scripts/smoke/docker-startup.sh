#!/usr/bin/env bash
# Container startup and volume-persistence smoke for this fork.
set -Eeuo pipefail

IMAGE="${1:-bagetter:smoke}"
NAME="bagetter-ci-smoke-$$"
VOLUME="bagetter-ci-smoke-$$"
PORT=50882
ENDPOINT="http://127.0.0.1:$PORT"

cleanup() {
  local status=$?
  if (( status != 0 )); then
    echo "Docker smoke failed; container logs:" >&2
    docker logs "$NAME" >&2 || true
  fi
  docker rm -f "$NAME" >/dev/null 2>&1 || true
  docker volume rm -f "$VOLUME" >/dev/null 2>&1 || true
}
trap cleanup EXIT

docker volume create "$VOLUME" >/dev/null

start_container() {
  docker run --detach --name "$NAME" \
    --publish "127.0.0.1:$PORT:8080" \
    --mount "type=volume,src=$VOLUME,dst=/data" \
    --env ASPNETCORE_URLS="http://0.0.0.0:8080" \
    "$IMAGE" >/dev/null

  for _ in $(seq 1 90); do
    if curl --noproxy '*' -fsS "$ENDPOINT/v3/index.json" >/dev/null 2>&1 \
        && curl --noproxy '*' -fsS "$ENDPOINT/health" >/dev/null 2>&1; then
      return 0
    fi
    if [[ "$(docker inspect -f '{{.State.Running}}' "$NAME")" != "true" ]]; then
      break
    fi
    sleep 1
  done
  echo "Container did not become ready" >&2
  return 1
}

start_container

curl --noproxy '*' -fsS "$ENDPOINT/v3/index.json" |
  python3 -c 'import json,sys; j=json.load(sys.stdin); assert any("PackageBaseAddress/3.0.0" in str(r.get("@type","")) for r in j["resources"])'

# Confirm that the running host actually uses its external persistent volume.
docker exec "$NAME" sh -c 'test -s /data/db/bagetter.db && printf "%s" persisted > /data/smoke-marker'
docker rm -f "$NAME" >/dev/null
start_container
docker exec "$NAME" sh -c 'test -s /data/db/bagetter.db && test "$(cat /data/smoke-marker)" = persisted'

echo "PASS: Docker startup, service index, health endpoint, and volume persistence"
