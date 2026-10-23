/// The construction trace: schema-valid, consistent with the source markers
/// and the built assemblies, closed over reuse, and free of the excluded
/// legacy rules.
module VerifiedConstruction.Tests.TraceTests

open System
open System.Diagnostics
open System.IO
open System.Reflection
open System.Text.Json
open System.Text.RegularExpressions
open Xunit
open Echelon.Construct
open Echelon.Provenance
open VerifiedConstruction.Tests.Fixtures

let private output () = Construct.run (handoffBytes ()) (sourceDigests ()) |> ok "construct"

let private traceText () = output().Files |> List.find (fst >> (=) Trace.Path) |> snd

let private traceJson () = JsonDocument.Parse(traceText ()).RootElement

let private constructs () =
    traceJson().GetProperty("constructs").EnumerateArray()
    |> Seq.map (fun c ->
        c.GetProperty("symbol").GetString() |> string,
        (c.GetProperty("disposition").GetString() |> string,
         c.GetProperty("authority").EnumerateArray() |> Seq.map (fun a -> a.GetProperty("id").GetString() |> string) |> List.ofSeq))
    |> Map.ofSeq

[<Fact>]
let ``the trace validates against echelon-construction-trace-0.1 (python jsonschema)`` () =
    let directory = Directory.CreateTempSubdirectory("vc-trace-").FullName
    try
        let trace = Path.Combine(directory, "trace.json")
        File.WriteAllText(trace, traceText ())
        let script =
            "import json,sys,jsonschema\n"
            + "s=json.load(open(sys.argv[1]));d=json.load(open(sys.argv[2]))\n"
            + "jsonschema.Draft202012Validator.check_schema(s)\n"
            + "errs=sorted(e.message for e in jsonschema.Draft202012Validator(s).iter_errors(d))\n"
            + "print('\\n'.join(errs));sys.exit(1 if errs else 0)\n"
        let scriptPath = Path.Combine(directory, "validate.py")
        File.WriteAllText(scriptPath, script)
        let start = ProcessStartInfo("python3", sprintf "-I \"%s\" \"%s\" \"%s\"" scriptPath (path "fixture/schemas/echelon-construction-trace-0.1.schema.json") trace)
        start.RedirectStandardOutput <- true
        start.RedirectStandardError <- true
        use p = match Process.Start start with null -> failwith "python3 could not be started" | p -> p
        let out = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd()
        p.WaitForExit()
        Assert.True((p.ExitCode = 0), "schema validation failed (requires python3 with jsonschema):\n" + out)
    finally
        Directory.Delete(directory, true)

[<Fact>]
let ``the trace names the handoff by ID and pinned digest`` () =
    let h = traceJson().GetProperty "handoff"
    Assert.Equal("HND-INVOICE-SLICE-001", h.GetProperty("handoffId").GetString())
    Assert.Equal("sha256:" + PinnedHandoffSha256, h.GetProperty("sha256").GetString())

[<Fact>]
let ``Q-FIN-001 constructs are deferred-pending-question, never authorized as a rule`` () =
    let c = constructs ()
    let disposition symbol = fst c[symbol]
    Assert.Equal("deferred-pending-question", disposition "Generated.Invoice.InvoiceTransition.undecided:PartiallyPaid/Void")
    Assert.Equal("deferred-pending-question", disposition "Generated.Invoice.InvoiceRefusalCode.PolicyUnresolvedQFIN001")
    Assert.Equal("deferred-pending-question", disposition "Invoice.Slice.Invoicing.pendingQuestion")
    Assert.DoesNotContain(c.Keys, fun s -> s.Contains "legal:PartiallyPaid/Void" || s.Contains "pairCode:PartiallyPaid/Void")

[<Fact>]
let ``the limit is authorized by DEC-FIN-001 and nothing cites the superseded DEC-FIN-000`` () =
    let c = constructs ()
    Assert.Contains("DEC-FIN-001", snd c["Invoice.Slice.Payment.PaymentLimit"])
    Assert.Contains("DEC-FIN-001", snd c["Generated.Invoice.InvoiceRefusalCode.PaymentLimitExceeded"])
    Assert.DoesNotContain(c.Values, fun (_, ids) -> List.contains "DEC-FIN-000" ids)
    Assert.DoesNotContain(c.Values, fun (_, ids) -> ids |> List.exists (fun id -> id.StartsWith "OBS-"))

[<Fact>]
let ``both legacy observations are excluded and routed to finance-controller`` () =
    let excluded =
        traceJson().GetProperty("excluded").EnumerateArray()
        |> Seq.map (fun e -> e.GetProperty("observationId").GetString() |> string, e.GetProperty("routedTo").GetString() |> string)
        |> List.ofSeq
    Assert.Equal<(string * string) list>([ "OBS-LEGACY-001", "finance-controller"; "OBS-LEGACY-002", "finance-controller" ], excluded)

[<Fact>]
let ``the excluded legacy rules do not appear in constructed source`` () =
    let files =
        [ "src/Invoice.Slice"; "src/Invoice.Slice.PaymentsService" ]
        |> List.collect (fun d -> Directory.GetFiles(path d, "*.fs", SearchOption.AllDirectories) |> List.ofArray)
        |> List.filter (fun f -> not (f.Contains "/obj/" || f.Contains "/bin/"))
    Assert.NotEmpty files
    for f in files do
        let code = File.ReadAllLines f |> Array.filter (fun l -> not (l.TrimStart().StartsWith "//")) |> String.concat "\n"
        Assert.False(code.Contains "25000", f + " carries the superseded 25,000 limit")
        Assert.False(code.Contains "STR-", f + " carries the STR- exemption")
        Assert.False(Regex.IsMatch(code, "(?i)strategic|category"), f + " carries a customer category")

[<Fact>]
let ``every output digest in the trace matches the file construct writes`` () =
    let files = output().Files |> Map.ofList
    for o in traceJson().GetProperty("outputs").EnumerateArray() do
        let p = o.GetProperty("path").GetString() |> string
        Assert.Equal(Trace.sha256Text files[p], o.GetProperty("sha256").GetString())

// ---- Source markers <-> trace <-> built assemblies -----------------------------

let private handwrittenFiles =
    Handwritten.constructs |> List.map _.File |> List.distinct

let private markerPattern = Regex("^\\s*\\[<Authority\\((\"[A-Z0-9-]+\"(, \"[A-Z0-9-]+\")*)\\)>\\]\\s*$")

[<Fact>]
let ``every Authority marker in source has exactly one trace row with the same IDs, and vice versa`` () =
    let markers =
        handwrittenFiles
        |> List.collect (fun f ->
            File.ReadAllLines(path f)
            |> Array.choose (fun line ->
                let m = markerPattern.Match line
                if m.Success then Some(f, m.Groups[1].Value.Replace("\"", "").Split(", ") |> Array.sort |> List.ofArray) else None)
            |> List.ofArray)
        |> List.sort
    let rows = Handwritten.constructs |> List.map (fun c -> c.File, c.Authority |> List.sort) |> List.sort
    Assert.Equal<(string * string list) list>(rows, markers)

let private symbolOf (m: MemberInfo) =
    let typeName (t: Type) = (t.FullName |> string).Replace('+', '.')
    match m with
    | :? Type as t -> typeName t
    | m ->
        match m.DeclaringType with
        | null -> m.Name
        | t -> typeName t + "." + m.Name

[<Fact>]
let ``every Authority attribute in the built assemblies matches its trace row`` () =
    let flags = BindingFlags.Public ||| BindingFlags.NonPublic ||| BindingFlags.Static ||| BindingFlags.Instance ||| BindingFlags.DeclaredOnly
    let assemblies =
        [ typeof<Invoice.Slice.Payment.ValidatedPayment>.Assembly
          typeof<Invoice.Slice.PaymentsService.CompositionRoot.ServiceError>.Assembly ]
    let attributed =
        assemblies
        |> List.collect (fun a ->
            a.GetTypes()
            // F# copies a module value's attributes onto its compiler-generated
            // startup-code field; those are not symbols.
            |> Array.filter (fun t -> not ((t.FullName |> string).StartsWith "<StartupCode$"))
            |> Array.collect (fun t -> Array.append [| t :> MemberInfo |] (t.GetMembers flags))
            |> Array.choose (fun m ->
                match m.GetCustomAttribute<AuthorityAttribute>() with
                | null -> None
                | attribute -> Some(symbolOf m, attribute.Ids |> Array.sort |> List.ofArray))
            |> List.ofArray)
        |> List.distinct
        |> List.sort
    let rows = Handwritten.constructs |> List.map (fun c -> c.Symbol, c.Authority |> List.sort) |> List.sort
    Assert.Equal<(string * string list) list>(rows, attributed)

[<Fact>]
let ``R2: a construct that reuses another cites every requirement, decision and question it reuses`` () =
    let index = Handwritten.constructs |> List.map (fun c -> c.Symbol, c.Authority) |> Map.ofList
    let isRule (id: string) = not (id.StartsWith "INV-")
    for user, reused in Handwritten.reuses do
        let missing = index[reused] |> List.filter isRule |> List.filter (fun id -> not (List.contains id index[user]))
        Assert.True(List.isEmpty missing, sprintf "%s reuses %s but does not cite %A" user reused missing)

[<Fact>]
let ``construct refuses a handwritten row that cites a superseded decision`` () =
    let admitted = Admission.readAndAdmit (handoffText ()) |> ok "admit"
    let bad : Handwritten.Construct =
        { Symbol = "X.y"; Kind = Handwritten.ConstructKind.Constant; File = "x.fs"
          Disposition = Handwritten.Disposition.Authorized; Authority = [ "DEC-FIN-000" ] }
    match Trace.build admitted [ bad ] with
    | Error errors -> Assert.Contains(Trace.NotCurrentAuthority("X.y", "DEC-FIN-000"), errors)
    | Ok _ -> failwith "expected refusal"

[<Fact>]
let ``construct refuses a row that cites an observation as authority`` () =
    let admitted = Admission.readAndAdmit (handoffText ()) |> ok "admit"
    let bad : Handwritten.Construct =
        { Symbol = "X.y"; Kind = Handwritten.ConstructKind.Constant; File = "x.fs"
          Disposition = Handwritten.Disposition.Authorized; Authority = [ "OBS-LEGACY-001" ] }
    match Trace.build admitted [ bad ] with
    | Error errors -> Assert.Contains(Trace.UnknownAuthority("X.y", "OBS-LEGACY-001"), errors)
    | Ok _ -> failwith "expected refusal"
