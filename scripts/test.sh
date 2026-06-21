#!/usr/bin/env bash
set -euo pipefail

repo_root="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
backend_solution="$repo_root/src/backend/FortressSouls.slnx"
frontend_dir="$repo_root/src/frontend"

run_step() {
  echo "==> $1"
  shift
  "$@"
}

run_step "backend build" dotnet build "$backend_solution"
run_step "backend test" dotnet test "$backend_solution" --no-build
run_step "local dev config test" node --test "$repo_root/scripts/dev-config.test.mjs"
(
  cd "$frontend_dir"
  run_step "frontend typecheck" npm run typecheck
  run_step "frontend test" npm test -- --run
  run_step "frontend build" npm run build
  run_step "frontend e2e install" npm run test:e2e:install
  run_step "frontend e2e smoke" npm run test:e2e
)
