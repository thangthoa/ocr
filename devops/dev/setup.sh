#!/usr/bin/env bash
# One-time preparation on the server: checks Docker and creates devops/dev/.env from the template.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/config.sh"

require_docker
log "$(docker version --format 'Docker {{.Server.Version}}')"

if [[ -f "$ENV_FILE" ]]; then
    log "$ENV_FILE already exists; left unchanged"
else
    cp "$SCRIPT_DIR/.env.example" "$ENV_FILE"
    chmod 600 "$ENV_FILE"
    log "Created $ENV_FILE. Set OCR_API_KEY in it, then run ./devops/dev/deploy.sh"
fi
