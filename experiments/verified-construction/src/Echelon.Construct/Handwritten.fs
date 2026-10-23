/// The handwritten (and extension-generated) rule-bearing constructs of the
/// slice, as data. Each row corresponds to exactly one `[<Authority(...)>]`
/// marker in the source; the test suite checks that correspondence by
/// reflection over the built assemblies and by scanning the source, so this
/// table cannot drift from the code silently.
///
/// VAC-002-P3-03-R2: a construct that reuses another's rule cites that rule
/// too (e.g. `Probe.step` cites everything `Invoicing.pay` and
/// `Invoicing.voidInvoice` rest on). The test `reuse closure` checks the
/// declared reuse edges below.
module Echelon.Construct.Handwritten

[<RequireQualifiedAccess>]
type ConstructKind =
    | State
    | Command
    | Transition
    | Refusal
    | ValidatedConstructor
    | ValueType
    | Constant
    | Port
    | Capability

[<RequireQualifiedAccess>]
type Disposition =
    | Authorized
    | DeferredPendingQuestion
    | Technical

type Construct =
    { Symbol: string
      Kind: ConstructKind
      File: string
      Disposition: Disposition
      Authority: string list }

let private authorityFile = "src/Invoice.Slice/generated/Invoice.Authority.g.fs"
let private moneyFile = "src/Invoice.Slice/Money.fs"
let private paymentFile = "src/Invoice.Slice/Payment.fs"
let private invoicingFile = "src/Invoice.Slice/Invoicing.fs"
let private persistenceFile = "src/Invoice.Slice/Persistence.fs"
let private probeFile = "src/Invoice.Slice/Probe.fs"
let private rootFile = "src/Invoice.Slice.PaymentsService/CompositionRoot.fs"

let private row symbol kind file disposition authority =
    { Symbol = symbol; Kind = kind; File = file; Disposition = disposition; Authority = authority }

let private authorized symbol kind file authority = row symbol kind file Disposition.Authorized authority

let constructs : Construct list =
    [ // Extension-generated decision parameters (AuthorityEmit).
      authorized "Invoice.Slice.Authority" ConstructKind.Constant authorityFile [ "DEC-FIN-001"; "DEC-FIN-002"; "Q-FIN-001" ]
      authorized "Invoice.Slice.Authority.decFin001Limit" ConstructKind.Constant authorityFile [ "DEC-FIN-001" ]
      authorized "Invoice.Slice.Authority.decFin001RefusalCode" ConstructKind.Constant authorityFile [ "DEC-FIN-001" ]
      authorized "Invoice.Slice.Authority.decFin002Role" ConstructKind.Constant authorityFile [ "DEC-FIN-002" ]
      row "Invoice.Slice.Authority.qFin001" ConstructKind.Constant authorityFile Disposition.DeferredPendingQuestion [ "Q-FIN-001" ]
      // Values.
      authorized "Invoice.Slice.Money.PositiveAmount" ConstructKind.ValueType moneyFile [ "REQ-INV-002" ]
      authorized "Invoice.Slice.Money.positiveAmount" ConstructKind.ValidatedConstructor moneyFile [ "REQ-INV-002" ]
      row "Invoice.Slice.Money.currency" ConstructKind.ValidatedConstructor moneyFile Disposition.Technical [ "REQ-INV-002" ]
      authorized "Invoice.Slice.Payment.PaymentLimit" ConstructKind.ValueType paymentFile [ "DEC-FIN-001"; "REQ-INV-002" ]
      authorized "Invoice.Slice.Payment.decFin001" ConstructKind.Constant paymentFile [ "DEC-FIN-001" ]
      authorized "Invoice.Slice.Payment.ValidatedPayment" ConstructKind.ValueType paymentFile [ "REQ-INV-002"; "DEC-FIN-001" ]
      authorized "Invoice.Slice.Payment.exceedsLimit" ConstructKind.Refusal paymentFile [ "DEC-FIN-001" ]
      authorized "Invoice.Slice.Payment.validate" ConstructKind.ValidatedConstructor paymentFile [ "REQ-INV-002"; "DEC-FIN-001" ]
      // The invoice over the generated state machine.
      authorized "Invoice.Slice.Invoicing.AppliedPayment" ConstructKind.ValueType invoicingFile [ "REQ-INV-002"; "REQ-INV-003"; "INV-INV-003" ]
      authorized "Invoice.Slice.Invoicing.Invoice" ConstructKind.ValueType invoicingFile [ "REQ-INV-001"; "REQ-INV-002"; "INV-INV-001"; "INV-INV-002" ]
      authorized "Invoice.Slice.Invoicing.Refusal" ConstructKind.Refusal invoicingFile [ "REQ-INV-001"; "REQ-INV-002"; "Q-FIN-001" ]
      authorized "Invoice.Slice.Invoicing.wireCode" ConstructKind.Refusal invoicingFile [ "REQ-INV-001"; "REQ-INV-002"; "DEC-FIN-001"; "Q-FIN-001" ]
      authorized "Invoice.Slice.Invoicing.refusalCode" ConstructKind.Refusal invoicingFile [ "REQ-INV-001"; "REQ-INV-002"; "DEC-FIN-001"; "Q-FIN-001" ]
      authorized "Invoice.Slice.Invoicing.create" ConstructKind.Transition invoicingFile [ "REQ-INV-001" ]
      row "Invoice.Slice.Invoicing.pendingQuestion" ConstructKind.Refusal invoicingFile Disposition.DeferredPendingQuestion [ "Q-FIN-001" ]
      authorized "Invoice.Slice.Invoicing.ofGenerated" ConstructKind.Refusal invoicingFile [ "REQ-INV-001"; "Q-FIN-001" ]
      authorized "Invoice.Slice.Invoicing.ofPaymentRefusal" ConstructKind.Refusal invoicingFile [ "REQ-INV-002"; "DEC-FIN-001" ]
      authorized "Invoice.Slice.Invoicing.settledWhen" ConstructKind.Transition invoicingFile [ "REQ-INV-002"; "INV-INV-002" ]
      authorized "Invoice.Slice.Invoicing.issue" ConstructKind.Transition invoicingFile [ "REQ-INV-001" ]
      authorized "Invoice.Slice.Invoicing.voidInvoice" ConstructKind.Transition invoicingFile [ "REQ-INV-001"; "Q-FIN-001" ]
      authorized "Invoice.Slice.Invoicing.recordPayment" ConstructKind.Transition invoicingFile
          [ "REQ-INV-001"; "REQ-INV-002"; "DEC-FIN-001"; "INV-INV-001"; "INV-INV-002"; "INV-INV-003" ]
      authorized "Invoice.Slice.Invoicing.pay" ConstructKind.Transition invoicingFile [ "REQ-INV-001"; "REQ-INV-002"; "DEC-FIN-001" ]
      // Persistence boundary.
      authorized "Invoice.Slice.Persistence.PaymentsServiceCapability" ConstructKind.Capability persistenceFile [ "DEC-FIN-002"; "REQ-INV-003" ]
      authorized "Invoice.Slice.Persistence.PaymentStore" ConstructKind.Port persistenceFile [ "REQ-INV-003"; "DEC-FIN-002" ]
      authorized "Invoice.Slice.Persistence.persist" ConstructKind.Port persistenceFile [ "REQ-INV-003"; "DEC-FIN-002" ]
      authorized "Invoice.Slice.PaymentsService.CompositionRoot.capability" ConstructKind.Capability rootFile [ "DEC-FIN-002" ]
      authorized "Invoice.Slice.PaymentsService.CompositionRoot.recordAndPersist" ConstructKind.Port rootFile
          [ "REQ-INV-001"; "REQ-INV-002"; "REQ-INV-003"; "DEC-FIN-001"; "DEC-FIN-002" ]
      // Probe facade (reuses the rules above).
      authorized "Invoice.Slice.Probe.start" ConstructKind.Transition probeFile [ "REQ-INV-001"; "REQ-INV-002" ]
      authorized "Invoice.Slice.Probe.step" ConstructKind.Transition probeFile [ "REQ-INV-001"; "REQ-INV-002"; "DEC-FIN-001"; "Q-FIN-001" ]
      row "Invoice.Slice.Probe.stateName" ConstructKind.State probeFile Disposition.Technical [ "REQ-INV-001" ] ]

/// Reuse edges (user -> reused) between rule-bearing constructs, for the
/// R2 closure check: a user's authority must include every reused construct's
/// requirement, decision and question IDs.
let reuses : (string * string) list =
    [ "Invoice.Slice.Payment.decFin001", "Invoice.Slice.Authority.decFin001Limit"
      "Invoice.Slice.Payment.validate", "Invoice.Slice.Money.positiveAmount"
      "Invoice.Slice.Payment.validate", "Invoice.Slice.Payment.exceedsLimit"
      "Invoice.Slice.Invoicing.wireCode", "Invoice.Slice.Authority.decFin001RefusalCode"
      "Invoice.Slice.Invoicing.voidInvoice", "Invoice.Slice.Invoicing.pendingQuestion"
      "Invoice.Slice.Invoicing.refusalCode", "Invoice.Slice.Invoicing.wireCode"
      "Invoice.Slice.Invoicing.ofGenerated", "Invoice.Slice.Invoicing.pendingQuestion"
      "Invoice.Slice.Invoicing.voidInvoice", "Invoice.Slice.Invoicing.ofGenerated"
      "Invoice.Slice.Invoicing.pay", "Invoice.Slice.Invoicing.ofPaymentRefusal"
      "Invoice.Slice.Probe.step", "Invoice.Slice.Invoicing.refusalCode"
      "Invoice.Slice.Invoicing.recordPayment", "Invoice.Slice.Invoicing.settledWhen"
      "Invoice.Slice.Invoicing.pay", "Invoice.Slice.Payment.validate"
      "Invoice.Slice.Invoicing.pay", "Invoice.Slice.Invoicing.recordPayment"
      "Invoice.Slice.PaymentsService.CompositionRoot.capability", "Invoice.Slice.Authority.decFin002Role"
      "Invoice.Slice.PaymentsService.CompositionRoot.recordAndPersist", "Invoice.Slice.Invoicing.pay"
      "Invoice.Slice.PaymentsService.CompositionRoot.recordAndPersist", "Invoice.Slice.Persistence.persist"
      "Invoice.Slice.Probe.step", "Invoice.Slice.Invoicing.pay"
      "Invoice.Slice.Probe.step", "Invoice.Slice.Invoicing.voidInvoice"
      "Invoice.Slice.Probe.step", "Invoice.Slice.Invoicing.issue" ]
