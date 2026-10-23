/// Paths and handoff mutation helpers shared by the tests.
module VerifiedConstruction.Tests.Fixtures

open System
open System.IO
open System.Text.Json.Nodes

/// The experiment root: the directory holding run.sh and fixture/.
let root =
    let rec find (current: DirectoryInfo) =
        if File.Exists(Path.Combine(current.FullName, "fixture", "invoice-slice", "handoff.json"))
           && File.Exists(Path.Combine(current.FullName, "run.sh")) then current.FullName
        else
            match current.Parent with
            | null -> failwith "experiment root not found"
            | parent -> find parent
    find (DirectoryInfo AppContext.BaseDirectory)

let path (relative: string) = Path.Combine(root, relative)

let handoffPath = path "fixture/invoice-slice/handoff.json"
let handoffBytes () = File.ReadAllBytes handoffPath
let handoffText () = File.ReadAllText handoffPath

/// The SHA-256 the shared fixture README pins (fixture-v1).
let PinnedHandoffSha256 = "ec5d0e6a4343b1774d40760c04af200a9b71147940305d1ddbabbc5c6db68bcd"

let private node (text: string) =
    match JsonNode.Parse text with
    | null -> failwith "empty JSON"
    | n -> n

let private find (array: JsonNode) (id: string) =
    array.AsArray() |> Seq.find (fun item -> (match item with null -> false | i -> string i["id"] = id))
    |> function null -> failwith id | n -> n.AsObject()

/// Applies `change` to the parsed fixture and returns the edited JSON text.
let mutate (change: JsonObject -> unit) : string =
    let document = (node (handoffText ())).AsObject()
    change document
    document.ToJsonString()

let record (document: JsonObject) (collection: string) (id: string) =
    match document[collection] with
    | null -> failwith collection
    | array -> find array id

let jsonArray (values: string list) =
    let a = JsonArray()
    values |> List.iter (fun v -> a.Add(JsonValue.Create v))
    a

/// Digests of the source documents the handoff cites, as the CLI computes them.
let sourceDigests () =
    let h =
        match Echelon.Construct.Handoff.read (handoffText ()) with
        | Ok h -> h
        | Error e -> failwithf "%A" e
    (h.Requirements |> List.map _.Source.Path) @ (h.Decisions |> List.map _.Source.Path)
    |> List.distinct
    |> List.map (fun p -> p, Echelon.Construct.Trace.sha256Hex (File.ReadAllBytes(path ("fixture/invoice-slice/" + p))))
    |> Map.ofList

let ok label result =
    match result with
    | Ok value -> value
    | Error e -> failwithf "%s: %A" label e
