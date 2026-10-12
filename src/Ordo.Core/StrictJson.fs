/// Strict decoding for versioned semantic wire contracts (ECIR, construction
/// plans). An object must contain exactly its permitted members, once each;
/// strings that name things must be nonblank. Errors carry the JSON path.
module internal Ordo.Core.StrictJson

open System
open Ordo.Core.Json

type ResultBuilder() =
    member _.Bind(value, f) = Result.bind f value
    member _.Return(value) = Ok value
    member _.ReturnFrom(value) = value

let result = ResultBuilder()
let fail path message = Error(sprintf "%s: %s" path message)

let fields path required optional value =
    match value with
    | JObject properties ->
        let names = properties |> List.map fst
        let dup = names |> List.countBy id |> List.tryFind (fun (_, count) -> count <> 1)
        let unknown = names |> List.tryFind (fun name -> not (List.contains name (required @ optional)))
        let missing = required |> List.tryFind (fun name -> not (List.contains name names))
        match dup, unknown, missing with
        | Some(name, _), _, _ -> fail path ("duplicate JSON member " + name)
        | _, Some name, _ -> fail path ("unknown JSON member " + name)
        | _, _, Some name -> fail path ("missing JSON member " + name)
        | _ -> Ok(Map.ofList properties)
    | _ -> fail path "expected object"

let field path name (properties: Map<string, JsonValue>) =
    match Map.tryFind name properties with
    | Some value -> Ok value
    | None -> fail path ("missing " + name)

let asText path value =
    match value with
    | JString str when not (String.IsNullOrWhiteSpace str) -> Ok str
    | _ -> fail path "expected nonblank string"

let textField path key properties =
    result {
        let! raw = field path key properties
        return! asText (path + "." + key) raw
    }

let strings path value =
    match value with
    | JArray items ->
        items |> List.mapi (fun index item -> asText (sprintf "%s[%d]" path index) item)
        |> List.fold (fun state next ->
            result { let! prior = state
                     let! value = next
                     return value :: prior }) (Ok [])
        |> Result.map List.rev
    | _ -> fail path "expected array of strings"

let items path decoder value =
    match value with
    | JArray values ->
        values
        |> List.mapi (fun index v -> decoder (sprintf "%s[%d]" path index) v)
        |> List.fold (fun state next ->
            result { let! prior = state
                     let! item = next
                     return item :: prior }) (Ok [])
        |> Result.map List.rev
    | _ -> fail path "expected array"
