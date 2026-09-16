/// Small, total HTML helpers.
///
/// Every value that reaches a page goes through `escape`. Content fragments
/// under `site/content/` are trusted authored HTML; everything derived from
/// `site/data/*.json` is escaped, because a manifest is data and data is never
/// markup.
module Ordo.Site.Html

open System

let escape (value: string) : string =
    value
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;")
        .Replace("'", "&#39;")

/// A URL-safe, lowercase identifier for use as an element id or path segment.
let slugify (value: string) : string =
    let characters =
        value
        |> Seq.map (fun ch ->
            if Char.IsLetterOrDigit ch then Char.ToLowerInvariant ch
            elif ch = '-' || ch = '_' then '-'
            else ' ')
        |> Seq.toArray

    System.String(characters).Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries)
    |> String.concat "-"

let private attribute (name: string, value: string) = " " + name + "=\"" + escape value + "\""

let private attributes (pairs: (string * string) list) =
    pairs |> List.map attribute |> String.concat ""

/// An element whose children are already-rendered HTML.
let element (tag: string) (pairs: (string * string) list) (children: string) : string =
    "<" + tag + attributes pairs + ">" + children + "</" + tag + ">"

/// An element whose text content must be escaped.
let text (tag: string) (pairs: (string * string) list) (content: string) : string =
    element tag pairs (escape content)

let void' (tag: string) (pairs: (string * string) list) : string = "<" + tag + attributes pairs + " />"

let join (parts: string list) : string = String.concat "\n" parts

let concat (parts: string list) : string = String.concat "" parts

/// Renders a list of already-rendered items as a `<ul>`, or nothing at all
/// when the list is empty — an empty section is never emitted as a heading
/// with nothing under it.
let unorderedList (className: string) (items: string list) : string =
    if List.isEmpty items then
        ""
    else
        element "ul" [ "class", className ] (items |> List.map (fun item -> element "li" [] item) |> concat)

let textList (className: string) (items: string list) : string =
    unorderedList className (items |> List.map escape)

/// A section that disappears entirely when it has no content, so a generated
/// page never shows an empty heading.
let sectionWhen (heading: string) (identifier: string) (body: string) : string =
    if body.Trim().Length = 0 then
        ""
    else
        join [ text "h2" [ "id", identifier ] heading; body ]

let paragraph (content: string) : string = text "p" [] content

/// A definition row used throughout the evidence pages.
let definitionRow (term: string) (definition: string) : string =
    text "dt" [] term + text "dd" [] definition

let definitionList (className: string) (rows: (string * string) list) : string =
    if List.isEmpty rows then
        ""
    else
        element "dl" [ "class", className ] (rows |> List.map (fun (term, value) -> definitionRow term value) |> concat)

/// Shortens a long hexadecimal SHA to its first twelve characters for display.
/// Anything that is not a long hex string — a short SHA, a tag name, a branch
/// name — is returned unchanged rather than truncated into something that no
/// longer resolves.
let shortSha (value: string) : string =
    if value.Length > 12 && value |> Seq.forall Uri.IsHexDigit then
        value.Substring(0, 12)
    else
        value
