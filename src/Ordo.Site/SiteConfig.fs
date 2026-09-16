/// Site-wide settings: one authority for the name, the description, the
/// canonical base URL, and the navigation. Nothing in the renderer hard-codes
/// a menu item, so adding a page to the navigation is a data change.
module Ordo.Site.SiteConfig

open Sde.Core.Json
open Ordo.Site.Validation

type NavItem = { Label: string; Path: string }

type Config =
    { Name: string
      Tagline: string
      Description: string
      /// Canonical origin, no trailing slash, e.g. "https://example.github.io/ordo".
      BaseUrl: string
      /// Path prefix every generated link is written under, "" for a site at
      /// the domain root and "/repo-name" for a GitHub project page.
      PathPrefix: string
      /// The custom domain this site is served from, when it has one. A site
      /// on a custom domain is served from that domain's root, so it also
      /// fixes `PathPrefix` to "" — see `checkDomainAgreement`.
      CustomDomain: string option
      /// The repository this site is generated from, for "view source" links.
      SourceRepository: string
      SourceUrl: string
      Nav: NavItem list
      FooterNote: string
      /// Fixed copyright line. Deliberately not derived from the clock: a
      /// generator that reads the current date does not build deterministically.
      Copyright: string }

let private isBlank (value: string) = value.Trim().Length = 0

let private requiredString (name: string) (value: JsonValue) : Validation<string> =
    match tryString (tryField name value) with
    | Some text when not (isBlank text) -> ok text
    | Some _ -> error ("site config field '" + name + "' is empty")
    | None -> error ("site config field '" + name + "' is missing")

/// Allowed to be empty: a site served from a domain root has no prefix.
let private prefixString (name: string) (value: JsonValue) : Validation<string> =
    match tryString (tryField name value) with
    | Some text when isBlank text -> ok ""
    | Some text when text.StartsWith "/" && not (text.EndsWith "/") -> ok text
    | Some text -> error ("site config field '" + name + "' must be empty or start with '/' and not end with '/' — got '" + text + "'")
    | None -> ok ""

let private optionalString (name: string) (value: JsonValue) : Validation<string option> =
    match tryField name value with
    | None -> ok None
    | Some JNull -> ok None
    | Some(JString text) when isBlank text -> ok None
    | Some(JString text) -> ok (Some text)
    | Some _ -> error ("site config field '" + name + "' must be a string or absent")

let private makeNavItem label path : NavItem = { Label = label; Path = path }

let private readNav (value: JsonValue) : Validation<NavItem list> =
    match tryField "nav" value with
    | Some(JArray items) ->
        items
        |> traverse (fun item ->
            match item with
            | JObject _ -> ok makeNavItem <*> requiredString "label" item <*> requiredString "path" item
            | _ -> error "site config 'nav' must contain only objects")
    | Some _ -> error "site config 'nav' must be an array"
    | None -> error "site config 'nav' is missing"

let private makeConfig name tagline description baseUrl pathPrefix customDomain sourceRepository sourceUrl nav footerNote copyright : Config =
    { Name = name
      Tagline = tagline
      Description = description
      BaseUrl = baseUrl
      PathPrefix = pathPrefix
      CustomDomain = customDomain
      SourceRepository = sourceRepository
      SourceUrl = sourceUrl
      Nav = nav
      FooterNote = footerNote
      Copyright = copyright }

/// A custom domain and the canonical base URL are two statements of the same
/// fact, and a custom domain also decides the path prefix. Rather than a
/// comment asking the next editor to keep three fields in step, they get a
/// mechanical agreement check — which is the rule this site's own boundary
/// doctrine states, applied to the site's own configuration.
///
/// This exists because the first deployment got it wrong: the site was
/// generated for a project-page path and served from a custom domain's root,
/// so every internal link and the stylesheet resolved to nothing.
let private checkDomainAgreement (config: Config) : Validation<Config> =
    match config.CustomDomain with
    | None -> ok config
    | Some domain ->
        let expected = "https://" + domain

        let baseProblem =
            if config.BaseUrl = expected then
                []
            else
                [ "site config 'baseUrl' must be '"
                  + expected
                  + "' while 'customDomain' is '"
                  + domain
                  + "', got '"
                  + config.BaseUrl
                  + "'" ]

        let prefixProblem =
            if config.PathPrefix = "" then
                []
            else
                [ "site config 'pathPrefix' must be empty while 'customDomain' is set, because a custom domain serves the site from its root, got '"
                  + config.PathPrefix
                  + "'" ]

        match baseProblem @ prefixProblem with
        | [] -> ok config
        | problems -> Error problems

let read (text: string) : Validation<Config> =
    let fields (value: JsonValue) : Validation<Config> =
        ok makeConfig
        <*> requiredString "name" value
        <*> requiredString "tagline" value
        <*> requiredString "description" value
        <*> requiredString "baseUrl" value
        <*> prefixString "pathPrefix" value
        <*> optionalString "customDomain" value
        <*> requiredString "sourceRepository" value
        <*> requiredString "sourceUrl" value
        <*> readNav value
        <*> requiredString "footerNote" value
        <*> requiredString "copyright" value

    match parse text with
    | Error message -> error ("site config is not valid JSON: " + message)
    | Ok(JObject _ as value) -> fields value |> bind checkDomainAgreement
    | Ok _ -> error "site config must be a JSON object"
