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

let private makeConfig name tagline description baseUrl pathPrefix sourceRepository sourceUrl nav footerNote copyright : Config =
    { Name = name
      Tagline = tagline
      Description = description
      BaseUrl = baseUrl
      PathPrefix = pathPrefix
      SourceRepository = sourceRepository
      SourceUrl = sourceUrl
      Nav = nav
      FooterNote = footerNote
      Copyright = copyright }

let read (text: string) : Validation<Config> =
    match parse text with
    | Error message -> error ("site config is not valid JSON: " + message)
    | Ok(JObject _ as value) ->
        ok makeConfig
        <*> requiredString "name" value
        <*> requiredString "tagline" value
        <*> requiredString "description" value
        <*> requiredString "baseUrl" value
        <*> prefixString "pathPrefix" value
        <*> requiredString "sourceRepository" value
        <*> requiredString "sourceUrl" value
        <*> readNav value
        <*> requiredString "footerNote" value
        <*> requiredString "copyright" value
    | Ok _ -> error "site config must be a JSON object"
