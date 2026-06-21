#!/usr/bin/env bash
set -euo pipefail

repo_root="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
config_helper="$repo_root/scripts/dev-config.mjs"
backend_project="$repo_root/src/backend/FortressSouls.Api/FortressSouls.Api.csproj"
frontend_dir="$repo_root/src/frontend"
backend_pid=""

cleanup() {
  if [[ -n "$backend_pid" ]] && kill -0 "$backend_pid" 2>/dev/null; then
    kill "$backend_pid" 2>/dev/null || true
    wait "$backend_pid" 2>/dev/null || true
  fi
}

trap cleanup EXIT INT TERM

if [[ ! -d "$frontend_dir/node_modules" ]]; then
  echo "==> frontend install"
  (
    cd "$frontend_dir"
    npm install
  )
fi

while IFS= read -r line; do
  [[ -z "$line" ]] && continue
  key="${line%%=*}"
  value="${line#*=}"
  export "$key=$value"
done < <(node "$config_helper" env "$repo_root")

echo "==> config"
node "$config_helper" summary "$repo_root"

echo "==> backend"
dotnet run --no-launch-profile --project "$backend_project" --urls "$FORTRESS_SOULS_BACKEND_BASE_URL" &
backend_pid=$!

echo "==> frontend"
(
  cd "$frontend_dir"
  npm run dev -- --host 127.0.0.1 --port "$FORTRESS_SOULS_FRONTEND_PORT" --strictPort
)
