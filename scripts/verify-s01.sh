#!/usr/bin/env bash
set -euo pipefail

echo "==> dotnet build"
dotnet build

echo "==> dotnet test"
dotnet test
