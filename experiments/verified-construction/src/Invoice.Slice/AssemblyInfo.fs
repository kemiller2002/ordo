namespace Invoice.Slice

open System.Runtime.CompilerServices

// DEC-FIN-002: only the payments service may write payments. The
// payments-service capability's constructor is `internal`; this is the only
// assembly outside Invoice.Slice that may call it. No code in Invoice.Slice
// itself constructs one (checked by a test that scans this project's source).
[<assembly: InternalsVisibleTo("Invoice.Slice.PaymentsService")>]
do ()
