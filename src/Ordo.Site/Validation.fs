/// Error-accumulating validation.
///
/// The site build never stops at the first bad field. A content author or an
/// agent editing `site/data/evidence.json` should see every problem in one
/// run, so parsing is applicative rather than monadic: independent fields are
/// validated independently and their errors concatenated.
module Ordo.Site.Validation

/// A value that is either present, or absent with every reason it is absent.
type Validation<'a> = Result<'a, string list>

let ok (value: 'a) : Validation<'a> = Ok value

let error (message: string) : Validation<'a> = Error [ message ]

let map (f: 'a -> 'b) (value: Validation<'a>) : Validation<'b> =
    match value with
    | Ok a -> Ok(f a)
    | Error errors -> Error errors

let bind (f: 'a -> Validation<'b>) (value: Validation<'a>) : Validation<'b> =
    match value with
    | Ok a -> f a
    | Error errors -> Error errors

/// Applicative application. Both sides are evaluated, so two independent bad
/// fields produce two messages rather than hiding one behind the other.
let apply (f: Validation<'a -> 'b>) (value: Validation<'a>) : Validation<'b> =
    match f, value with
    | Ok g, Ok a -> Ok(g a)
    | Error left, Error right -> Error(left @ right)
    | Error left, Ok _ -> Error left
    | Ok _, Error right -> Error right

let (<*>) (f: Validation<'a -> 'b>) (value: Validation<'a>) : Validation<'b> = apply f value

/// Turns a list of independently-validated values into a validated list,
/// keeping every error rather than the first.
let sequence (items: Validation<'a> list) : Validation<'a list> =
    let folder (item: Validation<'a>) (acc: Validation<'a list>) =
        match item, acc with
        | Ok value, Ok rest -> Ok(value :: rest)
        | Error left, Error right -> Error(left @ right)
        | Error left, Ok _ -> Error left
        | Ok _, Error right -> Error right

    List.foldBack folder items (Ok [])

let traverse (f: 'a -> Validation<'b>) (items: 'a list) : Validation<'b list> =
    items |> List.map f |> sequence

/// Adds a context prefix to every message, so a failure names which record it
/// came from without each individual check having to repeat it.
let withContext (context: string) (value: Validation<'a>) : Validation<'a> =
    match value with
    | Ok a -> Ok a
    | Error errors -> Error(errors |> List.map (fun message -> context + ": " + message))
