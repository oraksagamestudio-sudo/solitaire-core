#!/usr/bin/env bash
set -euo pipefail

# This script patches old test calls:
#   FreeCellState.NewGame(seed)
# into:
#   FreeCellState.NewGame(seed, new FreeCellConfig())
#
# It is idempotent (safe to run multiple times).

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")"/.. && pwd)"

files=(
  "$ROOT_DIR/tests/Solitaire.Core.Tests/FreeCellTests.cs"
  "$ROOT_DIR/tests/Solitaire.Core.Tests/FreeCellSequenceTests.cs"
)

for f in "${files[@]}"; do
  if [[ -f "$f" ]]; then
    echo "[patch] Updating $f"
    # 1) Add FreeCellConfig() as second argument when only one argument is present.
    perl -0777 -i -pe 's/FreeCellState\.NewGame\(\s*([^,\)\n]+?)\s*\)/FreeCellState.NewGame($1, new FreeCellConfig())/g' "$f"

    # 2) Ensure namespace import exists
    if ! grep -q 'using\s\+Solitaire\.FreeCell;' "$f"; then
      # Prepend the using at the very top
      tmp="$(mktemp)"
      printf '%s

' 'using Solitaire.FreeCell;' > "$tmp"
      cat "$f" >> "$tmp"
      mv "$tmp" "$f"
    fi
  else
    echo "[warn] Not found: $f (skipping)"
  fi
done

echo "[done] Tests patched."
