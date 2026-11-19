#!/usr/bin/env bash
# Shows which version runs here and whether it is healthy.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/config.sh"

PORT="$(app_port)"
log "Container"
docker ps -a --filter "name=^${CONTAINER_NAME}$" --format '{{.Names}}  {{.Image}}  {{.Status}}'

log "Health (waiting up to 60s)"
live=""
for _ in $(seq 1 30); do
    if curl -fsS -o /dev/null "http://localhost:$PORT/health/live" 2>/dev/null; then
        live=ok
        break
    fi
    sleep 2
done
[[ "$live" == ok ]] || die "app is not live on port $PORT; check ./devops/dev/logs.sh"
echo "live:  ok   http://localhost:$PORT"

# Readiness includes the external OCR service, so it can fail while the app itself is fine.
if curl -fsS -o /dev/null "http://localhost:$PORT/health/ready" 2>/dev/null; then
    echo "ready: ok"
else
    echo "ready: NOT READY (this server cannot reach the OCR service)"
fi

# The Word editor (OnlyOffice) takes a minute or two to start after a deploy.
DOCS="$(docs_port)"
if curl -fsS "http://localhost:$DOCS/healthcheck" 2>/dev/null | grep -q true; then
    echo "word:  ok   http://localhost:$DOCS"
else
    echo "word:  starting or down (http://localhost:$DOCS/healthcheck); check: docker logs ${CONTAINER_NAME}-docs"
fi
