/// The one wire vocabulary executable Ordo reads and writes.
///
/// Everything Ordo persists or sends to a provider is built as a `JsonValue`
/// and rendered by this module. Nothing concatenates JSON by hand, and
/// nothing derives a wire shape from an F# type name — renaming a record or
/// a union case must not be able to break a persisted record
/// (ORDO-8402 / ORDO-3-181).
///
/// Two renderings exist on purpose. `render` preserves the field order the
/// caller wrote, which is what a human reads in a stored record.
/// `renderCanonical` sorts object keys by ordinal and is the only rendering
/// a fingerprint is ever taken over, so that a fingerprint depends on
/// meaning rather than on the order a record happened to be constructed in
/// (ORDO-5803).
module Ordo.Core.Json

open System
open System.Text
open System.Text.Json

type JsonValue =
    | JNull
    | JBool of bool
    | JInt of int64
    | JFloat of float
    | JString of string
    | JArray of JsonValue list
    | JObject of (string * JsonValue) list

/// Why a JSON document could not be turned into the value a caller needed.
///
/// Decoding failures carry the path at which they occurred because the
/// normal consumer is an agent or an operator diagnosing a rejected provider
/// response, and "something was missing somewhere" is not actionable
/// (ORDO-9902).
type JsonError =
    | MalformedJson of message: string
    | UnexpectedType of path: string * expected: string
    | MissingMember of path: string
    | UnsupportedNumber of path: string

let private escapeInto (sb: StringBuilder) (value: string) =
    sb.Append '"' |> ignore

    for ch in value do
        match ch with
        | '"' -> sb.Append "\\\"" |> ignore
        | '\\' -> sb.Append "\\\\" |> ignore
        | '\b' -> sb.Append "\\b" |> ignore
        | '\f' -> sb.Append "\\f" |> ignore
        | '\n' -> sb.Append "\\n" |> ignore
        | '\r' -> sb.Append "\\r" |> ignore
        | '\t' -> sb.Append "\\t" |> ignore
        | c when c < ' ' -> sb.AppendFormat("\\u{0:x4}", int c) |> ignore
        | c -> sb.Append c |> ignore

    sb.Append '"' |> ignore

/// Renders a number the way every JSON reader will read it back unchanged:
/// round-trip format, invariant culture, and never a bare `NaN`/`Infinity`,
/// which are not JSON and would make a record unreadable elsewhere.
let private renderFloat (value: float) : string =
    if Double.IsFinite value then
        value.ToString("R", Globalization.CultureInfo.InvariantCulture)
    else
        "null"

let private renderInto (sb: StringBuilder) (sortKeys: bool) (value: JsonValue) =
    let rec go value indent =
        let pad n = String(' ', n * 2)

        match value with
        | JNull -> sb.Append "null" |> ignore
        | JBool true -> sb.Append "true" |> ignore
        | JBool false -> sb.Append "false" |> ignore
        | JInt n -> sb.Append(n.ToString(Globalization.CultureInfo.InvariantCulture)) |> ignore
        | JFloat f -> sb.Append(renderFloat f) |> ignore
        | JString s -> escapeInto sb s
        | JArray [] -> sb.Append "[]" |> ignore
        | JArray items ->
            sb.Append "[\n" |> ignore

            items
            |> List.iteri (fun i item ->
                if i > 0 then sb.Append ",\n" |> ignore
                sb.Append(pad (indent + 1)) |> ignore
                go item (indent + 1))

            sb.Append('\n').Append(pad indent).Append ']' |> ignore
        | JObject [] -> sb.Append "{}" |> ignore
        | JObject members ->
            let members =
                if sortKeys then
                    members |> List.sortWith (fun (a, _) (b, _) -> String.CompareOrdinal(a, b))
                else
                    members

            sb.Append "{\n" |> ignore

            members
            |> List.iteri (fun i (name, item) ->
                if i > 0 then sb.Append ",\n" |> ignore
                sb.Append(pad (indent + 1)) |> ignore
                escapeInto sb name
                sb.Append ": " |> ignore
                go item (indent + 1))

            sb.Append('\n').Append(pad indent).Append '}' |> ignore

    go value 0

/// Human-readable rendering that preserves the caller's field order.
let render (value: JsonValue) : string =
    let sb = StringBuilder()
    renderInto sb false value
    sb.ToString()

/// Order-independent rendering. The only rendering a fingerprint is taken
/// over, so that two records with the same meaning fingerprint the same
/// regardless of construction order.
let renderCanonical (value: JsonValue) : string =
    let sb = StringBuilder()
    renderInto sb true value
    sb.ToString()

let private fromElement (element: JsonElement) : Result<JsonValue, JsonError> =
    let rec go (element: JsonElement) (path: string) : Result<JsonValue, JsonError> =
        match element.ValueKind with
        | JsonValueKind.Null
        | JsonValueKind.Undefined -> Ok JNull
        | JsonValueKind.True -> Ok(JBool true)
        | JsonValueKind.False -> Ok(JBool false)
        | JsonValueKind.String -> Ok(JString(element.GetString() |> Option.ofObj |> Option.defaultValue ""))
        | JsonValueKind.Number ->
            match element.TryGetInt64() with
            | true, n -> Ok(JInt n)
            | _ ->
                match element.TryGetDouble() with
                | true, f -> Ok(JFloat f)
                | _ -> Error(UnsupportedNumber path)
        | JsonValueKind.Array ->
            let mutable index = 0
            let mutable failure = None
            let items = ResizeArray()

            for item in element.EnumerateArray() do
                if failure.IsNone then
                    match go item (sprintf "%s[%d]" path index) with
                    | Ok value -> items.Add value
                    | Error e -> failure <- Some e

                index <- index + 1

            match failure with
            | Some e -> Error e
            | None -> Ok(JArray(List.ofSeq items))
        | JsonValueKind.Object ->
            let mutable failure = None
            let members = ResizeArray()

            for property in element.EnumerateObject() do
                if failure.IsNone then
                    match go property.Value (sprintf "%s.%s" path property.Name) with
                    | Ok value -> members.Add(property.Name, value)
                    | Error e -> failure <- Some e

            match failure with
            | Some e -> Error e
            | None -> Ok(JObject(List.ofSeq members))
        | kind -> Error(UnexpectedType(path, string kind))

    go element "$"

/// Parses text into the wire vocabulary. Provider output and persisted
/// records both arrive through here, and both are untrusted until a decoder
/// has checked them against a contract (ORDO-7501).
let parse (text: string) : Result<JsonValue, JsonError> =
    if String.IsNullOrEmpty text then
        Error(MalformedJson "input was null or empty")
    else
        try
            use document = JsonDocument.Parse(text, JsonDocumentOptions(AllowTrailingCommas = false))
            fromElement document.RootElement
        with :? JsonException as ex ->
            Error(MalformedJson ex.Message)

/// Looks up a member of an object. Returns `None` for an absent member and
/// an error for a value that is not an object at all, because "this record
/// has no such field" and "this is not a record" are different defects.
let tryMember (name: string) (value: JsonValue) : Result<JsonValue option, JsonError> =
    match value with
    | JObject members -> members |> List.tryFind (fst >> (=) name) |> Option.map snd |> Ok
    | _ -> Error(UnexpectedType("$", "object"))

let requiredMember (name: string) (value: JsonValue) : Result<JsonValue, JsonError> =
    match tryMember name value with
    | Error e -> Error e
    | Ok None -> Error(MissingMember("$." + name))
    | Ok(Some found) -> Ok found

let asString (path: string) (value: JsonValue) : Result<string, JsonError> =
    match value with
    | JString s -> Ok s
    | _ -> Error(UnexpectedType(path, "string"))

let asInt (path: string) (value: JsonValue) : Result<int, JsonError> =
    match value with
    | JInt n when n >= int64 Int32.MinValue && n <= int64 Int32.MaxValue -> Ok(int n)
    | JInt _ -> Error(UnsupportedNumber path)
    | _ -> Error(UnexpectedType(path, "int"))

let asFloat (path: string) (value: JsonValue) : Result<float, JsonError> =
    match value with
    | JFloat f -> Ok f
    | JInt n -> Ok(float n)
    | _ -> Error(UnexpectedType(path, "number"))

let asArray (path: string) (value: JsonValue) : Result<JsonValue list, JsonError> =
    match value with
    | JArray items -> Ok items
    | _ -> Error(UnexpectedType(path, "array"))

/// Collects a list of results, failing on the first error. Used by decoders
/// so that a malformed element fails the whole record rather than being
/// silently dropped.
let collect (results: Result<'a, JsonError> list) : Result<'a list, JsonError> =
    let rec go acc remaining =
        match remaining with
        | [] -> Ok(List.rev acc)
        | Error e :: _ -> Error e
        | Ok value :: rest -> go (value :: acc) rest

    go [] results
