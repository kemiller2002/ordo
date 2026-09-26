/// The dependency boundaries, checked by the loader rather than by review.
///
/// Every one of these is a rule stated elsewhere in prose. Prose does not
/// fail a build (ORDO-3704 / ORDO-8601 / ORDO-8602 / ORDO-8603).
module Ordo.Tests.ArchitectureTests

open System.Reflection
open Xunit

let private core = typeof<Ordo.Core.Json.JsonValue>.Assembly
let private decisions = typeof<Ordo.Decisions.Outcome.ProviderError>.Assembly
let private adapter = typeof<Ordo.Providers.Anthropic.Adapter.AnthropicOptions>.Assembly

/// An assembly name's `Name` is nullable, so it is narrowed here rather than
/// at each of the call sites below.
let private nameOf (name: AssemblyName) =
    Option.ofObj name.Name |> Option.defaultValue "<unnamed>"

let private references (assembly: Assembly) =
    assembly.GetReferencedAssemblies() |> Array.map nameOf |> Set.ofArray

let private assertNoReferenceMatching (predicate: string -> bool) (description: string) (assembly: Assembly) =
    let offending = references assembly |> Set.filter predicate

    Assert.True(
        Set.isEmpty offending,
        sprintf "%s must not reference %s, but references %A" (nameOf (assembly.GetName())) description (Set.toList offending)
    )

[<Fact>]
let ``Ordo.Core references only the framework and FSharp.Core`` () =
    let permitted =
        set
            [ "FSharp.Core"
              "System.Runtime"
              "System.Private.CoreLib"
              "netstandard"
              "mscorlib"
              "System.Collections"
              "System.Memory"
              "System.Text.Json"
              "System.Security.Cryptography"
              // Framework regular expressions for the praxis.provenance/1
              // codec (`Provenance.fs`), whose key, timestamp and credential
              // grammars are pinned to the Praxis reference patterns
              // (ORDO-PROV-06 / DF-SDE-2026-0016).
              "System.Text.RegularExpressions"
              "System.Runtime.Extensions"
              "System.Text.Encoding.Extensions" ]

    let unexpected = Set.difference (references core) permitted

    Assert.True(
        Set.isEmpty unexpected,
        sprintf "Ordo.Core gained dependencies that were not justified: %A" (Set.toList unexpected)
    )

[<Fact>]
let ``Ordo.Core depends on no provider, no adapter and no repository operating system`` () =
    core
    |> assertNoReferenceMatching (fun name -> name.StartsWith "Anthropic") "a model-provider SDK"

    core
    |> assertNoReferenceMatching (fun name -> name.StartsWith "Ordo.Providers") "a provider adapter"

    core
    |> assertNoReferenceMatching (fun name -> name.StartsWith "Ros" || name.StartsWith "Sde") "the repository operating system"

[<Fact>]
let ``Ordo.Decisions depends inward only`` () =
    Assert.Contains("Ordo.Core", references decisions)

    decisions
    |> assertNoReferenceMatching (fun name -> name.StartsWith "Anthropic") "a concrete provider"

    decisions
    |> assertNoReferenceMatching (fun name -> name.StartsWith "Ordo.Providers") "a provider adapter"

    decisions
    |> assertNoReferenceMatching (fun name -> name.StartsWith "Ros" || name.StartsWith "Sde") "the repository operating system"

[<Fact>]
let ``the provider adapter depends on the abstraction, never the reverse`` () =
    Assert.Contains("Ordo.Decisions", references adapter)
    Assert.Contains("Anthropic", references adapter)
    Assert.DoesNotContain("Ordo.Providers.Anthropic", references decisions)
    Assert.DoesNotContain("Ordo.Providers.Anthropic", references core)

[<Fact>]
let ``no production assembly depends on a test package`` () =
    for assembly in [ core; decisions; adapter ] do
        assembly
        |> assertNoReferenceMatching (fun name -> name.StartsWith "xunit" || name.Contains "Test") "a test package"

[<Fact>]
let ``the dependency graph is acyclic and shallow`` () =
    Assert.DoesNotContain("Ordo.Decisions", references core)
    Assert.DoesNotContain("Ordo.Core", references core)
