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

/// True when the text holds a UTF-16 surrogate without its partner. Such
/// text has no UTF-8 form, so it can be neither persisted nor carried
/// verbatim, and readers in other languages disagree about it (Praxis
/// provenance contract revision 1.2; ORDO-PROV-06).
let hasUnpairedSurrogate (text: string) : bool =
    let rec scan index =
        if index >= text.Length then
            false
        elif Char.IsHighSurrogate text[index] then
            if index + 1 < text.Length && Char.IsLowSurrogate text[index + 1] then scan (index + 2) else true
        elif Char.IsLowSurrogate text[index] then
            true
        else
            scan (index + 1)

    scan 0

let private wellFormed (path: string) (text: string) : Result<string, JsonError> =
    if hasUnpairedSurrogate text then
        Error(MalformedJson(sprintf "%s: unpaired UTF-16 surrogate" path))
    else
        Ok text

/// Folds a sequence of fallible steps, stopping at the first error.
let private foldResult
    (step: 'state -> 'item -> Result<'state, JsonError>)
    (initial: 'state)
    (items: 'item seq)
    : Result<'state, JsonError> =
    items |> Seq.fold (fun state item -> state |> Result.bind (fun current -> step current item)) (Ok initial)

let private fromElement (element: JsonElement) : Result<JsonValue, JsonError> =
    let rec go (element: JsonElement) (path: string) : Result<JsonValue, JsonError> =
        match element.ValueKind with
        | JsonValueKind.Null
        | JsonValueKind.Undefined -> Ok JNull
        | JsonValueKind.True -> Ok(JBool true)
        | JsonValueKind.False -> Ok(JBool false)
        | JsonValueKind.String ->
            element.GetString() |> Option.ofObj |> Option.defaultValue "" |> wellFormed path |> Result.map JString
        | JsonValueKind.Number ->
            match element.TryGetInt64() with
            | true, n -> Ok(JInt n)
            | _ ->
                match element.TryGetDouble() with
                | true, f -> Ok(JFloat f)
                | _ -> Error(UnsupportedNumber path)
        | JsonValueKind.Array ->
            element.EnumerateArray()
            |> Seq.indexed
            |> foldResult
                (fun items (index, item) -> go item (sprintf "%s[%d]" path index) |> Result.map (fun value -> value :: items))
                []
            |> Result.map (List.rev >> JArray)
        | JsonValueKind.Object ->
            // A member name repeated within one object is malformed: readers
            // disagree about which duplicate wins, so a second value could be
            // smuggled past one of them (ORDO-PROV-06, contract 1.2).
            element.EnumerateObject()
            |> foldResult
                (fun (seen: Set<string>, members) property ->
                    let child = sprintf "%s.%s" path property.Name

                    wellFormed child property.Name
                    |> Result.bind (fun name ->
                        if seen.Contains name then
                            Error(MalformedJson(sprintf "%s: member name repeated within one object" child))
                        else
                            go property.Value child |> Result.map (fun value -> seen.Add name, (name, value) :: members)))
                (Set.empty, [])
            |> Result.map (snd >> List.rev >> JObject)
        | kind -> Error(UnexpectedType(path, string kind))

    go element "$"

/// Parses text into the wire vocabulary. Provider output and persisted
/// records both arrive through here, and both are untrusted until a decoder
/// has checked them against a contract (ORDO-7501).
///
/// Total: text that is not JSON, that repeats a member name within one
/// object, or that holds an unpaired UTF-16 surrogate is `MalformedJson`,
/// never an exception. System.Text.Json reports such text through
/// `JsonException`, `ArgumentException` or `InvalidOperationException`
/// depending on where it notices, so all three are caught.
let parse (text: string) : Result<JsonValue, JsonError> =
    if String.IsNullOrEmpty text then
        Error(MalformedJson "input was null or empty")
    elif hasUnpairedSurrogate text then
        Error(MalformedJson "input holds an unpaired UTF-16 surrogate")
    else
        try
            use document = JsonDocument.Parse(text, JsonDocumentOptions(AllowTrailingCommas = false))
            fromElement document.RootElement
        with
        | :? JsonException as ex -> Error(MalformedJson ex.Message)
        | :? ArgumentException as ex -> Error(MalformedJson ex.Message)
        | :? InvalidOperationException as ex -> Error(MalformedJson ex.Message)

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
