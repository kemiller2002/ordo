# State Directed Engineering

This repository is a greenfield pilot running Repository Operating System
1.2.1-main.16.1.

## Start here

1. Read [`AGENTS.md`](AGENTS.md) and [`BOOTSTRAP.md`](BOOTSTRAP.md).
2. Use [`context/ARCHITECTURE.md`](context/ARCHITECTURE.md) as this
   repository's semantic map; then follow the narrower canonical record for
   the area being changed.
3. Read [`PROJECT-CHARTER.md`](PROJECT-CHARTER.md) and establish the baseline
   in [`context/CURRENT-STATE.md`](context/CURRENT-STATE.md).
4. Select the first bounded mission and its observable acceptance criteria.
5. Record durable evidence, decisions, and handoffs as the work proceeds.

The current construction method is
[`method/CONSTRUCTION-METHOD-v0.2.md`](method/CONSTRUCTION-METHOD-v0.2.md).
Version 0.1 remains frozen as the prior method and historical trial target.

## Using SDE in your own repository

This repository is the canonical source of the methodology. To *apply* SDE to
a project, install the published package rather than cloning this repository:

```bash
npx @echelon-foundry/sde init      # bring the repository into a valid installed state
npx @echelon-foundry/sde status    # what is installed
npx @echelon-foundry/sde verify    # validate the installation
npx @echelon-foundry/sde upgrade   # move to a newer release
npx @echelon-foundry/sde doctor    # diagnose problems and how to fix them
```

That is the canonical public interface. The package installs a curated,
versioned subset of `doctrine/`, `method/` and `templates/sde/` into `.sde/`,
so an engineer or agent can apply SDE without reading the research that
produced it.

- [`distribution/README.md`](distribution/README.md) — the package overview
  (this is the npm package page)
- [`docs/installation.md`](docs/installation.md) — detailed installation behaviour
- [`docs/cli.md`](docs/cli.md) — full command reference, JSON schemas, exit codes
- [`docs/upgrading.md`](docs/upgrading.md) — migration behaviour and compatibility policy
- [`docs/development.md`](docs/development.md) — building and testing the tool
- [`docs/releasing.md`](docs/releasing.md) — the release and publish process

The tool's lifecycle logic is implemented in F# under `src/Sde.Core` and
`src/Sde.Cli`; Node exists only as the npm launcher.

## Local operating commands

```bash
./ros work begin TASK-001 --type task
./ros work context TASK-001
./ros status
./ros registry check
./ros registry build
./ros validate
```

`work context` reports legal actions and completion evidence. Validation errors include repair instructions; use `./ros validate --json` for machine-readable output. Complete work with explicit evidence paths as described in `docs/work-protocol.md`.

The installed snapshot is self-contained. It does not read from the source ROS
repository. `.ros/installation.json` records the package version and checksums
of installed files.

## Pilot rule

The operating system is itself under evaluation. Do not infer that
State Directed Engineering is a validated discipline, method, or product merely because
the repository follows a rigorous process. Measure whether the process improves
decisions, traceability, handoffs, and rework relative to the declared baseline.
