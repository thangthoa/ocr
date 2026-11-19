#!/usr/bin/env bash
# Builds the image from this checkout and (re)starts the app on this machine.
# Usage: ./devops/dev/deploy.sh [--pull]    --pull: `git pull --ff-only` first
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/config.sh"

require_docker
[[ -f "$ENV_FILE" ]] || die "$ENV_FILE is missing; run ./devops/dev/setup.sh first"
grep -Eq '^OCR_API_KEY=.+' "$ENV_FILE" || die "OCR_API_KEY is empty in $ENV_FILE"

cd "$REPO_ROOT"
if [[ "${1:-}" == "--pull" ]]; then
    log "Pulling latest code"
    git pull --ff-only
fi

TAG="$(git rev-parse --short HEAD 2>/dev/null || date +%Y%m%d%H%M%S)"
# Uncommitted changes get a distinct tag so the image never pretends to be a clean commit.
if [[ -n "$(git status --porcelain 2>/dev/null)" ]]; then
    TAG="$TAG-dirty-$(date +%Y%m%d%H%M%S)"
fi

# The Dockerfile restores, builds and publishes inside the SDK image: no .NET SDK needed here.
log "Building $IMAGE_NAME:$TAG"
docker build -f devops/docker/Dockerfile -t "$IMAGE_NAME:$TAG" .

mkdir -p "$STATE_DIR"
PREVIOUS="$(current_tag)"
log "Starting $IMAGE_NAME:$TAG"
compose "$TAG" up -d --remove-orphans
[[ -n "$PREVIOUS" && "$PREVIOUS" != "$TAG" ]] && echo "$PREVIOUS" > "$STATE_DIR/previous_tag"
echo "$TAG" > "$STATE_DIR/current_tag"

"$SCRIPT_DIR/status.sh"

# Keep the newest images for rollback; drop older ones so the disk does not fill up.
mapfile -t OLD < <(docker images "$IMAGE_NAME" --format '{{.Tag}}' | tail -n +"$((KEEP_IMAGES + 1))")
for old in "${OLD[@]}"; do
    [[ "$old" == "$TAG" || "$old" == "$(cat "$STATE_DIR/previous_tag" 2>/dev/null || true)" ]] && continue
    docker rmi "$IMAGE_NAME:$old" >/dev/null 2>&1 || true
done

log "Deployed $TAG on port $(app_port)"
