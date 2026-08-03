# Iwesun.Runtime.Networks 3.0 integration

Use the bundled `lib/Iwesun.Runtime.Networks/Iwesun.Runtime.Networks.dll` or the independently built
`Iwesun.Runtime.Networks` 3.0 package. Do not copy network endpoint implementations into Runtime or a
business host.

## Boundaries

- `Iwesun.Runtime.Networks` depends only on `System.*`; consumers depend on Networks, never the reverse.
- `TrackedRequestReplyEndpointBase<TRequest,TResponse,TKey>` is the only public protocol
  request/reply base. TCP, UDP, Ping, PTR, NBNS, HTTP, and DoH use `Network*Endpoint<TKey>` types.
- All protocol requests carry a non-empty `RequestedAccessPlan`. The low-level `TrySend` rejects
  `Guid.Empty` before enqueue; PowerShell, Process, and local snapshot adapters follow the same rule.
  Create the final `RequestId` synchronously at the closest source.
- The fixed identity chain is `RequestId -> AttemptId -> BranchId -> ResponseId`. Every created
  attempt has at least one branch, including ordinary single-path attempts. Rejected pre-attempt
  requests do not fabricate attempt or branch identities.
- `TKey` is repeatable caller query data only. Pending, retry, response matching, completion,
  removal, awaiting, and late-response attribution use GUID identities exclusively.
- Treat path providers and selectors separately. `Automatic`, `Direct`, `RouteAdapter`, and
  `SystemProxy` are explicit providers; target, source, interface, next hop, route scope, local
  endpoint, packet policy, and resolver constraints are orthogonal selectors.
- Never restore V2 types, `NetworkRouteDirective`, nullable Route, old route flags, compatibility
  handlers, or adapters. Missing migrations must remain compile errors.
- Requested, Resolved, and Actual are separate. Actual evidence belongs to Branch and must not be
  inferred from caller intent. Failure to satisfy a Required constraint is explicit and never
  falls back silently.
- Retries keep the normalized requested plan fixed. A new attempt may re-resolve current route and
  interface snapshots and choose another member of the same PreferredSet, creating new branches.
- HTTP/DoH pooling keys include stable access and security semantics, while excluding GUIDs,
  timestamps, timeout/retry settings, and observations. Request-level WFP policies disable unsafe
  connection reuse.
- UDP pools retain active and quarantined slots, periodically retire completely idle pools, and prefer
  safe idle-pool eviction over capacity rejection when traffic moves across many historical targets.
- `IpAddressValue` is the sole canonical IP contract and an exact 17-byte pure-address value:
  128 address bits plus Null/IPv4/IPv6 state. The removed `BinaryIpAddress` must remain a compile error.
  It never stores ScopeId, interface, port, or cached classification. Scoped IPv6 text is rejected;
  keep zone/interface constraints in the access plan, transport endpoint, or actual path evidence.
- `MacAddressValue` is an exact 7-byte EUI-48 value: 48 address bits plus Null/Value state.
  OUI, NIC identifier, text, and traits are computed views and are never cached as fields.
- ARP and IPv6-neighbor public records use `IpAddressValue` and `MacAddressValue`, never address
  strings. IPv6 interface scope stays in the separate `InterfaceIndex`; an unknown MAC is
  `MacAddressValue.Null`.
- Runtime embeds Networks with its independent `3.0.0.0` assembly/file version. Runtime version
  injection must not alter the Networks API version.
- WFP ExactNextHop, administrator mutation, crash cleanup, and real network recovery remain M11
  validation gates. Do not install, start services, or change system network state without explicit
  authorization.

Start with the installed `docs/Iwesun.Runtime.Networks/README.md`, then read the final design, endpoint
guide, migration matrix, and release status before migrating a consumer.
