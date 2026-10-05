# ROS repository identity correction (2026-10-05)

## What changed

`ros.json` named this repository `state-directed-engineering` ("State
Directed Engineering"), which was its identity when ROS was installed on
2026-09-02. The repository is published as `kemiller2002/ordo`, and Ordo is
the methodology's public name (`DF-SDE-2026-0005`). Since GH-52 work on
2026-10-05, the identity reads:

| Field | Before | After |
|---|---|---|
| `ros.json` `name` | `state-directed-engineering` | `ordo` |
| `ros.json` `project` | `State Directed Engineering` | `Ordo` |
| `ros.json` `repository.id` | `state-directed-engineering` | `ordo` |

ROS stamps `repository.id` into every new work event and into
`.ros/context/current.json`. Events written from this date on therefore say
`"repository": "ordo"`.

## What did not change, on purpose

- **`.ros/events/events.jsonl` history is not rewritten.** Events up to and
  including the `work.started` event for GH-52 carry
  `"repository": "state-directed-engineering"`. They are immutable,
  idempotently identified records (`docs/work-protocol.md`), and changing
  them would change their `eventId`s. Consumers that aggregate by repository
  slug (Praxis telemetry, portfolio queries) should treat
  `state-directed-engineering` as an **alias of `ordo`** for events dated
  before 2026-10-05.
- **`.ros/installation.json`** keeps `project_slug:
  state-directed-engineering`. It records what was installed on
  2026-09-02, not the current identity.
- **Research records** that use "State-Directed Engineering" or SDE as the
  methodology's descriptive name are correct as written (`DF-SDE-2026-0005`
  keeps SDE as the descriptive term).

## Follow-up

The same defect class exists in Dokimos, where a parallel correction is
being made. A doctor-style check that compares `ros.json` `repository.id`
with the remote slug, with a declared alias list for intentional renames,
would stop this drift recurring. That check belongs to ROS/Praxis, not to
this repository.
