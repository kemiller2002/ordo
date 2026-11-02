#!/usr/bin/env sh
# ORDO-OBS-001 acceptance: a real Ordo pattern recognition produces
# identifiable decision events; an execution and an independent verification
# are recorded only as separate, explicitly reported events; the summary
# reports what is unknown instead of inventing it.
#
# Input is the repository's time-entry-timer construction fixture, not a
# rehearsal. The verification receipt below is a hand-written Dokimos-shaped
# receipt that exercises the "executed but failed verification" path; it is
# labeled as such in docs/observability/acceptance/README.md.
#
# Usage: scripts/ordo-events-acceptance.sh [OUTPUT_DIR]
set -eu

root="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
out="${1:-$root/docs/observability/acceptance}"
fixture="$root/tests/fixtures/construction/time-entry-timer"
events="$out/decision-events.jsonl"

dotnet build "$root/src/Sde.Cli/Sde.Cli.fsproj" -c Release -v q --nologo >/dev/null
sde() { dotnet "$root/src/Sde.Cli/bin/Release/net8.0/sde.dll" "$@"; }

mkdir -p "$out"
rm -f "$events"
printf '["DEC-TIMER-STATES"]\n' > "$out/approvals.json"

export ORDO_RUN_ID="acceptance-ORDO-OBS-001"
export ORDO_SESSION_ID="acceptance"
export ORDO_WORK_ITEM="WI-0048"

# 1. Real recognition with approved decisions: matched, partial and escalated outcomes.
sde ecir recognize --manifest "$fixture/source-manifest.json" --blueprint "$fixture/blueprint.json" \
  --cohort COH-TIMER --decisions "$fixture/construction-decisions.json" --approvals "$out/approvals.json" \
  --events "$events" > "$out/recognition-with-decisions.txt"

# 2. Real recognition with no approved records: everything unmatched, fallback to agent.
sde ecir recognize --manifest "$fixture/source-manifest.json" --blueprint "$fixture/blueprint.json" \
  --cohort COH-TIMER --events "$events" > "$out/recognition-without-decisions.txt"

# 3. An executor reports executing the first pattern-selected decision.
decision="$(grep '"kind":"pattern.selection"' "$events" | grep '"disposition":"pattern-selected"' | head -n 1 \
  | sed 's/.*"decisionId":"\([^"]*\)".*/\1/')"
sde events record-execution --events "$events" --decision "$decision" --status succeeded \
  --duration-ms 1200 --executor acceptance-script

# 4. An independent verifier rejects it (Dokimos completion receipt shape).
cat > "$out/dokimos-receipt.json" <<'JSON'
{ "Contract": "dokimos.completion-verification", "Verdict": "rejected", "WorkItemId": "WI-0048" }
JSON
sde events record-verification --events "$events" --decision "$decision" --dokimos-receipt "$out/dokimos-receipt.json"

# 5. Summary.
sde events summarize --events "$events" --json > "$out/decision-summary.json"
sde events summarize --events "$events"
