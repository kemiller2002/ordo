/// Integration tests against the site that would actually be published.
///
/// These do not use a fixture site. If `site/` cannot be assembled, or if it
/// assembles into something whose numbers are not traceable, these fail.
module Ordo.Site.Tests.SiteTests

open System.IO
open Xunit
open Ordo.Site
open Ordo.Site.Model
open Ordo.Site.Tests.Fixtures

let private assembled = lazy (Build.assemble layout)

let private outputs () =
    let result, _ = valueOf assembled.Value
    result

let private manifest () =
    ManifestReader.read (File.ReadAllText(Path.Combine(siteRoot, "data", "evidence.json"))) |> valueOf

let private find (path: string) =
    outputs () |> List.tryFind (fun output -> output.Path = path)

[<Fact>]
let ``the real site assembles and passes every check`` () =
    match assembled.Value with
    | Ok(files, _) -> Assert.NotEmpty(files)
    | Error messages -> failwith ("site/ does not build:\n  " + System.String.Join("\n  ", messages))

/// Two builds of the same tree must produce identical bytes. A generator that
/// embeds a clock reading cannot be used to prove that a deployed page matches
/// the repository it claims to come from.
[<Fact>]
let ``the build is deterministic`` () =
    let first, _ = valueOf (Build.assemble layout)
    let second, _ = valueOf (Build.assemble layout)

    Assert.Equal(List.length first, List.length second)

    List.zip first second
    |> List.iter (fun (left, right) ->
        Assert.Equal(left.Path, right.Path)
        Assert.Equal(left.Text, right.Text))

[<Fact>]
let ``every experiment has a generated page`` () =
    let generated = outputs () |> List.map (fun output -> output.Path) |> Set.ofList

    (manifest ()).Experiments
    |> List.iter (fun experiment ->
        let expected = "evidence/" + experiment.Slug + "/index.html"
        Assert.True(Set.contains expected generated, expected + " was not generated"))

/// The publication rule, checked against the rendered page rather than the
/// manifest: a number reaches a reader together with what it does not show.
[<Fact>]
let ``every metric is rendered with its limitation and a source link`` () =
    let model = manifest ()

    model.Metrics
    |> List.iter (fun metric ->
        let experiment =
            match tryFindExperiment model metric.Experiment with
            | Some value -> value
            | None -> failwith ("metric " + metric.Id + " has no experiment")

        match find ("evidence/" + experiment.Slug + "/index.html") with
        | None -> failwith ("no page for experiment " + experiment.Id)
        | Some page ->
            Assert.Contains("id=\"" + metric.Id + "\"", page.Text)
            Assert.Contains(Html.escape metric.Limitation, page.Text)
            Assert.Contains("class=\"src\"", page.Text))

[<Fact>]
let ``every experiment page states at least one limitation`` () =
    (manifest ()).Experiments
    |> List.iter (fun experiment ->
        match find ("evidence/" + experiment.Slug + "/index.html") with
        | None -> failwith ("no page for " + experiment.Id)
        | Some page -> Assert.Contains("id=\"limitations\"", page.Text))

[<Fact>]
let ``the published evidence manifest lists every metric the site holds`` () =
    let model = manifest ()

    match find "data/evidence.json" with
    | None -> failwith "data/evidence.json was not generated"
    | Some published ->
        model.Metrics
        |> List.iter (fun metric -> Assert.Contains("\"" + metric.Id + "\"", published.Text))

[<Fact>]
let ``llms.txt routes to every experiment`` () =
    let model = manifest ()

    match find "llms.txt" with
    | None -> failwith "llms.txt was not generated"
    | Some published ->
        model.Experiments
        |> List.iter (fun experiment -> Assert.Contains("/evidence/" + experiment.Slug + "/", published.Text))

/// A site arguing for transparency about refuted results has to publish them.
[<Fact>]
let ``the research page publishes contradicted propositions`` () =
    let model = manifest ()
    let contradicted = model.Claims |> List.filter (fun claim -> claim.State = Contradicted)

    Assert.NotEmpty(contradicted)

    match find "research/index.html" with
    | None -> failwith "research/index.html was not generated"
    | Some page -> contradicted |> List.iter (fun claim -> Assert.Contains(claim.Id, page.Text))

[<Fact>]
let ``the evidence base cites more than one repository`` () =
    Assert.True(List.length (manifest ()).Repositories > 1)
