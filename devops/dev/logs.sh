#!/usr/bin/env bash
# Follows the app logs. Usage: ./devops/dev/logs.sh [lines]
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/config.sh"

docker logs --tail "${1:-200}" -f "$CONTAINER_NAME"
