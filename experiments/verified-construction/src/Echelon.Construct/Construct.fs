/// The whole construction as one pure function: handoff bytes and source
/// digests in; generated files and the trace out. The CLI (Program.fs) only
/// reads and writes files around it.
module Echelon.Construct.Construct

open System.Text
open Ordo.Core.Json
open Ordo.Core.ConstructionIrWire
open Ordo.Core.ConstructionPlan
open Ordo.Core.ConstructionClassification
open Ordo.Construction
open Echelon.Construct.Admission
open Echelon.Construct.Translate

type ConstructError =
    | Inadmissible of AdmissionError list
    | Untranslatable of TranslationError list
    | EngineRefused of string list
    | TraceRefused of Trace.TraceError list

type Output =
    { /// (path relative to the experiment root, content), in path order. Includes the trace.
      Files: (string * string) list
      HandoffDigest: string
      Classification: Classification
      Construction: Construction.Construction }

let private generatedRoot = "src/Invoice.Slice/"

let private json value = render value + "\n"

/// Pure. Same inputs give byte-identical output.
let run (handoffBytes: byte array) (sourceDigests: Map<string, string>) : Result<Output, ConstructError> =
    let handoffDigest = Trace.sha256Hex handoffBytes
    match readAndAdmit (Encoding.UTF8.GetString handoffBytes) with
    | Error e -> Error(Inadmissible e)
    | Ok admitted ->
        match verifySources admitted sourceDigests with
        | _ :: _ as e -> Error(Untranslatable e)
        | [] ->
            match validatedPlan admitted with
            | Error e -> Error(Untranslatable e)
            | Ok(authority, ir, _, validated) ->
                match Construction.construct validated with
                | Error e -> Error(EngineRefused(e |> List.map (sprintf "%A")))
                | Ok construction ->
                    let generated =
                        construction.Artifacts |> List.map (fun a -> generatedRoot + a.Path, a.Content)
                    let authorityFile = AuthorityEmit.Path, AuthorityEmit.emit handoffDigest admitted authority
                    let classification = classify validated
                    let evidence =
                        [ "out/ecir/source-manifest.json", encodeManifest ir.Manifest + "\n"
                          "out/ecir/blueprint.json", encodeBlueprint ir.Blueprint + "\n"
                          "out/plan.v4.json", canonicalJson validated + "\n"
                          "out/classification.json", json (toJson classification) ]
                    let files = generated @ [ authorityFile ] @ evidence
                    let constructs = Trace.generatedConstructs admitted ir validated @ Handwritten.constructs
                    match Trace.build admitted constructs with
                    | Error e -> Error(TraceRefused e)
                    | Ok traced ->
                        let outputs = files |> List.map (fun (path, content) -> path, Trace.sha256Text content)
                        let trace =
                            Trace.renderTrace (handoffOf admitted).HandoffId handoffDigest traced (Trace.excluded admitted) outputs
                        Ok { Files = files @ [ Trace.Path, trace ] |> List.sortBy fst
                             HandoffDigest = handoffDigest
                             Classification = classification
                             Construction = construction }
