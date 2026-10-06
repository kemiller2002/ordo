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

type Command =
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
let private canonicalCommandNames = [ "init"; "status"; "verify"; "upgrade"; "doctor"; "boundary" ]
let private legacyUpgradeAlias = "update"

let allCommandNames = canonicalCommandNames @ [ legacyUpgradeAlias ]

let private usageLine =
    "Usage: sde <init|status|verify|upgrade|doctor|boundary> [options]"

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

let parse (argv: string list) : ParseResult =
    let globalsOf (args: string list) =
        { Json = List.contains "--json" args
          Verbose = List.contains "--verbose" args || List.contains "-v" args }

    match argv |> List.tryFind (fun arg -> not (arg.StartsWith "-")) with
    | Some "boundary" -> parseBoundary (globalsOf argv) argv
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
