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

type VerifyOptions = { Strict: bool }

type UpgradeOptions = { DryRun: bool; Check: bool }

type DoctorOptions = { Unused: unit }

type Command =
    | Init of InitOptions
    | Status of StatusOptions
    | Verify of VerifyOptions
    | Upgrade of UpgradeOptions
    | Doctor of DoctorOptions
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
let private canonicalCommandNames = [ "init"; "status"; "verify"; "upgrade"; "doctor" ]
let private legacyUpgradeAlias = "update"

let allCommandNames = canonicalCommandNames @ [ legacyUpgradeAlias ]

let private usageLine =
    "Usage: sde <init|status|verify|upgrade|doctor> [options]"

let usage = usageLine

let parse (argv: string list) : ParseResult =
    // Flags are recognised anywhere, before or after the command name, so
    // that `sde --json status` and `sde status --json` both work; neither
    // form is documented as the only one and users type both.
    let flags, positionals = argv |> List.partition (fun arg -> arg.StartsWith "-")

    let known =
        [ "--help"; "-h"; "--version"; "-V"; "--json"; "--verbose"; "-v"; "--dry-run"; "--check"; "--strict" ]

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
            | "init" -> finish (Init { DryRun = dryRun || check; Check = check })
            | "status" -> finish (Status { Unused = () })
            | "verify" -> finish (Verify { Strict = strict })
            | "upgrade" -> finish (Upgrade { DryRun = dryRun || check; Check = check })
            | n when n = legacyUpgradeAlias -> finish (Upgrade { DryRun = dryRun || check; Check = check })
            | "doctor" -> finish (Doctor { Unused = () })
            | other -> ParseFailed(sprintf "Unknown command: %s\n%s" other usageLine)
