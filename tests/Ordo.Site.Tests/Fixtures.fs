/// Locates the real site sources, so the suite exercises the site that would
/// actually be published rather than a synthetic stand-in.
module Ordo.Site.Tests.Fixtures

open System
open System.IO

let repositoryRoot =
    let rec search (directory: DirectoryInfo option) =
        match directory with
        | None -> None
        | Some current ->
            if File.Exists(Path.Combine(current.FullName, "ros.json")) then
                Some current.FullName
            else
                search (Option.ofObj current.Parent)

    match search (Some(DirectoryInfo AppContext.BaseDirectory)) with
    | Some directory -> directory
    | None -> failwith "could not locate the repository root (no ros.json found above the test assembly)"

let siteRoot = Path.Combine(repositoryRoot, "site")

let layout = Ordo.Site.Build.layoutFor siteRoot (Path.Combine(Path.GetTempPath(), "ordo-site-tests-unused"))

/// A minimal manifest used by the parser tests. Every negative test below is a
/// single-field edit of this document, so a failure names one cause.
let validManifest =
    """{
  "schemaVersion": "1.0.0",
  "repositories": [
    { "id": "repo", "name": "owner/repo", "url": "https://example.invalid/owner/repo",
      "visibility": "public", "role": "evidence" }
  ],
  "experiments": [
    { "id": "exp", "slug": "exp", "title": "An experiment", "status": "Complete",
      "summary": "A summary.", "repository": "repo", "objective": "An objective.",
      "method": "A method.", "limitations": ["Only one trial."],
      "sources": [ { "repository": "repo", "path": "report.md" } ] }
  ],
  "metrics": [
    { "id": "M-1", "label": "Tests passing", "value": "59 / 59",
      "definition": "Tests that passed.", "measurement": "Test runner output.",
      "experiment": "exp", "provenance": "instrumented",
      "limitation": "Says nothing about what was not tested.",
      "source": { "repository": "repo", "path": "tests.json" } }
  ],
  "claims": [
    { "id": "C-1", "proposition": "A proposition.", "confidence": "recommended",
      "state": "supported", "records": ["EV-1"], "limitation": "One codebase only.",
      "source": { "repository": "repo", "path": "map.md" } }
  ],
  "glossary": [
    { "term": "State", "definition": "A named thing that can be true.", "origin": "Paradigm" }
  ]
}"""

let errorsOf (result: Result<'a, string list>) : string list =
    match result with
    | Ok _ -> []
    | Error messages -> messages

let valueOf (result: Result<'a, string list>) : 'a =
    match result with
    | Ok value -> value
    | Error messages -> failwith ("expected success, got: " + String.Join("; ", messages))
