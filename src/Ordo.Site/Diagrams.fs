/// Diagrams, as semantic HTML and one inline SVG.
///
/// Flow diagrams are ordered lists with CSS connectors rather than pictures:
/// a screen reader reads them as the sequence they are, they reflow on a
/// phone, they need no script, and they cost nothing to download. The single
/// SVG is the one place where geometry — a cycle between three states — is
/// genuinely part of the meaning.
module Ordo.Site.Diagrams

open Ordo.Site.Html

let private step (index: int) (label: string) (detail: string) =
    element
        "li"
        []
        (concat
            [ text "span" [ "class", "flow-index" ] (string index)
              element
                  "span"
                  [ "class", "flow-body" ]
                  (concat [ text "strong" [ "class", "flow-label" ] label; text "span" [ "class", "flow-detail" ] detail ]) ])

let private flow (caption: string) (steps: (string * string) list) =
    element
        "figure"
        [ "class", "diagram diagram-flow" ]
        (concat
            [ element "ol" [ "class", "flow" ] (steps |> List.mapi (fun i (label, detail) -> step (i + 1) label detail) |> concat)
              text "figcaption" [] caption ])

/// What actually happens when an actor asks a system under Ordo to do
/// something. Every gate is a place a request can be refused with a reason.
let transition () : string =
    flow
        "A requested action is checked against the current state before it can become a new state. Each gate refuses with a named reason rather than throwing or returning null."
        [ "Current state", "The named state the entity is actually in — not a combination of flags a reader has to infer."
          "Requested action", "A named action, not an arbitrary field write."
          "Capability check", "Is this action one the current state permits at all? If not, the request is refused here."
          "Evidence check", "Is the basis for the transition present and valid?"
          "Policy check", "Do the business rules covering this action hold for these particular values?"
          "Legal transition", "Only now does state change, and only to a state the model declares reachable from the current one."
          "New state, obligations, effects", "The resulting state, what must still happen for the transition to be complete, and the external effects requested as data rather than performed in place." ]

/// The order work happens in, from requirement to recorded evidence.
let engineering () : string =
    flow
        "The construction order Ordo prescribes. Verification is not a phase at the end; it is several mechanisms with different sensitivities, applied in order of how early and how cheaply each can refuse a mistake."
        [ "Requirements", "Numbered, with acceptance criteria that can each be traced to a test."
          "Semantic model", "The domain's states, and what each one means."
          "State and legal transitions", "Which changes the model permits, and from where."
          "Capabilities and obligations", "What an actor may do next, and what is still owed."
          "Implementation", "Written at the semantic authority first, then propagated outward."
          "Mechanical verification", "Compiler, architecture checks, contract checks — everything a machine can refuse without running the program."
          "Heterogeneous verification", "Behavioural tests, integration against real boundaries, adversarial review — mechanisms whose blind spots differ from each other's."
          "Evidence", "What ran, what it found, and what it does not establish, recorded durably." ]

/// Which failure classes each verification mechanism can and cannot refuse.
/// The right-hand column is the argument: no single mechanism covers the set.
let verification () : string =
    let rows =
        [ ("Type and exhaustiveness checking",
           "Unrepresentable states, unhandled cases, signature drift",
           "Code that type-checks and does the wrong thing")
          ("Architecture and dependency checks",
           "Layering violations, inverted dependencies",
           "Correct layering around incorrect behaviour")
          ("Contract and boundary checks",
           "Representation collapse at a declared boundary",
           "Boundaries nobody declared")
          ("Behavioural tests",
           "Behaviour the author thought to assert",
           "A check that passes for the wrong reason")
          ("Integration against a real boundary",
           "Present-but-inert implementations, wire-format mismatches",
           "Conditions the integration environment never produces")
          ("Independent adversarial review",
           "Declared-but-unreachable outcomes, vacuous assertions",
           "Anything the reviewer also fails to imagine") ]

    let renderRow (mechanism: string, catches: string, misses: string) =
        element
            "tr"
            []
            (concat
                [ text "th" [ "scope", "row" ] mechanism
                  text "td" [] catches
                  text "td" [ "class", "misses" ] misses ])

    let head =
        element
            "tr"
            []
            (concat
                [ text "th" [ "scope", "col" ] "Mechanism"
                  text "th" [ "scope", "col" ] "Refuses"
                  text "th" [ "scope", "col" ] "Cannot refuse" ])

    element
        "figure"
        [ "class", "diagram diagram-table" ]
        (concat
            [ element
                  "table"
                  [ "class", "matrix" ]
                  (concat
                      [ text "caption" [] "Verification mechanisms and their blind spots"
                        element "thead" [] head
                        element "tbody" [] (rows |> List.map renderRow |> concat) ])
              text
                  "figcaption"
                  []
                  "Each row's right-hand column is what some later mechanism has to cover. This is why Ordo asks for several mechanisms with different sensitivities rather than a higher number from one." ])

/// The one diagram where geometry carries meaning: a three-state lifecycle in
/// which one state is terminal and one transition is reversible.
let lifecycle () : string =
    """<figure class="diagram diagram-svg">
<svg viewBox="0 0 640 260" role="img" aria-labelledby="lifecycle-title lifecycle-desc" class="lifecycle">
  <title id="lifecycle-title">Time entry lifecycle: three states and four legal transitions</title>
  <desc id="lifecycle-desc">A time entry is created into the Recorded state. Recorded can move to Voided, and Voided back to Recorded. Recorded can move to Superseded when the entry is split or merged. Superseded is terminal: no transition leaves it.</desc>
  <defs>
    <marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
      <path d="M 0 0 L 10 5 L 0 10 z" fill="currentColor" />
    </marker>
  </defs>
  <g class="lifecycle-edges" fill="none" stroke="currentColor" stroke-width="1.5" marker-end="url(#arrow)">
    <path d="M 44 70 L 96 70" />
    <path d="M 232 56 L 372 56" />
    <path d="M 372 92 L 232 92" />
    <path d="M 166 118 L 166 178" />
  </g>
  <g class="lifecycle-labels">
    <text x="20" y="58">create</text>
    <text x="252" y="44">void</text>
    <text x="252" y="112">restore</text>
    <text x="178" y="154">split or merge</text>
  </g>
  <g class="lifecycle-nodes">
    <rect x="100" y="42" width="132" height="56" />
    <text x="166" y="76" text-anchor="middle">Recorded</text>
    <rect x="376" y="42" width="132" height="56" />
    <text x="442" y="76" text-anchor="middle">Voided</text>
    <rect x="100" y="182" width="132" height="56" class="terminal" />
    <text x="166" y="209" text-anchor="middle">Superseded</text>
    <text x="166" y="226" text-anchor="middle" class="terminal-note">terminal</text>
  </g>
</svg>
<figcaption>The lifecycle implemented independently by three separate builds of the same requirements. Nothing leaves <em>Superseded</em>: every guard refuses it, and two acceptance criteria assert the refusal rather than relying on its absence from the code.</figcaption>
</figure>"""
