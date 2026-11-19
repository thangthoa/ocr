#!/usr/bin/env bash
# Stops the app. Images and .env are kept, so deploy.sh or rollback.sh can start it again.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/config.sh"

require_docker
compose "$(current_tag)" down
log "Stopped"
