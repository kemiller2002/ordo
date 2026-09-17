# EX-SDE-2026-0002 run data

`run-manifest.json` holds every run's start and close telemetry markers, its
`worker_epoch` readings, its independent verification against its committed
diff, and the operational log of decisions taken while the experiment was in
flight — including the ones that cut against the convenient answer.

`complexity/` holds a post-hoc measurement that is **not part of this
experiment's analysis**; see the README there and `EV-SDE-2026-0008`.

This directory sits under `research/runs/` rather than beside the experiment
record because ROS scans `research/experiments/` as artifacts requiring front
matter, and run data is data, not an artifact.
