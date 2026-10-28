/// Command-line parsing, and nothing else.
///
/// This is an adapter: it turns argv into a value from the lifecycle's
/// vocabulary and then gets out of the way. It performs no filesystem
/// access, makes no lifecycle decision, and has no knowledge of what any
/// command does.
module Sde.Cli.Args

type InitOptions =
    { DryRun: bool
      /// Report whether changes are required and exit with a distinct code
      /// if so. Implies no writes.
      Check: bool }

type StatusOptions = { Unused: unit }

type VerifyOptions =
    { Strict: bool
      /// Fail on every installation-integrity condition and report
      /// structural findings as review signals. Excludes `Strict`.
      IntegrityOnly: bool }

type UpgradeOptions = { DryRun: bool; Check: bool }

type DoctorOptions = { Unused: unit }

/// `boundary assess`: every value is a path the caller supplied. Reading
/// and interpreting the files is the command's job, not the parser's.
type BoundaryAssessOptions =
    { Map: string
      Expected: string
      Changed: string
      Policy: string option
      /// When given, changed source files under this directory are read to
      /// extract their imports (F# `open`, C# `using`).
      Root: string option }

type EcirValidateOptions =
    { Manifest: string
      Blueprint: string
      Cohort: string option }

/// Manifest-free consumer admission of one published contract, optionally
/// against a digest the caller pinned earlier (stale-reference detection).
/// With `RequirementKeys` it is a readiness assessment of the work that
/// completes those requirements under `AuthorizedDecisionIds` (REL-GATE-002).
/// The IDs are the caller's statement of what its own authority approved; this
/// CLI checks readiness against them and grants nothing.
type EcirCheckOptions =
    { Blueprint: string
      ExpectedDigest: string option
      RequirementKeys: string list
      AuthorizedDecisionIds: string list }

type EcirScaffoldOptions =
    { Manifest: string
      Output: string }

type EcirRecognizeOptions =
    { Manifest: string
      Blueprint: string
      Cohort: string
      Decisions: string option
      Approvals: string option
      Pin: string option
      PacketsDirectory: string option
      AllowedPaths: string list
      EscalateOpenQuestions: bool }

/// `verification schedule`: order the work an admitted verification plan
/// implies. Every value is a path or identity the caller supplied; the
/// command reads, never writes, except `Output` when given.
type VerificationScheduleOptions =
    { Plan: string
      ExpectedDigest: string option
      EvidenceManifest: string option
      SourceSha: string option
      RunId: string option
      /// `ordo.verification-reuse-context/1`: the only way deterministic
      /// evidence from another run can be reused (DF-SDE-2026-0026).
      ReuseContext: string option
      Output: string option }

type Command =
    | VerificationSchedule of VerificationScheduleOptions
    | EcirValidate of EcirValidateOptions
    | EcirCheck of EcirCheckOptions
    | EcirScaffold of EcirScaffoldOptions
    | EcirRecognize of EcirRecognizeOptions
    | Init of InitOptions
    | Status of StatusOptions
    | Verify of VerifyOptions
    | Upgrade of UpgradeOptions
    | Doctor of DoctorOptions
    | BoundaryAssess of BoundaryAssessOptions
    /// Help for the whole tool, or for one named command.
    | Help of topic: string option
    | Version

type GlobalOptions =
    { /// Emit a single JSON document on stdout and nothing else.
      Json: bool
      Verbose: bool }

type Invocation =
    { Command: Command
      Global: GlobalOptions }

type ParseResult =
    | Parsed of Invocation
    | ParseFailed of message: string

/// Command names understood by the parser. `update` is retained as an alias
/// for `upgrade`: it was the released name through 1.1.1 and scripts in
/// consumers' repositories still call it.
let private canonicalCommandNames = [ "init"; "status"; "verify"; "upgrade"; "doctor"; "boundary"; "ecir"; "verification" ]
let private legacyUpgradeAlias = "update"

let allCommandNames = canonicalCommandNames @ [ legacyUpgradeAlias ]

let private usageLine =
    "Usage: sde <init|status|verify|upgrade|doctor|boundary|ecir|verification> [options]"

let usage = usageLine

let boundaryUsage =
    "Usage: sde boundary assess --map FILE --expected FILE --changed FILE [--policy FILE] [--root DIR] [--json]"

/// `boundary` takes options with values, which the lifecycle commands never
/// have, so it is parsed on its own rather than by widening the flag-only
/// grammar every other command shares.
let private parseBoundary (globals: GlobalOptions) (argv: string list) : ParseResult =
    let valued = [ "--map"; "--expected"; "--changed"; "--policy"; "--root" ]
    let switches = [ "--json"; "--verbose"; "-v"; "--help"; "-h" ]

    let rec walk args (values: Map<string, string>) (positionals: string list) =
        match args with
        | [] -> Ok(values, List.rev positionals)
        | flag :: rest when List.contains flag valued ->
            match rest with
            | value :: tail when not (value.StartsWith "--") ->
                if values.ContainsKey flag then
                    Error(sprintf "Option given twice: %s\n%s" flag boundaryUsage)
                else
                    walk tail (values.Add(flag, value)) positionals
            | _ -> Error(sprintf "Option %s requires a value\n%s" flag boundaryUsage)
        | flag :: rest when List.contains flag switches -> walk rest values positionals
        | flag :: _ when flag.StartsWith "-" -> Error(sprintf "Unknown option: %s\n%s" flag boundaryUsage)
        | positional :: rest -> walk rest values (positional :: positionals)

    let wantsHelp = argv |> List.exists (fun a -> a = "--help" || a = "-h")

    match walk argv Map.empty [] with
    | Error message -> ParseFailed message
    | Ok(_, _) when wantsHelp -> Parsed { Command = Help(Some "boundary"); Global = globals }
    | Ok(values, positionals) ->
        match positionals with
        | [ "boundary"; "assess" ] ->
            let missing =
                [ "--map"; "--expected"; "--changed" ] |> List.filter (fun f -> not (values.ContainsKey f))

            match missing with
            | first :: _ -> ParseFailed(sprintf "Missing required option: %s\n%s" first boundaryUsage)
            | [] ->
                Parsed
                    { Command =
                        BoundaryAssess
                            { Map = values.["--map"]
                              Expected = values.["--expected"]
                              Changed = values.["--changed"]
                              Policy = values.TryFind "--policy"
                              Root = values.TryFind "--root" }
                      Global = globals }
        | [ "boundary" ] -> ParseFailed(sprintf "Missing subcommand: assess\n%s" boundaryUsage)
        | "boundary" :: other :: _ -> ParseFailed(sprintf "Unknown boundary subcommand: %s\n%s" other boundaryUsage)
        | _ -> ParseFailed boundaryUsage

let ecirUsage =
    "Usage: sde ecir validate --manifest FILE --blueprint FILE [--cohort ID] [--json] | sde ecir check --blueprint FILE [--expected-digest sha256:HEX] [--requirement KEY]... [--authorized-decision ID]... [--json] | sde ecir scaffold --manifest FILE --output FILE [--json] | sde ecir recognize --manifest FILE --blueprint FILE --cohort ID [--decisions FILE] [--approvals FILE] [--pin FILE] [--allow GLOB]... [--escalate-open-questions] [--packets DIR] [--json]"

/// Only structural/semantic validation can be authorized by this CLI.
//// Cohort-level execution also needs externally verified decision approvals;
/// passing a string on the command line is deliberately not authorization.
let private parseEcir (globals: GlobalOptions) (argv: string list) : ParseResult =
    let valued = [ "--manifest"; "--blueprint"; "--expected-digest"; "--cohort"; "--output"; "--decisions"; "--approvals"; "--pin"; "--packets" ]
    let switches = [ "--json"; "--verbose"; "-v"; "--help"; "-h"; "--escalate-open-questions" ]
    let repeated = [ "--allow"; "--requirement"; "--authorized-decision" ]
    let valuesOf name =
        argv |> List.pairwise |> List.choose (fun (flag, value) -> if flag = name then Some value else None)
    let readinessFlags = not (List.isEmpty (valuesOf "--requirement") && List.isEmpty (valuesOf "--authorized-decision"))
    let allowed = valuesOf "--allow"
    let rec walk args values positionals =
        match args with
        | [] -> Ok(values, List.rev positionals)
        | flag :: value :: rest when List.contains flag repeated && not (value.StartsWith "--") -> walk rest values positionals
        | flag :: _ when List.contains flag repeated -> Error(sprintf "Missing value: %s" flag)
        | flag :: value :: rest when List.contains flag valued && not (value.StartsWith "--") ->
            if Map.containsKey flag values then
                Error(sprintf "Option given twice: %s" flag)
            else walk rest (Map.add flag value values) positionals
        | flag :: rest when List.contains flag valued -> Error(sprintf "Missing value: %s" flag)
        | flag :: rest when List.contains flag switches -> walk rest values positionals
        | flag :: _ when flag.StartsWith "-" -> Error(sprintf "Unknown ECIR flag: %s" flag)
        | item :: rest -> walk rest values (item :: positionals)
    match walk argv Map.empty [] with
    | Error e -> ParseFailed(e + "\n" + ecirUsage)
    | Ok(_, _) when List.contains "--help" argv || List.contains "-h" argv ->
        Parsed { Command = Help(Some "ecir"); Global = globals }
    | Ok(_, positionals) when readinessFlags && positionals <> [ "ecir"; "check" ] -> ParseFailed ecirUsage
    | Ok(values, [ "ecir"; "validate" ]) ->
        match Map.tryFind "--manifest" values, Map.tryFind "--blueprint" values with
        | Some manifest, Some blueprint when not (Map.containsKey "--output" values) ->
            Parsed { Command = EcirValidate { Manifest = manifest; Blueprint = blueprint; Cohort = Map.tryFind "--cohort" values }; Global = globals }
        | _ -> ParseFailed ecirUsage
    | Ok(values, [ "ecir"; "check" ]) ->
        match Map.tryFind "--blueprint" values with
        | Some blueprint when
            [ "--manifest"; "--cohort"; "--output" ] |> List.forall (fun flag -> not (Map.containsKey flag values))
            && (List.isEmpty (valuesOf "--authorized-decision") || not (List.isEmpty (valuesOf "--requirement"))) ->
            Parsed
                { Command =
                    EcirCheck
                        { Blueprint = blueprint
                          ExpectedDigest = Map.tryFind "--expected-digest" values
                          RequirementKeys = valuesOf "--requirement"
                          AuthorizedDecisionIds = valuesOf "--authorized-decision" }
                  Global = globals }
        | _ -> ParseFailed ecirUsage
    | Ok(values, [ "ecir"; "scaffold" ]) ->
        match Map.tryFind "--manifest" values, Map.tryFind "--output" values with
        | Some manifest, Some output when
            not (Map.containsKey "--cohort" values)
            && not (Map.containsKey "--blueprint" values) ->
            Parsed { Command = EcirScaffold { Manifest = manifest; Output = output }; Global = globals }
        | _ -> ParseFailed ecirUsage
    | Ok(values, [ "ecir"; "recognize" ]) ->
        match Map.tryFind "--manifest" values, Map.tryFind "--blueprint" values, Map.tryFind "--cohort" values with
        | Some manifest, Some blueprint, Some cohort when not (Map.containsKey "--output" values) ->
            Parsed
                { Command =
                    EcirRecognize
                        { Manifest = manifest
                          Blueprint = blueprint
                          Cohort = cohort
                          Decisions = Map.tryFind "--decisions" values
                          Approvals = Map.tryFind "--approvals" values
                          Pin = Map.tryFind "--pin" values
                          PacketsDirectory = Map.tryFind "--packets" values
                          AllowedPaths = allowed
                          EscalateOpenQuestions = List.contains "--escalate-open-questions" argv }
                  Global = globals }
        | _ -> ParseFailed ecirUsage
    | _ -> ParseFailed ecirUsage

let verificationUsage =
    "Usage: sde verification schedule --plan FILE [--expected-digest HEX] [--evidence-manifest FILE --source-sha SHA --run-id ID [--reuse-context FILE]] [--output FILE] [--json]"

let private parseVerification (globals: GlobalOptions) (argv: string list) : ParseResult =
    let valued = [ "--plan"; "--expected-digest"; "--evidence-manifest"; "--source-sha"; "--run-id"; "--reuse-context"; "--output" ]
    let switches = [ "--json"; "--verbose"; "-v"; "--help"; "-h" ]

    let rec walk args (values: Map<string, string>) positionals =
        match args with
        | [] -> Ok(values, List.rev positionals)
        | flag :: value :: rest when List.contains flag valued && not (value.StartsWith "--") ->
            if values.ContainsKey flag then Error(sprintf "Option given twice: %s" flag)
            else walk rest (values.Add(flag, value)) positionals
        | flag :: _ when List.contains flag valued -> Error(sprintf "Option %s requires a value" flag)
        | flag :: rest when List.contains flag switches -> walk rest values positionals
        | flag :: _ when flag.StartsWith "-" -> Error(sprintf "Unknown option: %s" flag)
        | item :: rest -> walk rest values (item :: positionals)

    match walk argv Map.empty [] with
    | Error message -> ParseFailed(message + "\n" + verificationUsage)
    | Ok _ when List.contains "--help" argv || List.contains "-h" argv -> Parsed { Command = Help(Some "verification"); Global = globals }
    | Ok(values, [ "verification"; "schedule" ]) ->
        let evidence = [ "--evidence-manifest"; "--source-sha"; "--run-id" ] |> List.filter values.ContainsKey

        match values.TryFind "--plan" with
        | None -> ParseFailed("Missing required option: --plan\n" + verificationUsage)
        | Some _ when not evidence.IsEmpty && evidence.Length <> 3 ->
            ParseFailed("--evidence-manifest, --source-sha and --run-id are given together: evidence is judged only against a named subject\n" + verificationUsage)
        | Some _ when values.ContainsKey "--reuse-context" && evidence.IsEmpty ->
            ParseFailed("--reuse-context judges evidence: give it with --evidence-manifest, --source-sha and --run-id\n" + verificationUsage)
        | Some plan ->
            Parsed
                { Command =
                    VerificationSchedule
                        { Plan = plan
                          ExpectedDigest = values.TryFind "--expected-digest"
                          EvidenceManifest = values.TryFind "--evidence-manifest"
                          SourceSha = values.TryFind "--source-sha"
                          RunId = values.TryFind "--run-id"
                          ReuseContext = values.TryFind "--reuse-context"
                          Output = values.TryFind "--output" }
                  Global = globals }
    | _ -> ParseFailed verificationUsage

let parse (argv: string list) : ParseResult =
    let globalsOf (args: string list) =
        { Json = List.contains "--json" args
          Verbose = List.contains "--verbose" args || List.contains "-v" args }

    match argv |> List.tryFind (fun arg -> not (arg.StartsWith "-")) with
    | Some "boundary" -> parseBoundary (globalsOf argv) argv
    | Some "ecir" -> parseEcir (globalsOf argv) argv
    | Some "verification" -> parseVerification (globalsOf argv) argv
    | _ ->

    // Flags are recognised anywhere, before or after the command name, so
    // that `sde --json status` and `sde status --json` both work; neither
    // form is documented as the only one and users type both.
    let flags, positionals = argv |> List.partition (fun arg -> arg.StartsWith "-")

    let known =
        [ "--help"; "-h"; "--version"; "-V"; "--json"; "--verbose"; "-v"; "--dry-run"; "--check"; "--strict"; "--integrity-only" ]

    let unknown = flags |> List.filter (fun flag -> not (List.contains flag known))

    if not unknown.IsEmpty then
        ParseFailed(sprintf "Unknown option: %s\n%s" (List.head unknown) usageLine)
    else

    let has names = flags |> List.exists (fun flag -> List.contains flag names)

    let globals =
        { Json = has [ "--json" ]
          Verbose = has [ "--verbose"; "-v" ] }

    let wantsHelp = has [ "--help"; "-h" ]
    let wantsVersion = has [ "--version"; "-V" ]
    let dryRun = has [ "--dry-run" ]
    let check = has [ "--check" ]
    let strict = has [ "--strict" ]
    let integrityOnly = has [ "--integrity-only" ]

    let finish command =
        Parsed { Command = command; Global = globals }

    match positionals with
    | [] ->
        if wantsVersion then finish Version
        elif wantsHelp then finish (Help None)
        // Bare `sde` is a usage error, not a silent success: an automation
        // that loses its argument should fail rather than appear to work.
        else ParseFailed usageLine

    | [ name ] when wantsHelp && List.contains name allCommandNames -> finish (Help(Some name))

    | name :: rest ->
        if not rest.IsEmpty then
            ParseFailed(sprintf "Unexpected argument: %s\n%s" (List.head rest) usageLine)
        elif wantsVersion then
            finish Version
        else
            match name with
            | "verify" when strict && integrityOnly ->
                ParseFailed(
                    sprintf
                        "--integrity-only and --strict are mutually exclusive: --integrity-only already fails on every integrity condition, and reports structural findings instead of failing on them.\n%s"
                        usageLine
                )
            | other when integrityOnly && other <> "verify" ->
                ParseFailed(sprintf "--integrity-only applies only to verify.\n%s" usageLine)
            | "init" -> finish (Init { DryRun = dryRun || check; Check = check })
            | "status" -> finish (Status { Unused = () })
            | "verify" -> finish (Verify { Strict = strict; IntegrityOnly = integrityOnly })
            | "upgrade" -> finish (Upgrade { DryRun = dryRun || check; Check = check })
            | n when n = legacyUpgradeAlias -> finish (Upgrade { DryRun = dryRun || check; Check = check })
            | "doctor" -> finish (Doctor { Unused = () })
            | other -> ParseFailed(sprintf "Unknown command: %s\n%s" other usageLine)
