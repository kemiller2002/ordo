/// Authored content: front matter, page records, and token substitution.
///
/// A content file is semantic HTML with a small front-matter header. There is
/// no Markdown step and no template language beyond `{{token}}`, because the
/// only thing templating has to do here is guarantee that a number in prose
/// came from the evidence manifest rather than from an author's memory.
module Ordo.Site.Content

open System
open System.IO
open Ordo.Site.Validation

type Page =
    { /// Site-absolute path ending in "/", e.g. "/concepts/state/".
      Path: string
      Title: string
      Description: string
      /// Navigation grouping used by indexes and the machine-readable index.
      Section: string
      Order: int
      /// One sentence, reused in indexes, llms.txt and the JSON index.
      Summary: string
      /// Authored HTML with tokens still unresolved.
      Body: string
      /// Source file, for error messages.
      SourceFile: string }

let private normalize (text: string) = text.Replace("\r\n", "\n")

let private splitOnce (separator: char) (line: string) : (string * string) option =
    let index = line.IndexOf separator

    if index < 0 then
        None
    else
        Some(line.Substring(0, index).Trim(), line.Substring(index + 1).Trim())

let private readPairs (file: string) (header: string list) : Validation<(string * string) list> =
    header
    |> List.filter (fun line -> line.Trim().Length > 0)
    |> traverse (fun line ->
        match splitOnce ':' line with
        | Some(key, value) when key.Length > 0 -> ok (key, value)
        | _ -> error (file + ": front matter line is not 'key: value' — " + line))

let private lookup (file: string) (pairs: (string * string) list) (key: string) : Validation<string> =
    match pairs |> List.tryFind (fun (name, _) -> name = key) with
    | Some(_, value) when value.Length > 0 -> ok value
    | Some _ -> error (file + ": front matter key '" + key + "' is empty")
    | None -> error (file + ": front matter key '" + key + "' is missing")

let private lookupInt (file: string) (pairs: (string * string) list) (key: string) : Validation<int> =
    lookup file pairs key
    |> bind (fun value ->
        match Int32.TryParse(value, Globalization.NumberStyles.Integer, Globalization.CultureInfo.InvariantCulture) with
        | true, number -> ok number
        | _ -> error (file + ": front matter key '" + key + "' must be an integer, got '" + value + "'"))

let private makePage sourceFile path title description section order summary body : Page =
    { Path = path
      Title = title
      Description = description
      Section = section
      Order = order
      Summary = summary
      Body = body
      SourceFile = sourceFile }

/// A site path is required to be absolute and directory-shaped, so every link
/// in the site can be compared as a plain string without normalisation rules
/// scattered across the renderer.
let private checkPath (file: string) (path: string) : Validation<string> =
    if not (path.StartsWith "/") then
        error (file + ": 'path' must start with '/' — got '" + path + "'")
    elif not (path.EndsWith "/") then
        error (file + ": 'path' must end with '/' — got '" + path + "'")
    else
        ok path

let parseDocument (file: string) (text: string) : Validation<Page> =
    let lines = (normalize text).Split '\n' |> Array.toList

    match lines with
    | first :: rest when first.Trim() = "---" ->
        let header = rest |> List.takeWhile (fun line -> line.Trim() <> "---")
        let remainder = rest |> List.skipWhile (fun line -> line.Trim() <> "---")

        match remainder with
        | _ :: body ->
            let bodyText = body |> String.concat "\n"

            readPairs file header
            |> bind (fun pairs ->
                ok (makePage file)
                <*> (lookup file pairs "path" |> bind (checkPath file))
                <*> lookup file pairs "title"
                <*> lookup file pairs "description"
                <*> lookup file pairs "section"
                <*> lookupInt file pairs "order"
                <*> lookup file pairs "summary"
                <*> ok bodyText)
        | [] -> error (file + ": front matter block is never closed with '---'")
    | _ -> error (file + ": a content file must begin with a '---' front matter block")

/// Reads every content file under a root, in a stable order so that two builds
/// of the same tree produce byte-identical output.
let readAll (root: string) : Validation<Page list> =
    if not (Directory.Exists root) then
        error ("content directory does not exist: " + root)
    else
        Directory.GetFiles(root, "*.html", SearchOption.AllDirectories)
        |> Array.sortWith (fun left right -> System.String.CompareOrdinal(left, right))
        |> Array.toList
        |> traverse (fun file ->
            let relative = Path.GetRelativePath(root, file).Replace('\\', '/')
            parseDocument relative (File.ReadAllText file))

// ---------------------------------------------------------------------------
// Tokens
// ---------------------------------------------------------------------------

/// Replaces every `{{token}}` using `resolve`, accumulating every failure.
///
/// An unresolvable token is a build failure rather than a passthrough: the
/// whole point of routing metrics through tokens is that a stale or invented
/// reference cannot be published.
let substitute (resolve: string -> Validation<string>) (source: string) : Validation<string> =
    let rec go (index: int) (acc: string list) (errors: string list) : Validation<string> =
        let start = source.IndexOf("{{", index, StringComparison.Ordinal)

        if start < 0 then
            let parts = source.Substring index :: acc |> List.rev

            if List.isEmpty errors then
                ok (String.concat "" parts)
            else
                Error errors
        else
            let finish = source.IndexOf("}}", start + 2, StringComparison.Ordinal)

            if finish < 0 then
                Error(errors @ [ "unterminated '{{' token" ])
            else
                let literal = source.Substring(index, start - index)
                let token = source.Substring(start + 2, finish - start - 2).Trim()

                match resolve token with
                | Ok html -> go (finish + 2) (html :: literal :: acc) errors
                | Error messages -> go (finish + 2) (literal :: acc) (errors @ messages)

    go 0 [] []
