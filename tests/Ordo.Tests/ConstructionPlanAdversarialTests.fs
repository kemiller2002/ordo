/// Adversarial tests for the typed construction-plan vocabulary (GH-64,
/// CONSTRUCT-001). A self-contained door fixture is prepared through ECIR;
/// every test then tries to get a plan past `ConstructionPlan.validate` or
/// `ConstructionPlanWire.readPlan` that must be refused.
module Ordo.Tests.ConstructionPlanAdversarialTests

open System
open System.Reflection
open System.Security.Cryptography
open System.Text
open System.Text.RegularExpressions
open Xunit
open Ordo.Core
open Ordo.Core.Json
open Ordo.Core.ConstructionIr
open Ordo.Core.ConstructionPlan

// ---------------------------------------------------------------------------
// Fixture: sources, blueprint, prepared cohort.
// ---------------------------------------------------------------------------

let private sha256Of (text: string) =
    "sha256:" + (text |> Encoding.UTF8.GetBytes |> SHA256.HashData |> Convert.ToHexString).ToLowerInvariant()

let private source key originalId line text =
    { Key = key
      OriginalId = originalId
      Document = "requirements/door.md"
      Location = sprintf "line %d" line
      Revision = "r1"
      ContentDigest = sha256Of text }

/// Door opens/closes (decision-backed), door locks, prerequisite audit clock, deferred alarm.
let private srcA = source "door/R-001" "R-001" 10 "The door opens and closes on command."
let private srcL = source "door/R-002" "R-002" 20 "A closed door can be locked and unlocked with a key."
let private srcP = source "door/R-003" "R-003" 30 "Every door event is stamped by the audit clock."
let private srcD = source "door/R-004" "R-004" 40 "An alarm sounds when the door is forced."
let private srcU = source "door/R-005" "R-005" 50 "The door must be both always open and always locked."

let private manifestOf sources = { Digest = Ordo.Core.ConstructionIrWire.manifestDigest sources; Requirements = sources }
let private manifest = manifestOf [ srcA; srcL; srcP; srcD ]

let private rA = srcA.Key
let private rL = srcL.Key
let private rP = srcP.Key
let private rD = srcD.Key

let private node id kind keys deps : ConstructionNode =
    { Id = id; Kind = kind; RequirementKeys = keys; DependsOn = deps; Justification = None }

let private row src disposition nodes : RequirementRecord = { Source = src; Disposition = disposition; NodeIds = nodes }

let private cohortId = "COHORT-DOOR"

let private blueprintWith (cohortDeps: string list) : Blueprint =
    { SchemaVersion = ConstructionIr.SchemaVersion
      SourceManifestDigest = manifest.Digest
      Requirements =
        [ row srcA Modeled [ "DEC-A"; "CONTRACT-A"; "VERIFY-A"; cohortId ]
          row srcL Modeled [ "CONTRACT-L"; "VERIFY-L"; cohortId ]
          row srcP Modeled [ "CONTRACT-P"; "VERIFY-P"; "COHORT-P" ]
          row srcD (Deferred "alarm hardware is not chosen") [ "DEF-D" ] ]
      Nodes =
        [ node "DEC-A" Decision [ rA ] []
          node "CONTRACT-A" Contract [ rA ] []
          node "VERIFY-A" VerificationObligation [ rA ] []
          node "CONTRACT-L" Contract [ rL ] []
          node "VERIFY-L" VerificationObligation [ rL ] []
          node cohortId Cohort [ rA; rL ] cohortDeps
          node "CONTRACT-P" Contract [ rP ] []
          node "VERIFY-P" VerificationObligation [ rP ] []
          node "COHORT-P" Cohort [ rP ] []
          node "DEF-D" Deferral [ rD ] [] ] }

/// The door cohort depends on the audit-clock contract: P is required scope but not assigned.
let private blueprint = blueprintWith [ "CONTRACT-P" ]
let private blueprintDigest = Ordo.Core.ConstructionIrWire.blueprintDigest blueprint
let private approvals = Set.ofList [ "DEC-A" ]

let private prepared =
    match prepareCohort manifest blueprint cohortId approvals with
    | Ok cohort -> cohort
    | Error errors -> failwithf "fixture cohort is not ready: %A" errors

// ---------------------------------------------------------------------------
// Fixture: a valid plan for the door workflow.
// ---------------------------------------------------------------------------

let private trace keys nodes : TraceDraft = { RequirementKeys = keys; NodeIds = nodes }
let private tA = trace [ rA ] [ "CONTRACT-A" ]
let private tL = trace [ rL ] [ "CONTRACT-L" ]

let private state name role t = DefineState { StateDraft.Name = name; Role = role; Trace = t }

let private keyPresent = { GuardDraft.Name = "key present"; Predicate = ExtensionPredicate }

let private transition command from target guards t =
    DefineTransition { TransitionDraft.Command = command; From = from; To = target; Guards = guards; Trace = t }

let private workflowOp name = DefineWorkflow { WorkflowDraft.Name = name; Trace = trace [ rA; rL ] [ "CONTRACT-A"; "CONTRACT-L" ] }

let private invariantOp name holdsIn =
    DefineInvariant { InvariantDraft.Name = name; Predicate = ExtensionPredicate; HoldsIn = holdsIn; Trace = tL }

let private portOp kind t = DeclarePort { PortDraft.Kind = kind; Trace = t }

let private capabilityOp command capability =
    // A capability requirement must cite an externally authorized decision.
    BindCapability { CapabilityDraft.Command = command; Capability = capability; Trace = trace [ rA; rL ] [ "CONTRACT-L"; "DEC-A" ] }

let private unresolvedOp subject question =
    DeclareUnresolved { UnresolvedDraft.Subject = subject; Question = question; Trace = trace [ rL ] [ "VERIFY-L" ] }

let private operations =
    [ workflowOp "door"
      state "closed" Initial tA
      state "open" Ordinary tA
      state "locked" Ordinary tL
      state "decommissioned" Terminal tA
      transition "open" "closed" "open" [] (trace [ rA ] [ "CONTRACT-A"; "DEC-A" ])
      transition "close" "open" "closed" [] tA
      transition "lock" "closed" "locked" [ keyPresent ] tL
      transition "unlock" "locked" "closed" [ keyPresent ] tL
      transition "decommission" "closed" "decommissioned" [] tA
      invariantOp "locked implies closed" [ "locked" ]
      portOp CurrentTime tA
      portOp (TransitionEffect "lock") tL
      capabilityOp "unlock" "door.unlock"
      unresolvedOp "no timer" "How long may a door stay open before it relocks?" ]

let private plan: PlanDraft =
    { SchemaVersion = PlanSchemaVersion
      VocabularyVersion = VocabularyVersion
      Binding =
        { SourceManifestDigest = manifest.Digest
          BlueprintDigest = blueprintDigest
          CohortId = cohortId
          DecisionIds = [ "DEC-A" ] }
      Operations = operations }

// ---------------------------------------------------------------------------
// Helpers.
// ---------------------------------------------------------------------------

let private withOps ops = { plan with Operations = ops }
let private withBinding f = { plan with Binding = f plan.Binding }

/// Replace the operation with the given derived label.
let private replaceOp label replacement =
    withOps (operations |> List.map (fun op -> if operationLabel op = label then replacement else op))

let private dropOp label = withOps (operations |> List.filter (fun op -> operationLabel op <> label))
let private addOps extra = withOps (operations @ extra)

let private violations draft =
    match ConstructionPlan.validate prepared draft with
    | Ok _ -> []
    | Error found -> found

let private refusedWith (expected: PlanViolation) draft =
    let found = violations draft
    Assert.True(List.contains expected found, sprintf "expected %A; got %A" expected found)

let private validDigest draft =
    match ConstructionPlan.validate prepared draft with
    | Ok validated -> ConstructionPlan.digest validated
    | Error found -> failwithf "expected a valid plan; got %A" found

let private canonical draft =
    match ConstructionPlan.validate prepared draft with
    | Ok validated -> canonicalJson validated
    | Error found -> failwithf "expected a valid plan; got %A" found

/// A semantic change must either be refused or produce a different digest.
let private changesIdentity draft =
    match ConstructionPlan.validate prepared draft with
    | Error _ -> ()
    | Ok validated -> Assert.NotEqual<string>(validDigest plan, ConstructionPlan.digest validated)

// ---------------------------------------------------------------------------
// Baseline.
// ---------------------------------------------------------------------------

[<Fact>]
let ``the fixture manifest and blueprint are internally consistent`` () =
    Assert.Equal(Ok(), Ordo.Core.ConstructionIrWire.verifyManifestDigest manifest)
    Assert.Empty(ConstructionIr.validate manifest blueprint)
    let view = preparedCohortView prepared
    Assert.Equal<Set<string>>(Set.ofList [ rA; rL ], view.AssignedRequirementKeys)
    Assert.Equal<Set<string>>(Set.ofList [ rA; rL; rP ], view.RequiredRequirementKeys)

[<Fact>]
let ``the valid door plan validates`` () =
    match ConstructionPlan.validate prepared plan with
    | Error found -> failwithf "expected Ok; got %A" found
    | Ok validated ->
        let v = ConstructionPlan.view validated
        Assert.Equal("Door", v.Workflow.Name.Key)
        Assert.Equal("Closed", (List.head v.Workflow.States).Name.Key)
        Assert.Equal<string list>([ "Close"; "Decommission"; "Lock"; "Open"; "Unlock" ], v.Workflow.Commands |> List.map _.Key)

// ---------------------------------------------------------------------------
// 1. Binding: the plan must name exactly the cohort it was prepared against.
// ---------------------------------------------------------------------------

[<Fact>]
let ``a stale source manifest digest is refused`` () =
    let other = manifestOf [ srcA; { srcL with ContentDigest = sha256Of "edited" }; srcP; srcD ]
    refusedWith (StaleSourceManifest(other.Digest, manifest.Digest))
        (withBinding (fun b -> { b with SourceManifestDigest = other.Digest }))

[<Fact>]
let ``a plan bound to a different blueprint digest is refused`` () =
    let different =
        { blueprint with
            Nodes = blueprint.Nodes |> List.map (fun n -> if n.Id = "VERIFY-L" then { n with Justification = Some "edited" } else n) }
    let otherDigest = Ordo.Core.ConstructionIrWire.blueprintDigest different
    Assert.NotEqual<string>(blueprintDigest, otherDigest)
    refusedWith (StaleBlueprint(otherDigest, blueprintDigest)) (withBinding (fun b -> { b with BlueprintDigest = otherDigest }))

[<Fact>]
let ``a plan bound to another cohort is refused`` () =
    refusedWith (CohortMismatch("COHORT-P", cohortId)) (withBinding (fun b -> { b with CohortId = "COHORT-P" }))

[<Fact>]
let ``a required decision that is not bound is refused`` () =
    refusedWith (UnboundDecision "DEC-A") (withBinding (fun b -> { b with DecisionIds = [] }))

[<Fact>]
let ``a decision bound but not required is unauthorized`` () =
    refusedWith (DecisionOutOfScope "DEC-ROGUE") (withBinding (fun b -> { b with DecisionIds = [ "DEC-A"; "DEC-ROGUE" ] }))

[<Fact>]
let ``unsupported schema and vocabulary versions are refused`` () =
    refusedWith (UnsupportedPlanSchema "ordo.construction-plan/2") { plan with SchemaVersion = "ordo.construction-plan/2" }
    refusedWith (UnsupportedVocabulary "ordo.construction-vocabulary/0") { plan with VocabularyVersion = "ordo.construction-vocabulary/0" }

// ---------------------------------------------------------------------------
// 2. Trace: both directions are checked against the prepared scope.
// ---------------------------------------------------------------------------

[<Fact>]
let ``an operation without a requirement trace is refused`` () =
    refusedWith (MissingSourceTrace "state:Open") (replaceOp "state:Open" (state "open" Ordinary (trace [] [ "CONTRACT-A" ])))

[<Fact>]
let ``an operation without a node trace is refused`` () =
    refusedWith (MissingNodeTrace "state:Open") (replaceOp "state:Open" (state "open" Ordinary (trace [ rA ] [])))

[<Fact>]
let ``a requirement outside the manifest is not assigned`` () =
    refusedWith (UnassignedRequirement("state:Open", "door/R-999"))
        (replaceOp "state:Open" (state "open" Ordinary (trace [ rA; "door/R-999" ] [ "CONTRACT-A" ])))

[<Fact>]
let ``a prerequisite requirement is required scope but not implementation scope`` () =
    Assert.Contains(rP, (preparedCohortView prepared).RequiredRequirementKeys)
    refusedWith (UnassignedRequirement("state:Open", rP))
        (replaceOp "state:Open" (state "open" Ordinary (trace [ rA; rP ] [ "CONTRACT-A"; "CONTRACT-P" ])))

[<Fact>]
let ``a deferred requirement cannot be traced`` () =
    refusedWith (UnassignedRequirement("state:Open", rD))
        (replaceOp "state:Open" (state "open" Ordinary (trace [ rA; rD ] [ "CONTRACT-A" ])))

[<Fact>]
let ``a node outside required scope is refused`` () =
    let outside label nodeId =
        refusedWith (UnauthorizedNodeReference(label, nodeId))
            (replaceOp label (state "open" Ordinary (trace [ rA ] [ "CONTRACT-A"; nodeId ])))
    outside "state:Open" "VERIFY-P"
    outside "state:Open" "COHORT-P"
    outside "state:Open" "DEF-D"
    outside "state:Open" "NO-SUCH-NODE"

[<Fact>]
let ``a node anchored to no traced requirement is an inconsistent trace`` () =
    refusedWith (InconsistentTrace("state:Open", "CONTRACT-L"))
        (replaceOp "state:Open" (state "open" Ordinary (trace [ rA ] [ "CONTRACT-A"; "CONTRACT-L" ])))
    refusedWith (InconsistentTrace("state:Open", "CONTRACT-P"))
        (replaceOp "state:Open" (state "open" Ordinary (trace [ rA ] [ "CONTRACT-P" ])))

[<Fact>]
let ``an assigned requirement covered by no operation is refused`` () =
    // The workflow trace claims only A here; claiming L would count as coverage.
    let onlyA =
        [ DefineWorkflow { WorkflowDraft.Name = "door"; Trace = trace [ rA ] [ cohortId ] }
          state "closed" Initial tA
          state "open" Ordinary tA
          transition "open" "closed" "open" [] tA
          transition "close" "open" "closed" [] tA ]
    refusedWith (UncoveredRequirement rL) (withOps onlyA)

// ---------------------------------------------------------------------------
// 3. Structure.
// ---------------------------------------------------------------------------

[<Fact>]
let ``a plan without a workflow is refused`` () = refusedWith MissingWorkflow (dropOp "workflow:Door")

[<Fact>]
let ``a plan with two workflows is refused`` () = refusedWith MultipleWorkflows (addOps [ workflowOp "gate" ])

[<Fact>]
let ``a state defined twice is refused`` () = refusedWith (DuplicateDefinition "state:Open") (addOps [ state "open" Ordinary tA ])

[<Fact>]
let ``normalized-name duplicates are the same state`` () =
    let spellings = [ "no timer"; "NoTimer"; "no-timer"; "no_timer"; "noTimer" ]
    for spelling in spellings |> List.tail do
        let draft =
            addOps
                [ state "no timer" Ordinary tA
                  state spelling Ordinary tA
                  transition "start" "closed" "no timer" [] tA
                  transition "stop" "no timer" "closed" [] tA ]
        refusedWith (DuplicateDefinition "state:NoTimer") draft

[<Fact>]
let ``a workflow without an initial state is refused`` () =
    refusedWith MissingInitialState (replaceOp "state:Closed" (state "closed" Ordinary tA))

[<Fact>]
let ``a workflow with two initial states is refused`` () =
    refusedWith (MultipleInitialStates [ "Closed"; "Open" ]) (replaceOp "state:Open" (state "open" Initial tA))

[<Fact>]
let ``a transition to or from an unknown state is refused`` () =
    refusedWith (UnknownState("transition:Closed/Jam", "Jammed")) (addOps [ transition "jam" "closed" "jammed" [] tA ])
    refusedWith (UnknownState("transition:Ajar/Shut", "Ajar")) (addOps [ transition "shut" "ajar" "closed" [] tA ])

[<Fact>]
let ``an invariant over an unknown state is refused`` () =
    refusedWith (UnknownState("invariant:LockedImpliesClosed", "Jammed"))
        (replaceOp "invariant:LockedImpliesClosed" (invariantOp "locked implies closed" [ "locked"; "jammed" ]))

[<Fact>]
let ``the same command from one state with different targets conflicts`` () =
    refusedWith (ConflictingTransition("Closed", "Open", [ "Locked"; "Open" ]))
        (addOps [ transition "Open" "Closed" "locked" [] tA ])

[<Fact>]
let ``a transition defined twice is a duplicate, even when respelled`` () =
    refusedWith (DuplicateDefinition "transition:Closed/Open") (addOps [ transition "Open" "Closed" "Open" [] tA ])

[<Fact>]
let ``a transition out of a terminal state is refused`` () =
    refusedWith (TransitionFromTerminal "transition:Decommissioned/Revive")
        (addOps [ transition "revive" "decommissioned" "closed" [] tA ])

[<Fact>]
let ``an unreachable state is refused`` () =
    refusedWith (UnreachableState "Orphan")
        (addOps [ state "orphan" Ordinary tA; transition "adopt" "orphan" "closed" [] tA ])

[<Fact>]
let ``a non-terminal state with no way out is refused`` () =
    refusedWith (DeadEndState "Stuck") (addOps [ state "stuck" Ordinary tA; transition "jam" "open" "stuck" [] tA ])

[<Fact>]
let ``a port or capability naming an unknown command is refused`` () =
    refusedWith (UnknownCommand("port:transitionEffect/Explode", "Explode")) (addOps [ portOp (TransitionEffect "explode") tL ])
    refusedWith (UnknownCommand("capability:OpenSesame/door.magic", "OpenSesame")) (addOps [ capabilityOp "open sesame" "door.magic" ])

[<Fact>]
let ``a respelled port is a duplicate`` () =
    refusedWith (DuplicateDefinition "port:transitionEffect/Lock") (addOps [ portOp (TransitionEffect "Lock") tL ])
    refusedWith (DuplicateDefinition "port:currentTime") (addOps [ portOp CurrentTime tA ])

[<Fact>]
let ``an invariant over no state is refused`` () =
    refusedWith (EmptyInvariantScope "invariant:LockedImpliesClosed")
        (replaceOp "invariant:LockedImpliesClosed" (invariantOp "locked implies closed" []))

[<Fact>]
let ``an unresolved obligation with a blank question is refused`` () =
    for blank in [ ""; "   "; "\t\n" ] do
        refusedWith (BlankUnresolvedQuestion "unresolved:NoTimer") (replaceOp "unresolved:NoTimer" (unresolvedOp "no timer" blank))

[<Fact>]
let ``an invalid capability id is refused`` () =
    for raw in [ ""; " door.unlock"; "door.unlock "; "door\nunlock"; String('c', 300) ] do
        refusedWith (InvalidCapability(sprintf "capability:Unlock/%s" raw, raw))
            (replaceOp "capability:Unlock/door.unlock" (capabilityOp "unlock" raw))

// ---------------------------------------------------------------------------
// 4. Names cannot smuggle code, paths or members.
// ---------------------------------------------------------------------------

let private smuggled: (string * NameError) list =
    [ "x\"; System.IO.File.Delete(\"a\")", NameHasIllegalCharacter '"'
      "../../etc/passwd", NameMustStartWithLetter
      "a.b", NameHasIllegalCharacter '.'
      "src/Program.fs", NameHasIllegalCharacter '/'
      "let x = 1", NameHasIllegalCharacter '='
      "x``y", NameHasIllegalCharacter '`'
      "a\nb", NameHasIllegalCharacter '\n'
      "café", NameHasIllegalCharacter 'é'
      "Ωmega", NameMustStartWithLetter
      "ａbc", NameMustStartWithLetter
      "1door", NameMustStartWithLetter
      "-door", NameMustStartWithLetter
      " door", NameMustStartWithLetter
      "_door", NameMustStartWithLetter
      "door--frame", NameHasEmptyWord
      "door  frame", NameHasEmptyWord
      "door-", NameHasEmptyWord
      String('a', 65), NameTooLong 65
      "", NameBlank ]

[<Fact>]
let ``semantic names refuse code, paths, unicode and malformed words`` () =
    for raw, reason in smuggled do
        Assert.Equal(Error reason, SemanticName.create raw)

[<Fact>]
let ``a 64 character name is accepted`` () =
    Assert.True(Result.isOk (SemanticName.create (String('a', 64))))

[<Fact>]
let ``smuggled names are refused as guard names`` () =
    for raw, reason in smuggled do
        let bad = transition "open" "closed" "open" [ { GuardDraft.Name = raw; Predicate = ExtensionPredicate } ] (trace [ rA ] [ "CONTRACT-A"; "DEC-A" ])
        refusedWith (InvalidName("transition:Closed/Open", raw, reason)) (replaceOp "transition:Closed/Open" bad)

[<Fact>]
let ``smuggled names are refused in every name position`` () =
    let raw = "src/Program.fs"
    let reason = NameHasIllegalCharacter '/'
    refusedWith (InvalidName("workflow:" + raw, raw, reason)) (replaceOp "workflow:Door" (workflowOp raw))
    refusedWith (InvalidName("state:" + raw, raw, reason)) (addOps [ state raw Ordinary tA ])
    refusedWith (InvalidName("transition:Closed/" + raw, raw, reason)) (addOps [ transition raw "closed" "open" [] tA ])
    refusedWith (InvalidName("transition:Closed/Open", raw, reason)) (replaceOp "transition:Closed/Open" (transition "open" "closed" raw [] tA))
    refusedWith (InvalidName("invariant:LockedImpliesClosed", raw, reason))
        (replaceOp "invariant:LockedImpliesClosed" (invariantOp "locked implies closed" [ raw ]))
    refusedWith (InvalidName("port:transitionEffect/" + raw, raw, reason)) (addOps [ portOp (TransitionEffect raw) tL ])
    refusedWith (InvalidName(sprintf "capability:%s/door.x" raw, raw, reason)) (addOps [ capabilityOp raw "door.x" ])
    refusedWith (InvalidName("unresolved:" + raw, raw, reason)) (addOps [ unresolvedOp raw "why?" ])

// ---------------------------------------------------------------------------
// 5. Wire: the reader refuses anything the vocabulary does not name.
// ---------------------------------------------------------------------------

let private planJson = toJson plan

let private mapMember name f json =
    match json with
    | JObject members -> JObject(members |> List.map (fun (k, v) -> if k = name then k, f v else k, v))
    | other -> other

let private addMember name value json =
    match json with
    | JObject members -> JObject(members @ [ name, value ])
    | other -> other

let private mapItem index f json =
    match json with
    | JArray values -> JArray(values |> List.mapi (fun i v -> if i = index then f v else v))
    | other -> other

let private appendItem value json =
    match json with
    | JArray values -> JArray(values @ [ value ])
    | other -> other

let private mapOperation index f = mapMember "operations" (mapItem index f)

let private jTrace = JObject [ "requirementKeys", JArray [ JString rA ]; "nodeIds", JArray [ JString "CONTRACT-A" ] ]

let private refusedOnWire (fragment: string) json =
    match ConstructionPlanWire.readPlan (render json) with
    | Ok draft -> failwithf "expected refusal containing %s; read %A" fragment draft
    | Error message -> Assert.Contains(fragment, message)

[<Fact>]
let ``the wire round trips a valid draft`` () =
    Assert.Equal(Ok plan, ConstructionPlanWire.readPlan (ConstructionPlanWire.encodePlan plan))

[<Fact>]
let ``prohibited operation kinds are unsupported operations`` () =
    let prohibited =
        [ "createFile"; "writeFile"; "deleteFile"; "runCommand"; "exec"; "addDependency"; "addPackage"; "editProject"
          "setArchitecture"; "modifyPolicy"; "grantAuthority"; "rawCode"; "fsharpExpression" ]
    for kind in prohibited do
        let op = JObject [ "op", JString kind; "path", JString "src/Program.fs"; "code", JString "rm -rf /"; "trace", jTrace ]
        refusedOnWire ("unsupported operation: " + kind) (mapMember "operations" (appendItem op) planJson)

[<Fact>]
let ``operation kinds are case sensitive`` () =
    let op = JObject [ "op", JString "DefineWorkflow"; "name", JString "x"; "trace", jTrace ]
    refusedOnWire "unsupported operation: DefineWorkflow" (mapMember "operations" (appendItem op) planJson)

[<Fact>]
let ``unknown predicate kinds are refused for guards and invariants`` () =
    let guardIndex = plan.Operations |> List.findIndex (fun o -> operationLabel o = "transition:Closed/Lock")
    let invariantIndex = plan.Operations |> List.findIndex (fun o -> operationLabel o = "invariant:LockedImpliesClosed")
    for kind in [ "expression"; "fsharp"; "code" ] do
        let guardBad = mapOperation guardIndex (mapMember "guards" (mapItem 0 (mapMember "predicate" (fun _ -> JString kind)))) planJson
        refusedOnWire ("unsupported predicate: " + kind) guardBad
        let invariantBad = mapOperation invariantIndex (mapMember "predicate" (fun _ -> JString kind)) planJson
        refusedOnWire ("unsupported predicate: " + kind) invariantBad

[<Fact>]
let ``unknown port kinds are refused`` () =
    for kind in [ "fileSystem"; "network"; "process"; "shell" ] do
        let op = JObject [ "op", JString "declarePort"; "port", JString kind; "trace", jTrace ]
        refusedOnWire ("unsupported port: " + kind) (mapMember "operations" (appendItem op) planJson)
        let withCommand = JObject [ "op", JString "declarePort"; "port", JString kind; "command", JString "open"; "trace", jTrace ]
        refusedOnWire ("unsupported port: " + kind) (mapMember "operations" (appendItem withCommand) planJson)

[<Fact>]
let ``port shapes must match their kind`` () =
    let currentTimeWithCommand = JObject [ "op", JString "declarePort"; "port", JString "currentTime"; "command", JString "open"; "trace", jTrace ]
    refusedOnWire "incorrect fields for port currentTime" (mapMember "operations" (appendItem currentTimeWithCommand) planJson)
    let effectWithoutCommand = JObject [ "op", JString "declarePort"; "port", JString "transitionEffect"; "trace", jTrace ]
    refusedOnWire "incorrect fields for port transitionEffect" (mapMember "operations" (appendItem effectWithoutCommand) planJson)

[<Fact>]
let ``extra members are refused everywhere`` () =
    let lockIndex = plan.Operations |> List.findIndex (fun o -> operationLabel o = "transition:Closed/Lock")
    let extras = [ "path", JString "src/Program.fs"; "code", JString "File.Delete()"; "validated", JBool true; "approved", JBool true ]
    for name, value in extras do
        let unknown = "unknown JSON member " + name
        refusedOnWire unknown (addMember name value planJson)
        refusedOnWire unknown (mapMember "binding" (addMember name value) planJson)
        for index in 0 .. List.length plan.Operations - 1 do
            refusedOnWire unknown (mapOperation index (addMember name value) planJson)
            refusedOnWire unknown (mapOperation index (mapMember "trace" (addMember name value)) planJson)
        refusedOnWire unknown (mapOperation lockIndex (mapMember "guards" (mapItem 0 (addMember name value))) planJson)

[<Fact>]
let ``duplicate JSON keys are refused`` () =
    let dupTop = addMember "schemaVersion" (JString PlanSchemaVersion) planJson
    refusedOnWire "duplicate JSON member schemaVersion" dupTop
    let dupBinding = mapMember "binding" (addMember "cohortId" (JString "COHORT-P")) planJson
    refusedOnWire "duplicate JSON member cohortId" dupBinding
    let dupName = mapOperation 0 (addMember "name" (JString "gate")) planJson
    refusedOnWire "duplicate JSON member name" dupName
    let dupOp = mapOperation 0 (addMember "op" (JString "createFile")) planJson
    refusedOnWire "exactly one string member op" dupOp
    let dupTrace = mapOperation 0 (mapMember "trace" (addMember "nodeIds" (JArray [ JString "VERIFY-P" ]))) planJson
    refusedOnWire "duplicate JSON member nodeIds" dupTrace

[<Fact>]
let ``wire schema and vocabulary versions are refused before semantics`` () =
    refusedOnWire "unsupported ordo.construction-plan/2" (mapMember "schemaVersion" (fun _ -> JString "ordo.construction-plan/2") planJson)
    refusedOnWire "unsupported x" (mapMember "vocabularyVersion" (fun _ -> JString "x") planJson)

[<Fact>]
let ``unknown state roles are refused`` () =
    let index = plan.Operations |> List.findIndex (fun o -> operationLabel o = "state:Open")
    refusedOnWire "unknown state role: approved" (mapOperation index (mapMember "role" (fun _ -> JString "approved")) planJson)

// ---------------------------------------------------------------------------
// 6. No fabricated validation.
// ---------------------------------------------------------------------------

[<Fact>]
let ``a fabricated validated marker is refused by the reader`` () =
    refusedOnWire "unknown JSON member validated" (addMember "validated" (JBool true) planJson)
    refusedOnWire "unknown JSON member digest" (addMember "digest" (JString (validDigest plan)) planJson)

/// True when the type is, or generically contains, `target`.
let rec private mentions (target: Type) (t: Type) =
    t = target || (t.IsGenericType && t.GetGenericArguments() |> Array.exists (mentions target))

/// Every public method in the target's assembly that can return (a value containing) the target.
let private publicProducers (target: Type) =
    target.Assembly.GetExportedTypes()
    |> Array.collect (fun t ->
        t.GetMethods(BindingFlags.Public ||| BindingFlags.Static ||| BindingFlags.Instance ||| BindingFlags.DeclaredOnly)
        |> Array.filter (fun m -> mentions target m.ReturnType)
        |> Array.map (fun m -> string t.FullName + "." + m.Name))
    |> Array.sort

[<Fact>]
let ``validate is the only public way to obtain a ValidatedPlan`` () =
    Assert.Empty(typeof<ValidatedPlan>.GetConstructors())
    Assert.Equal<string[]>([| "Ordo.Core.ConstructionPlan.validate" |], publicProducers typeof<ValidatedPlan>)
    // And validate needs a PreparedCohort, which only prepareCohort produces.
    Assert.Equal<string[]>([| "Ordo.Core.ConstructionIr.prepareCohort" |], publicProducers typeof<PreparedCohort>)

// ---------------------------------------------------------------------------
// 7. Determinism and identity.
// ---------------------------------------------------------------------------

let private mapNames (f: string -> string) (draft: PlanDraft) =
    let mapOp op =
        match op with
        | DefineWorkflow w -> DefineWorkflow { w with Name = f w.Name }
        | DefineState s -> DefineState { s with Name = f s.Name }
        | DefineTransition t ->
            DefineTransition
                { t with Command = f t.Command; From = f t.From; To = f t.To; Guards = t.Guards |> List.map (fun g -> { g with Name = f g.Name }) }
        | DefineInvariant i -> DefineInvariant { i with Name = f i.Name; HoldsIn = List.map f i.HoldsIn }
        | DeclarePort { Kind = TransitionEffect c; Trace = t } -> DeclarePort { Kind = TransitionEffect(f c); Trace = t }
        | DeclarePort p -> DeclarePort p
        | BindCapability c -> BindCapability { c with Command = f c.Command }
        | DeclareUnresolved u -> DeclareUnresolved { u with Subject = f u.Subject }
    { draft with Operations = List.map mapOp draft.Operations }

let private mapTraces (f: TraceDraft -> TraceDraft) (draft: PlanDraft) =
    let mapOp op =
        match op with
        | DefineWorkflow w -> DefineWorkflow { w with Trace = f w.Trace }
        | DefineState s -> DefineState { s with Trace = f s.Trace }
        | DefineTransition t -> DefineTransition { t with Trace = f t.Trace }
        | DefineInvariant i -> DefineInvariant { i with Trace = f i.Trace }
        | DeclarePort p -> DeclarePort { p with Trace = f p.Trace }
        | BindCapability c -> BindCapability { c with Trace = f c.Trace }
        | DeclareUnresolved u -> DeclareUnresolved { u with Trace = f u.Trace }
    { draft with Operations = List.map mapOp draft.Operations }

let private assertSameIdentity variant =
    Assert.Equal(validDigest plan, validDigest variant)
    Assert.Equal(canonical plan, canonical variant)

[<Fact>]
let ``operation order does not change identity`` () =
    assertSameIdentity (withOps (List.rev operations))
    assertSameIdentity (withOps (operations |> List.sortBy operationLabel))

[<Fact>]
let ``trace and decision order and duplication do not change identity`` () =
    assertSameIdentity (plan |> mapTraces (fun t -> ({ RequirementKeys = List.rev t.RequirementKeys; NodeIds = List.rev t.NodeIds }: TraceDraft)))
    assertSameIdentity (plan |> mapTraces (fun t -> ({ RequirementKeys = t.RequirementKeys @ t.RequirementKeys; NodeIds = t.NodeIds @ t.NodeIds }: TraceDraft)))
    assertSameIdentity (withBinding (fun b -> { b with DecisionIds = [ "DEC-A"; "DEC-A" ] }))

[<Fact>]
let ``decision id order does not change identity`` () =
    // Needs two required decisions: add a second decision on the lock requirement.
    let bp =
        { blueprint with
            Requirements = blueprint.Requirements |> List.map (fun r -> if r.Source.Key = rL then { r with NodeIds = "DEC-L" :: r.NodeIds } else r)
            Nodes = node "DEC-L" Decision [ rL ] [] :: blueprint.Nodes }
    let cohort =
        match prepareCohort manifest bp cohortId (Set.ofList [ "DEC-A"; "DEC-L" ]) with
        | Ok c -> c
        | Error e -> failwithf "%A" e
    let draft order = { plan with Binding = { plan.Binding with BlueprintDigest = Ordo.Core.ConstructionIrWire.blueprintDigest bp; DecisionIds = order } }
    let digestOf order =
        match ConstructionPlan.validate cohort (draft order) with
        | Ok v -> ConstructionPlan.digest v
        | Error e -> failwithf "%A" e
    Assert.Equal(digestOf [ "DEC-A"; "DEC-L" ], digestOf [ "DEC-L"; "DEC-A" ])
    match ConstructionPlan.validate cohort (draft [ "DEC-A" ]) with
    | Ok _ -> failwith "a missing decision must be refused"
    | Error e -> Assert.Contains(UnboundDecision "DEC-L", e)

[<Fact>]
let ``respelled names do not change identity`` () =
    let pascal (raw: string) =
        raw.Split([| ' '; '-'; '_' |]) |> Array.map (fun w -> string (Char.ToUpperInvariant w[0]) + w.Substring 1) |> String.concat ""
    assertSameIdentity (plan |> mapNames pascal)
    assertSameIdentity (plan |> mapNames (fun raw -> raw.Replace(' ', '-')))
    assertSameIdentity (plan |> mapNames (fun raw -> raw.Replace(' ', '_')))
    assertSameIdentity (plan |> mapNames (fun raw -> if raw = "no timer" then "NoTimer" else raw))
    assertSameIdentity (plan |> mapNames (fun raw -> if raw = "no timer" then "no-timer" else raw))

[<Fact>]
let ``semantic changes change identity or are refused`` () =
    // Target state.
    changesIdentity (replaceOp "transition:Locked/Unlock" (transition "unlock" "locked" "open" [ keyPresent ] tL))
    // Roles.
    changesIdentity (replaceOp "state:Decommissioned" (state "decommissioned" Ordinary tA))
    changesIdentity (replaceOp "state:Open" (state "open" Terminal tA))
    // Guards.
    changesIdentity (replaceOp "transition:Closed/Lock" (transition "lock" "closed" "locked" [] tL))
    changesIdentity
        (replaceOp "transition:Closed/Lock"
            (transition "lock" "closed" "locked" [ { GuardDraft.Name = "key turned"; Predicate = ExtensionPredicate } ] tL))
    // Trace keys and nodes.
    changesIdentity (replaceOp "state:Open" (state "open" Ordinary (trace [ rA ] [ "CONTRACT-A"; "VERIFY-A" ])))
    changesIdentity (replaceOp "state:Open" (state "open" Ordinary (trace [ rA; rL ] [ "CONTRACT-A" ])))
    // Unresolved question text.
    changesIdentity (replaceOp "unresolved:NoTimer" (unresolvedOp "no timer" "Something else?"))
    // Capability id.
    changesIdentity (replaceOp "capability:Unlock/door.unlock" (capabilityOp "unlock" "door.open-lock"))
    // Decision binding and cohort are refused outright.
    Assert.NotEmpty(violations (withBinding (fun b -> { b with DecisionIds = [ "DEC-B" ] })))
    Assert.NotEmpty(violations (withBinding (fun b -> { b with CohortId = "COHORT-P" })))

[<Fact>]
let ``case is significant beyond the first letter`` () =
    // "Closed" and "CLOSED" are distinct names; renaming changes identity.
    changesIdentity (plan |> mapNames (fun raw -> if raw = "open" then "OPEN" else raw))

[<Fact>]
let ``the digest is stable across repeated validation`` () =
    let digests = List.init 50 (fun _ -> validDigest plan) |> List.distinct
    Assert.Equal(1, List.length digests)

[<Fact>]
let ``the digest is sha256 over lowercase hex`` () =
    Assert.Matches(Regex("^sha256:[0-9a-f]{64}$"), validDigest plan)

[<Fact>]
let ``the digest is the hash of the canonical json`` () =
    Assert.Equal(sha256Of (canonical plan), validDigest plan)

[<Fact>]
let ``the canonical json reads back and revalidates to the same identity`` () =
    match ConstructionPlanWire.readPlan (canonical plan) with
    | Error message -> failwith message
    | Ok draft -> assertSameIdentity draft

[<Fact>]
let ``the wire round trip preserves identity`` () =
    match ConstructionPlanWire.readPlan (ConstructionPlanWire.encodePlan plan) with
    | Error message -> failwith message
    | Ok draft -> Assert.Equal(validDigest plan, validDigest draft)

// ---------------------------------------------------------------------------
// 8. Readiness cannot be bypassed.
// ---------------------------------------------------------------------------

[<Fact>]
let ``a cohort with an unapproved decision cannot be prepared`` () =
    match prepareCohort manifest blueprint cohortId Set.empty with
    | Ok _ -> failwith "an unapproved decision must block preparation"
    | Error errors -> Assert.Contains(MissingDecisionAuthorization "DEC-A", errors)

[<Fact>]
let ``a cohort depending on a deferred requirement cannot be prepared`` () =
    let bp = blueprintWith [ "CONTRACT-P"; "DEF-D" ]
    match prepareCohort manifest bp cohortId approvals with
    | Ok _ -> failwith "a deferred dependency must block preparation"
    | Error errors -> Assert.Contains(BlockedRequirement rD, errors)

[<Fact>]
let ``a cohort depending on an unresolved requirement cannot be prepared`` () =
    let m = manifestOf [ srcA; srcL; srcP; srcD; srcU ]
    let bp =
        let b = blueprintWith [ "CONTRACT-P"; "CONFLICT-U" ]
        { b with
            SourceManifestDigest = m.Digest
            Requirements = b.Requirements @ [ row srcU (Unresolved "contradictory") [ "CONFLICT-U" ] ]
            Nodes = b.Nodes @ [ node "CONFLICT-U" Conflict [ srcU.Key ] [] ] }
    Assert.Empty(ConstructionIr.validate m bp)
    match prepareCohort m bp cohortId approvals with
    | Ok _ -> failwith "an unresolved dependency must block preparation"
    | Error errors -> Assert.Contains(BlockedRequirement srcU.Key, errors)

[<Fact>]
let ``a plan for an unready cohort cannot be validated against any prepared cohort`` () =
    // The only prepared cohort for this fixture is the door; preparing the
    // prerequisite cohort and reusing the door plan is refused.
    match prepareCohort manifest blueprint "COHORT-P" Set.empty with
    | Error errors -> failwithf "prerequisite cohort should be ready: %A" errors
    | Ok other ->
        match ConstructionPlan.validate other plan with
        | Ok _ -> failwith "a plan for one cohort must not validate against another"
        | Error errors -> Assert.Contains(CohortMismatch(cohortId, "COHORT-P"), errors)
