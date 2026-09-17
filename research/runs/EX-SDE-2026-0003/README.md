# EX-SDE-2026-0003 run data

Empty until the experiment runs. `run-manifest.json` will hold each run's
per-requirement telemetry markers, its `worker_epoch` readings, the churn
measurement for each of the nine requirements, and the independent verification
of each requirement against the wave-1 acceptance suite.

**Before any run begins, two things must be true and recorded here:**

1. Condition B's baseline exists, is acceptance-equivalent to condition A's, and
   its commit SHA is written into the experiment record. If it cannot be made
   equivalent, the experiment does not run.
2. The nine-requirement sequence is unchanged from the document order frozen in
   the experiment record.

Churn is measured by `tools/Ordo.Churn`:

    dotnet run --project tools/Ordo.Churn -- <repo> <baseline> R1=<rev> R2=<rev> ...

**A run is void unless `unattributed_lines` is zero for every requirement.** The
tool exits non-zero and warns when it is not. See the experiment record's
attribution-completeness section for why.
