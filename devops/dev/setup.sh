#!/usr/bin/env bash
# One-time preparation on the server: checks Docker and creates devops/dev/.env from the template.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/config.sh"

require_docker
log "$(docker version --format 'Docker {{.Server.Version}}')"

if [[ -f "$ENV_FILE" ]]; then
    log "$ENV_FILE already exists; only missing settings are added"
else
    cp "$SCRIPT_DIR/.env.example" "$ENV_FILE"
    chmod 600 "$ENV_FILE"
    log "Created $ENV_FILE"
fi

# Settings added after the first setup (Word editor, ADR 0004).
add_setting_if_missing() {
    grep -Eq "^$1=" "$ENV_FILE" || { printf '%s=%s\n' "$1" "$2" >> "$ENV_FILE"; log "Added $1=$2 to $ENV_FILE"; }
}
add_setting_if_missing DOCS_EDITOR_PUBLIC_URL "http://localhost:8085"
add_setting_if_missing DOCS_PORT "8085"

if ! grep -Eq '^DOCS_EDITOR_JWT_SECRET=.{32,}' "$ENV_FILE"; then
    secret="$(head -c 32 /dev/urandom | od -An -tx1 | tr -d ' \n')"
    if grep -Eq '^DOCS_EDITOR_JWT_SECRET=' "$ENV_FILE"; then
        sed -i "s|^DOCS_EDITOR_JWT_SECRET=.*|DOCS_EDITOR_JWT_SECRET=$secret|" "$ENV_FILE"
    else
        printf 'DOCS_EDITOR_JWT_SECRET=%s\n' "$secret" >> "$ENV_FILE"
    fi
    log "Generated DOCS_EDITOR_JWT_SECRET in $ENV_FILE"
fi

grep -Eq '^OCR_API_KEY=.+' "$ENV_FILE" || log "Set OCR_API_KEY in $ENV_FILE"
log "Check DOCS_EDITOR_PUBLIC_URL in $ENV_FILE (the address browsers use for the Word editor), then run ./devops/dev/deploy.sh"
