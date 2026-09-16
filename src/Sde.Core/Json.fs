/// The one place JSON is produced or consumed.
///
/// Every machine-readable surface this tool exposes (`--json` output, the
/// installed MANIFEST.json, the .echelon installation record) is built from
/// the `JsonValue` union below and rendered by `render`. Nothing anywhere
/// else concatenates JSON by hand, so a schema is a value that can be
/// constructed and tested rather than a string that happens to parse.
///
/// Rendering deliberately matches `JSON.stringify(value, null, 2)` byte for
/// byte: the previous JavaScript implementation wrote MANIFEST.json that
/// way, and an installation written by this tool must hash identically to
/// one written by the release that preceded it.
module Sde.Core.Json

open System
open System.Text
open System.Text.Json

type JsonValue =
    | JNull
    | JBool of bool
    | JInt of int
    | JString of string
    | JArray of JsonValue list
    | JObject of (string * JsonValue) list

/// JSON string escaping with the same rules as JSON.stringify: quote and
/// backslash, the named control escapes, \u00XX for the remaining C0
/// controls, and every other character (including '/' and non-ASCII) passed
/// through unchanged.
let escapeInto (sb: StringBuilder) (value: string) =
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

/// JSON-encodes a single string, used for quoting untrusted values inside
/// human-readable error messages so a path containing a newline or a quote
/// cannot forge extra output lines.
let quote (value: string) : string =
    let sb = StringBuilder()
    escapeInto sb value
    sb.ToString()

let private renderInto (sb: StringBuilder) (value: JsonValue) =
    let rec go value indent =
        let pad n = String(' ', n * 2)

        match value with
        | JNull -> sb.Append "null" |> ignore
        | JBool true -> sb.Append "true" |> ignore
        | JBool false -> sb.Append "false" |> ignore
        | JInt n -> sb.Append(n.ToString Globalization.CultureInfo.InvariantCulture) |> ignore
        | JString s -> escapeInto sb s
        | JArray [] -> sb.Append "[]" |> ignore
        | JArray items ->
            sb.Append "[\n" |> ignore

            items
            |> List.iteri (fun i item ->
                if i > 0 then sb.Append ",\n" |> ignore
                sb.Append(pad (indent + 1)) |> ignore
                go item (indent + 1))

            sb.Append("\n").Append(pad indent).Append "]" |> ignore
        | JObject [] -> sb.Append "{}" |> ignore
        | JObject fields ->
            sb.Append "{\n" |> ignore

            fields
            |> List.iteri (fun i (name, fieldValue) ->
                if i > 0 then sb.Append ",\n" |> ignore
                sb.Append(pad (indent + 1)) |> ignore
                escapeInto sb name
                sb.Append ": " |> ignore
                go fieldValue (indent + 1))

            sb.Append("\n").Append(pad indent).Append "}" |> ignore

    go value 0

/// Renders with two-space indentation and no trailing newline.
let render (value: JsonValue) : string =
    let sb = StringBuilder()
    renderInto sb value
    sb.ToString()

/// Renders a document destined for a file: two-space indentation plus the
/// single trailing newline `JSON.stringify(...) + "\n"` produced.
let renderDocument (value: JsonValue) : string = render value + "\n"

// ---------------------------------------------------------------------------
// Reading
// ---------------------------------------------------------------------------

/// Parses text into a JsonValue, or reports why it is not JSON. Numbers that
/// are not 32-bit integers are read as strings, because no schema this tool
/// owns has a non-integer number in it and silently rounding one would be
/// worse than refusing it.
let parse (text: string) : Result<JsonValue, string> =
    let rec convert (element: JsonElement) =
        match element.ValueKind with
        | JsonValueKind.Null
        | JsonValueKind.Undefined -> JNull
        | JsonValueKind.True -> JBool true
        | JsonValueKind.False -> JBool false
        | JsonValueKind.String -> JString(element.GetString() |> Option.ofObj |> Option.defaultValue "")
        | JsonValueKind.Number ->
            match element.TryGetInt32() with
            | true, n -> JInt n
            | _ -> JString(element.GetRawText())
        | JsonValueKind.Array -> JArray [ for item in element.EnumerateArray() -> convert item ]
        | JsonValueKind.Object -> JObject [ for property in element.EnumerateObject() -> property.Name, convert property.Value ]
        | _ -> JNull

    try
        use document = JsonDocument.Parse(text, JsonDocumentOptions(CommentHandling = JsonCommentHandling.Disallow))
        Ok(convert document.RootElement)
    with
    | :? JsonException as ex -> Error ex.Message
    | :? ArgumentException as ex -> Error ex.Message

let tryField (name: string) (value: JsonValue) : JsonValue option =
    match value with
    | JObject fields -> fields |> List.tryPick (fun (key, v) -> if key = name then Some v else None)
    | _ -> None

let tryString (value: JsonValue option) : string option =
    match value with
    | Some(JString s) -> Some s
    | _ -> None

let tryInt (value: JsonValue option) : int option =
    match value with
    | Some(JInt n) -> Some n
    | _ -> None

let tryBool (value: JsonValue option) : bool option =
    match value with
    | Some(JBool b) -> Some b
    | _ -> None

let tryArray (value: JsonValue option) : JsonValue list option =
    match value with
    | Some(JArray items) -> Some items
    | _ -> None
