/// Assembles the whole site in memory, validates it, and only then writes it.
///
/// The order matters. Nothing reaches `dist/` until every check in
/// `Ordo.Site.Validate` has passed against the exact bytes that would be
/// written, so a broken build leaves the previous output untouched rather than
/// half-replaced.
module Ordo.Site.Build

open System.IO
open Ordo.Site.Validation
open Ordo.Site.Content
open Ordo.Site.Model
open Ordo.Site.SiteConfig
open Ordo.Site.Validate

type Layout =
    { /// Directory holding `site.json` and `evidence.json`.
      DataDirectory: string
      ContentDirectory: string
      /// Optional per-experiment narrative fragments, named `<slug>.html`.
      NarrativeDirectory: string
      AssetDirectory: string
      OutputDirectory: string }

let layoutFor (siteRoot: string) (outputDirectory: string) : Layout =
    { DataDirectory = Path.Combine(siteRoot, "data")
      ContentDirectory = Path.Combine(siteRoot, "content")
      NarrativeDirectory = Path.Combine(siteRoot, "narratives")
      AssetDirectory = Path.Combine(siteRoot, "assets")
      OutputDirectory = outputDirectory }

let private readFile (path: string) : Validation<string> =
    if File.Exists path then
        ok (File.ReadAllText path)
    else
        error ("required file is missing: " + path)

/// `/concepts/state/` becomes `concepts/state/index.html`; `/` becomes
/// `index.html`.
let outputPathFor (sitePath: string) : string =
    let trimmed = sitePath.TrimStart '/'
    trimmed + "index.html"

/// A generated page record for an experiment, so experiment pages appear in
/// navigation, the page index and llms.txt exactly like authored pages do.
let private experimentPageRecord (experiment: Experiment) (order: int) : Page =
    { Path = "/evidence/" + experiment.Slug + "/"
      Title = experiment.Title
      Description = experiment.Summary
      Section = "Evidence"
      Order = 100 + order
      Summary = experiment.Summary
      Body = ""
      SourceFile = "generated from site/data/evidence.json" }

let private narrativeFor (layout: Layout) (experiment: Experiment) : string =
    let path = Path.Combine(layout.NarrativeDirectory, experiment.Slug + ".html")
    if File.Exists path then File.ReadAllText path else ""

let private assetFiles (layout: Layout) : (string * string) list =
    if not (Directory.Exists layout.AssetDirectory) then
        []
    else
        Directory.GetFiles(layout.AssetDirectory, "*", SearchOption.AllDirectories)
        |> Array.sortWith (fun left right -> System.String.CompareOrdinal(left, right))
        |> Array.toList
        |> List.map (fun file ->
            let relative = Path.GetRelativePath(layout.AssetDirectory, file).Replace('\\', '/')
            "assets/" + relative, file)

/// Builds every output file. Returns the outputs and the asset files they are
/// allowed to link to.
let assemble (layout: Layout) : Validation<Output list * (string * string) list> =
    let inputs =
        ok (fun config manifest authored -> (config, manifest, authored))
        <*> (readFile (Path.Combine(layout.DataDirectory, "site.json")) |> bind SiteConfig.read)
        <*> (readFile (Path.Combine(layout.DataDirectory, "evidence.json")) |> bind ManifestReader.read)
        <*> Content.readAll layout.ContentDirectory

    inputs
    |> bind (fun (config, manifest, authored) ->
        let experimentPages =
            manifest.Experiments
            |> List.mapi (fun index experiment -> experiment, experimentPageRecord experiment index)

        let allPages =
            authored @ (experimentPages |> List.map snd)
            |> List.sortWith (fun left right -> System.String.CompareOrdinal(left.Path, right.Path))

        let context: Render.Context =
            { Config = config
              Manifest = manifest
              Pages = allPages }

        let resolve = Render.resolveToken context

        let renderAuthored (page: Page) : Validation<Output> =
            Content.substitute resolve page.Body
            |> withContext page.SourceFile
            |> map (fun body ->
                { Path = outputPathFor page.Path
                  Text = Render.document context page body })

        let renderExperiment (experiment: Experiment, page: Page) : Validation<Output> =
            Content.substitute resolve (narrativeFor layout experiment)
            |> withContext ("narrative for " + experiment.Slug)
            |> map (fun narrative ->
                { Path = outputPathFor page.Path
                  Text = Render.document context page (Render.experimentPage context experiment narrative) })

        let machine =
            [ { Path = "data/evidence.json"
                Text = Machine.evidenceDocument config manifest }
              { Path = "data/glossary.json"
                Text = Machine.glossaryDocument manifest }
              { Path = "data/index.json"
                Text = Machine.indexDocument config allPages }
              { Path = "llms.txt"
                Text = Machine.llmsText config allPages manifest }
              { Path = "sitemap.xml"
                Text = Machine.sitemap config allPages }
              { Path = "robots.txt"
                Text = Machine.robots config } ]

        let rendered =
            ok (fun authoredOutputs experimentOutputs -> authoredOutputs @ experimentOutputs @ machine)
            <*> traverse renderAuthored authored
            <*> traverse renderExperiment experimentPages

        rendered
        |> bind (fun produced ->
            let outputs =
                produced |> List.sortWith (fun left right -> System.String.CompareOrdinal(left.Path, right.Path))

            let assets = assetFiles layout
            let assetPaths = assets |> List.map fst

            Validate.run config outputs assetPaths |> map (fun _ -> (outputs, assets))))

/// `Path.GetDirectoryName` returns null for a rootless path, so the result is
/// narrowed through an option rather than tested for emptiness — the compiler
/// then knows the directory is a real one at the point it is created.
let private ensureParentDirectory (destination: string) : unit =
    match Option.ofObj (Path.GetDirectoryName destination) with
    | Some directory when directory.Length > 0 -> Directory.CreateDirectory directory |> ignore
    | _ -> ()

let private destinationFor (root: string) (relative: string) : string =
    Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))

let private writeText (root: string) (output: Output) =
    let destination = destinationFor root output.Path
    ensureParentDirectory destination
    File.WriteAllText(destination, output.Text)

let private copyAsset (root: string) (relative: string, sourceFile: string) =
    let destination = destinationFor root relative
    ensureParentDirectory destination
    File.Copy(sourceFile, destination, true)

/// Writes a validated site. `.nojekyll` is written because GitHub Pages
/// otherwise runs Jekyll over the artifact, which silently drops files and
/// directories whose names begin with an underscore.
let write (layout: Layout) (outputs: Output list) (assets: (string * string) list) : unit =
    if Directory.Exists layout.OutputDirectory then
        Directory.Delete(layout.OutputDirectory, true)

    Directory.CreateDirectory layout.OutputDirectory |> ignore
    outputs |> List.iter (writeText layout.OutputDirectory)
    assets |> List.iter (copyAsset layout.OutputDirectory)
    File.WriteAllText(Path.Combine(layout.OutputDirectory, ".nojekyll"), "")
