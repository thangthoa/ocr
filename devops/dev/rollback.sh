#!/usr/bin/env bash
# Restarts the app on the image deployed before the current one.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/config.sh"

require_docker
PREVIOUS="$(cat "$STATE_DIR/previous_tag" 2>/dev/null || true)"
[[ -n "$PREVIOUS" ]] || die "no previous deployment recorded"
docker image inspect "$IMAGE_NAME:$PREVIOUS" >/dev/null 2>&1 || die "image $IMAGE_NAME:$PREVIOUS no longer exists"

CURRENT="$(current_tag)"
log "Rolling back from ${CURRENT:-unknown} to $PREVIOUS"
compose "$PREVIOUS" up -d
echo "$PREVIOUS" > "$STATE_DIR/current_tag"
[[ -n "$CURRENT" ]] && echo "$CURRENT" > "$STATE_DIR/previous_tag"

"$SCRIPT_DIR/status.sh"
