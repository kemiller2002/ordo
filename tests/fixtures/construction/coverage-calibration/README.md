# Coverage-report calibration fixtures

Verbatim copies of the frozen ECIR inputs of the slices CONSTRUCT-008 measured.
The coverage report's route calibration (`ConstructionCoverage.measuredCalibration`)
cites these slices, and `tests/Ordo.Tests/ConstructionCoverageTests.fs`
re-derives every structure ratio it quotes from these files. Do not edit them.
A changed input is a new calibration and needs a new calibration id.

Source: `kemiller2002/research-documents@f6778c260c2bc144544510a44eb81a88480ac542`,
`research/construction-by-design/`.

| Directory | Source paths | Cohort | Authorized decision |
|---|---|---|---|
| `summa/` | `CONSTRUCT-008/validation/summa-delivery-01/skeleton/ecir/summa-delivery-01/{source-manifest,blueprint}.json`, `.../decisions/construction-decisions.json` | `COH-SUMMA-DELIVERY` | `DEC-SUMMA-ATTEMPT-STATES` |
| `chrona/` | `CONSTRUCT-008/validation/chrona-timer-01/skeleton/ecir/chrona-timer/{source-manifest,blueprint}.json`, `.../decisions/construction-decisions.json` | `COH-CHRONA-TIMER` | `DEC-TIMER-LIFECYCLE` |
| `expanded/` | `time-entry-expanded-01/harness/ecir/{source-manifest,blueprint,construction-decisions}.json` | `COH-TIME-ENTRY-WEEK` | `DEC-ENTRY-LIFECYCLE` |

The fourth slice, time-entry timer pilot 01, is `../time-entry-timer/`.

The authorizations are test inputs that stand in for the operator approvals
recorded with each slice (for example `time-entry-expanded-01/harness/ecir/APPROVAL.md`).
They are not approvals.
