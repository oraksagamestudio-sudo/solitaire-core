# All-Green Patch (tests signature fix)

This small patch makes the whole solution build **green** by updating the test project
to the new FreeCell factory method signature:

- from: `FreeCellState.NewGame(seed)`
- to:   `FreeCellState.NewGame(seed, new FreeCellConfig())`

## How to use

```bash
cd ~/dev/solitaire-core

# If your CLI showed CS1003 errors in Program.cs earlier,
# re-apply your known-good CLI file (already used successfully before):
#   unzip -o ~/Downloads/cli-foundation-pop-fix-esc.zip -d .

# 1) Unzip this patch
unzip -o ~/Downloads/all-green-tests-patch.zip -d .

# 2) Run the patcher
bash scripts/patch-tests.sh

# 3) Clean & build
dotnet clean
dotnet build ./Solitaire.sln -c Release
```

The script is **idempotent**—safe to run multiple times.
