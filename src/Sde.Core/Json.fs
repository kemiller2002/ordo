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

// ---------------------------------------------------------------------------
// Editing a document another party owns
// ---------------------------------------------------------------------------

/// What setting one string property of a JSON object document amounts to.
type PropertyUpdate =
    /// The property already holds the value; the document is left byte for byte.
    | Unchanged
    /// The document with only that property set. `previous` is the string the
    /// property held before, if it held one.
    | Rewritten of previous: string option * document: string
    /// Not a JSON object this tool can rewrite without losing something (it
    /// is malformed, carries comments, or is not an object at all).
    | NotAnObject

let private editOptions =
    JsonSerializerOptions(WriteIndented = true, Encoder = Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping)

/// Sets the string property `key` of the object document `text` to `value`,
/// changing nothing else: every other property keeps its value (numbers keep
/// their exact text) and its position, and a missing property is added last.
/// Used for files shared with the repository and other tools, where only one
/// property is this tool's to write. Pure; the result ends with one newline.
let setStringProperty (key: string) (value: string) (text: string) : PropertyUpdate =
    let parsed =
        try
            let node =
                Nodes.JsonNode.Parse(text, documentOptions = JsonDocumentOptions(CommentHandling = JsonCommentHandling.Disallow))

            // Materialise an object's properties here, where a duplicate
            // property name is caught, rather than while rewriting it.
            match node with
            | :? Nodes.JsonObject as current -> current.Count |> ignore
            | _ -> ()

            Some node
        with
        | :? JsonException
        | :? ArgumentException -> None

    match parsed with
    | Some(:? Nodes.JsonObject as current) ->
        let previous =
            match current[key] with
            | :? Nodes.JsonValue as held when held.GetValueKind() = JsonValueKind.String -> Some(held.GetValue<string>())
            | _ -> None

        if previous = Some value then
            Unchanged
        else
            let pinned: Nodes.JsonNode | null = Nodes.JsonValue.Create value

            // A JSON null is a null node; everything else is copied, since a
            // node belongs to one parent.
            let copy (node: Nodes.JsonNode | null) : Nodes.JsonNode | null =
                match node with
                | null -> null
                | held -> held.DeepClone()

            let properties =
                [ for property in current -> property.Key, (if property.Key = key then pinned else copy property.Value) ]
                @ (if current.ContainsKey key then [] else [ key, pinned ])

            let rewritten =
                Nodes.JsonObject(properties |> List.map Collections.Generic.KeyValuePair<string, Nodes.JsonNode | null>)

            // Indented output uses the platform newline; files this tool
            // writes always use "\n". A raw CR cannot occur inside a JSON
            // string token, so this touches only layout.
            Rewritten(previous, rewritten.ToJsonString(editOptions).Replace("\r\n", "\n") + "\n")
    | _ -> NotAnObject
