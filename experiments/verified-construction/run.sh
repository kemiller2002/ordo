#!/usr/bin/env bash
# EXPERIMENTAL (VC-002): build, construct, test, and check that construction
# is reproducible. Writes src/Invoice.Slice/generated/ and out/ deterministically.
#
# Requires: .NET SDK 10 (dotnet on PATH, or /root/.dotnet), python3 with
# jsonschema for the trace schema test. Uses no services or containers; the
# NuGet restore needs the xunit/test-sdk packages in the local cache or a feed.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$here"

if ! command -v dotnet >/dev/null 2>&1 && [ -x /root/.dotnet/dotnet ]; then
  export PATH=/root/.dotnet:$PATH DOTNET_ROOT=/root/.dotnet
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

handoff=fixture/invoice-slice/handoff.json
pinned=ec5d0e6a4343b1774d40760c04af200a9b71147940305d1ddbabbc5c6db68bcd

echo "== fixture pin"
actual="$(sha256sum "$handoff" | cut -d' ' -f1)"
if [ "$actual" != "$pinned" ]; then
  echo "handoff.json sha256 $actual does not match fixture-v1 $pinned" >&2
  exit 1
fi

echo "== build construct (reuses Ordo.Core and Ordo.Construction unchanged)"
dotnet build src/Echelon.Construct/Echelon.Construct.fsproj -c Release --nologo -v quiet

construct() { dotnet src/Echelon.Construct/bin/Release/net10.0/construct.dll "$handoff" "$1"; }

echo "== construct (handoff -> generated F# + out/construction-trace.json)"
construct . > /dev/null

echo "== reproducibility: two fresh constructions must match each other and the committed output"
scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT
construct "$scratch/a" > "$scratch/a.sums"
construct "$scratch/b" > "$scratch/b.sums"
diff -u "$scratch/a.sums" "$scratch/b.sums"
for f in $(cd "$scratch/a" && find . -type f | sort); do
  cmp -s "$scratch/a/$f" "$scratch/b/$f" || { echo "construct is not deterministic for $f" >&2; exit 1; }
  cmp -s "$scratch/a/$f" "$f" || { echo "construct output $f differs from the working tree" >&2; exit 1; }
done
cat "$scratch/a.sums"

echo "== build the constructed slice"
dotnet build src/Invoice.Slice.PaymentsService/Invoice.Slice.PaymentsService.fsproj -c Release --nologo -v quiet

echo "== tests"
dotnet test tests/VerifiedConstruction.Tests/VerifiedConstruction.Tests.fsproj -c Release --nologo

echo "== done: out/construction-trace.json sha256:$(sha256sum out/construction-trace.json | cut -d' ' -f1)"
