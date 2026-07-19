# Iwesun.Networks 1.2.0 integration

Use the bundled `lib/Iwesun.Networks/Iwesun.Networks.dll` or the independently released
`Iwesun.Networks` 1.2.0 package. Do not copy network endpoint implementations into Runtime or a
business host.

## Boundaries

- `Iwesun.Networks` depends only on `System.*`; consumers depend on Networks, never the reverse.
- Request and result contracts are value types. Keep endpoint-specific records as `readonly record struct` or `struct`.
- Preserve the FIFO and hardware-state model. New endpoints derive from
  `NetworkAsyncEndpointBase<TSend,TReceive>` and publish completion through `PublishReceived` or
  `PublishReceivedRaw`.
- Validate and reject bad work in `OnFilterSend`; do not throw through the endpoint send thread.
- `OnSend` starts asynchronous work and returns promptly. Completion, failure and timeout must each
  publish a result.
- Runtime 1.0.29 bundles Networks with its independent `1.2.0.0` file version. Runtime package
  version injection must not be interpreted as changing the Networks API version.

Start with the installed `docs/Iwesun.Networks/README.md`, then read the endpoint-specific guide and
the 1.2.0 release status before migrating a consumer.
