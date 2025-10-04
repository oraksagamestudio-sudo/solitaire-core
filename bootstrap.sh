#!/usr/bin/env bash
set -euo pipefail

# Create solution and add existing projects, then build & test.
dotnet new sln -n Solitaire --force

dotnet sln Solitaire.sln add   src/Solitaire.Core/Solitaire.Core.csproj   src/Solitaire.FreeCell/Solitaire.FreeCell.csproj   src/Solitaire.Klondike/Solitaire.Klondike.csproj   src/Solitaire.Cli/Solitaire.Cli.csproj   tests/Solitaire.Core.Tests/Solitaire.Core.Tests.csproj

dotnet restore ./Solitaire.sln
dotnet build ./Solitaire.sln -c Release
dotnet test  ./Solitaire.sln -c Release
