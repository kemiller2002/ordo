/// The one file in this library that talks to a network.
///
/// Its whole job is translation: an Ordo `ProviderRequest` into a Messages
/// API call constrained to a single tool, and the reply — or the failure —
/// back into an Ordo `ProviderResponse`. No provider type escapes this
/// module, which is what lets the same decision run against the fake
/// provider, this one, or a future one without the contract changing
/// (ORDO-3-242 / ORDO-4303).
///
/// Retries here are transport retries only. A response that breaks the
/// contract is returned as a typed failure and never re-asked: asking again
/// until the answer is acceptable is prohibited (ORDO-6702 / ORDO-3-149).
module Ordo.Providers.Anthropic.Adapter

open System
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Anthropic
open Anthropic.Exceptions
open Anthropic.Models.Messages
open Ordo.Core.Json
open Ordo.Core.Identifiers
open Ordo.Decisions.Outcome
open Ordo.Decisions.Provider

/// How to reach the provider, and which model to ask.
///
/// `Model` has no default. A recorded decision must name the system that
/// made it, and a floating alias makes a historical record unreproducible,
/// so the choice is the caller's to state (ORDO-10504 / ORDO-4301).
type AnthropicOptions =
    { Model: string
      MaxTokens: int
      /// How long one attempt may take before it is abandoned. Surfaces as a
      /// typed `ProviderTimeout`, not as a cancellation (ORDO-6603).
      Timeout: TimeSpan
      /// How many times a transient transport failure may be re-sent.
      TransportRetries: int }

[<RequireQualifiedAccess>]
module AnthropicOptions =

    /// Options for a named model, with limits a caller can see and change.
    let forModel (model: string) =
        { Model = model
          MaxTokens = 16000
          Timeout = TimeSpan.FromMinutes 2.0
          TransportRetries = 2 }

let private toJsonElement (value: JsonValue) : JsonElement =
    // Parsed rather than deserialized: `Deserialize<JsonElement>` is typed as
    // returning a nullable, which a schema fragment never is. `Clone` detaches
    // the element from the document before the document is disposed.
    use document = JsonDocument.Parse(render value)
    document.RootElement.Clone()

let private schemaProperties (request: ProviderRequest) =
    match Contract.toolInputSchema request with
    | JObject members ->
        let properties =
            members
            |> List.tryPick (function
                | "properties", JObject properties -> Some properties
                | _ -> None)
            |> Option.defaultValue []

        properties |> List.map (fun (name, value) -> name, toJsonElement value)
    | _ -> []

/// Reads the provider's usage report, inventing nothing it did not send.
let private usageOf (usage: Usage) : ProviderUsage =
    let optional (value: Nullable<int64>) =
        if value.HasValue then Some(int value.Value) else None

    { InputTokens = Some(int usage.InputTokens)
      OutputTokens = Some(int usage.OutputTokens)
      CachedInputTokens = optional usage.CacheReadInputTokens
      ProviderReportedCost = None }

/// Classifies a transport failure. The categories matter because only some
/// of them make re-sending the identical request reasonable (ORDO-1604).
let private errorOf (ex: exn) : ProviderError =
    match ex with
    | :? AnthropicRateLimitException -> RateLimited None
    | :? AnthropicUnauthorizedException as ex -> AuthenticationFailure ex.Message
    | :? AnthropicForbiddenException as ex -> AuthenticationFailure ex.Message
    | :? Anthropic5xxException as ex -> Unavailable ex.Message
    | :? AnthropicBadRequestException as ex -> ContractViolation ex.Message
    | :? AnthropicUnprocessableEntityException as ex -> ContractViolation ex.Message
    | :? AnthropicInvalidDataException as ex -> InvalidResponse ex.Message
    | :? AnthropicIOException as ex -> Unavailable ex.Message
    | :? TaskCanceledException as ex -> Unavailable ex.Message
    | ex -> InternalFailure ex.Message

/// Finds the single tool call this adapter asked for.
///
/// A reply containing no such call — because the model wrote prose, or
/// called nothing — is a contract violation. There is no fallback that reads
/// the prose instead (ORDO-7502 / ORDO-6101).
let private toolInputOf (message: Message) : Result<JsonValue, ProviderResponse> =
    let toolUse =
        message.Content
        |> Seq.tryPick (fun block ->
            match block.TryPickToolUse() with
            | true, value ->
                // The out parameter is nullable even when the pick succeeds,
                // so it is narrowed before anything reads a member of it.
                Option.ofObj value
                |> Option.filter (fun toolUse -> toolUse.Name = Contract.ToolName)
            | _ -> None)

    match toolUse with
    | None ->
        Error(
            ProviderFailed(
                ContractViolation(
                    sprintf "reply contained no call to the %s tool (stop reason: %s)" Contract.ToolName (string message.StopReason)
                )
            )
        )
    | Some toolUse ->
        match parse (JsonSerializer.Serialize toolUse.Input) with
        | Ok value -> Ok value
        | Error error -> Error(ProviderFailed(InvalidResponse(sprintf "tool input was not readable JSON: %A" error)))

/// One attempt, plus however many transport retries remain.
///
/// Recursive rather than a loop so that each retry is a fresh attempt with
/// its own timeout, and lifted out of the caller's `task` block because a
/// recursive binding inside one forces a slower dynamic state machine.
let rec private attempt
    (client: AnthropicClient)
    (options: AnthropicOptions)
    (identity: ProviderIdentity)
    (request: ProviderRequest)
    (parameters: MessageCreateParams)
    (cancellation: CancellationToken)
    (remaining: int)
    (retriesSoFar: int)
    : Task<ProviderOutcome> =
    task {
        try
            use timeout = CancellationTokenSource.CreateLinkedTokenSource cancellation
            timeout.CancelAfter options.Timeout

            let! message = client.Messages.Create(parameters, cancellationToken = timeout.Token)

            let response =
                match toolInputOf message with
                | Error failure -> failure
                | Ok input -> Contract.interpret request input

            return
                { Response = response
                  Identity = identity
                  Usage = usageOf message.Usage
                  TransportRetries = retriesSoFar }
        with
        | :? OperationCanceledException when cancellation.IsCancellationRequested ->
            // The caller cancelled. Not a provider failure (ORDO-6602).
            return
                { Response = ResponseCancelled
                  Identity = identity
                  Usage = ProviderUsage.unreported
                  TransportRetries = retriesSoFar }
        | :? OperationCanceledException ->
            return
                { Response = ProviderFailed(ProviderTimeout options.Timeout)
                  Identity = identity
                  Usage = ProviderUsage.unreported
                  TransportRetries = retriesSoFar }
        | ex ->
            let error = errorOf ex

            if remaining > 0 && ProviderError.isTransportTransient error then
                return! attempt client options identity request parameters cancellation (remaining - 1) (retriesSoFar + 1)
            else
                return
                    { Response = ProviderFailed error
                      Identity = identity
                      Usage = ProviderUsage.unreported
                      TransportRetries = retriesSoFar }
    }

/// Builds a provider backed by the Anthropic Messages API.
///
/// The client is supplied by the caller rather than constructed here, so
/// that credentials arrive through whatever secure boundary the application
/// already uses and never sit in an Ordo contract (ORDO-2502 / ORDO-3-171).
let create (client: AnthropicClient) (options: AnthropicOptions) : DecisionProvider =
    let identity =
        { Provider =
            ProviderId.create "anthropic"
            |> function
                | Ok id -> id
                | Error _ -> failwith "the literal provider id \"anthropic\" is valid by construction"
          Model = Some options.Model
          ModelVersion = None
          AdapterVersion = Contract.AdapterVersion }

    let resolve (request: ProviderRequest) (cancellation: CancellationToken) : Task<ProviderOutcome> =
        let parameters =
            MessageCreateParams(
                Model = options.Model,
                MaxTokens = int64 options.MaxTokens,
                System = Contract.systemInstruction,
                Messages =
                    [| MessageParam(
                           Role = Role.User,
                           Content =
                               MessageParamContent(
                                   Contract.promptBlocks request
                                   |> List.map (fun text -> ContentBlockParam(TextBlockParam(Text = text)))
                                   |> Array.ofList
                               )
                       ) |],
                Tools =
                    [| ToolUnion(
                           Tool(
                               Name = Contract.ToolName,
                               Description =
                                   "Record the resolution of the bounded question in the user message. This is the only way to answer.",
                               Strict = true,
                               InputSchema =
                                   InputSchema(Properties = readOnlyDict (schemaProperties request), Required = [| "outcome" |])
                           )
                       ) |],
                ToolChoice = ToolChoice(ToolChoiceTool(Name = Contract.ToolName, DisableParallelToolUse = true))
            )

        attempt client options identity request parameters cancellation options.TransportRetries 0

    { Identity = identity
      Capabilities =
        [ BoundedChoiceSelection
          NativeStructuredOutput
          SelfReportedConfidence
          CooperativeCancellation ]
      Resolve = resolve }
