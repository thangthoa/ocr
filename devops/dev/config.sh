# Shared settings for the dev deploy scripts. They run ON the server, from a checkout of this repo.
# Values can be overridden from the environment, e.g. `APP_PORT=9000 ./devops/dev/deploy.sh`.
# shellcheck shell=bash

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
IMAGE_NAME="${IMAGE_NAME:-oppocr-web}"
CONTAINER_NAME="${CONTAINER_NAME:-oppocr-dev}"
ENV_FILE="${ENV_FILE:-$SCRIPT_DIR/.env}"
STATE_DIR="$SCRIPT_DIR/.state"
# Images kept for rollback (current, previous, and older ones up to this count).
KEEP_IMAGES="${KEEP_IMAGES:-3}"

log() {
    printf '\033[1;34m==>\033[0m %s\n' "$*"
}

die() {
    printf '\033[1;31merror:\033[0m %s\n' "$*" >&2
    exit 1
}

# Docker Compose v2 is `docker compose`; older hosts only have `docker-compose`.
# Usage: compose <image-tag> <compose args...>
compose() {
    local tag="$1"
    shift
    local cmd=(docker compose)
    docker compose version >/dev/null 2>&1 || cmd=(docker-compose)
    IMAGE_TAG="$tag" IMAGE_NAME="$IMAGE_NAME" ENV_FILE="$ENV_FILE" CONTAINER_NAME="$CONTAINER_NAME" APP_PORT="$(app_port)" \
        "${cmd[@]}" -f "$SCRIPT_DIR/docker-compose.yml" --project-directory "$SCRIPT_DIR" -p "$CONTAINER_NAME" "$@"
}

# APP_PORT from the environment, else from the .env file, else 5286.
app_port() {
    if [[ -n "${APP_PORT:-}" ]]; then
        echo "$APP_PORT"
        return
    fi
    local port
    port="$(grep -E '^APP_PORT=' "$ENV_FILE" 2>/dev/null | tail -1 | cut -d= -f2 || true)"
    echo "${port:-5286}"
}

current_tag() {
    cat "$STATE_DIR/current_tag" 2>/dev/null || true
}

require_docker() {
    command -v docker >/dev/null || die "Docker is not installed"
    docker info >/dev/null 2>&1 || die "cannot talk to Docker (is the daemon running, is this user in the docker group?)"
    docker compose version >/dev/null 2>&1 || command -v docker-compose >/dev/null || die "docker compose is not installed"
}
