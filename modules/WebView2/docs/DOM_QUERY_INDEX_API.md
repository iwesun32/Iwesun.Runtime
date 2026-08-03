# Strongly typed DOM query index

`IWebRuntimeDomQuerySession` is the live Fill query boundary shared by product
code and CLI hosts. It does not accept JavaScript, JSON, file paths, or generic
CDP commands.

## Data flow

1. The page capture implementation creates a
   `WebRuntimeDomQueryIndexBuilder` from the whole-tree request plan.
2. It records exactly one explicit result for every requested property slot.
3. The builder creates an immutable `WebRuntimeDomQueryIndex`.
4. A `WebRuntimeIndexedDomQuerySession` exposes that index.
5. Product and CLI callers submit one batch of
   `WebRuntimeDomPropertyRequest` values.
6. The session resolves every request by its complete identity and preserves
   request order.

For a live page, `WebRuntimeLiveDomQuerySession` accepts the whole-tree batch,
calls one `IWebRuntimeDomEvidenceReader`, validates the returned records with
the same builder, publishes `LastIndex`, and returns results in request order.
Product and CLI entry points use this same session type.

Missing records are errors. An absent DOM attribute is represented by an
explicit `ConfirmedAbsent` record. Unsupported evidence is represented by an
explicit `SourceUnsupported` record. Neither state may be inferred from a
missing index entry.

The builder rejects duplicate planned identities, results outside the plan,
multiple results for one identity, and publication while any planned identity
is still pending.

## Identity

The index key contains:

- document scope;
- absolute XPath;
- tag name;
- reflected CLR property name;
- DOM property name;
- owner kind;
- slot category;
- evidence kind;
- initialization, link, or runtime slot.

This prevents one property, frame, slot, or element from supplying another
property's value.

## Example

```csharp
var identity = new WebRuntimeDomPropertyIdentity(
    "top",
    "/html/body/div",
    "div",
    "id",
    "Id",
    WebRuntimeDomOwnerKind.Attribute,
    WebRuntimeDomSlotCategory.DataOrganization,
    WebRuntimeDomEvidenceKind.Attributes,
    WebRuntimeDomDataSlot.Initialization);

var request = new WebRuntimeDomPropertyRequest(
    "0",
    identity.DocumentScope,
    identity.XPath,
    identity.TagName,
    identity.PropertyName,
    identity.ReflectedPropertyName,
    identity.OwnerKind,
    identity.Category,
    identity.EvidenceKind,
    identity.Slot);

var builder = new WebRuntimeDomQueryIndexBuilder(
    1,
    new Uri("https://example.test/"),
    DateTimeOffset.UtcNow,
    [request]);
builder.SetCaptured(
    identity,
    "app",
    WebRuntimeDomValueSource.DirectConstant,
    description: "authored id attribute");

var index = builder.Build();
var session = new WebRuntimeIndexedDomQuerySession(index);
```

To publish a newly captured page state, create a complete index with a higher
revision and call `ReplaceIndex`. The replacement is atomic. Mutating an
existing index is unsupported.

## Performance contract

- One Fill operation submits one whole-tree query batch.
- `WebRuntimeCdpDomEvidenceReader` captures one immutable
  `DOMSnapshot.captureSnapshot` containing the requested computed-style names,
  DOM rectangles, paint order, text opacity, and blended background evidence.
- Runtime computed-style slots are resolved from that same snapshot revision.
  Chromium may omit the computed-style table for nodes without a layout entry;
  only those incomplete nodes fall back to `CSS.getComputedStyleForNode`. The
  formal whole-tree path must not repeat that call for nodes whose snapshot
  evidence is complete.
- Authored declarations and rule identities use
  `CSS.getMatchedStylesForNode` once per distinct requested node. Runtime event
  listeners use `DOM.resolveNode` plus `DOMDebugger.getEventListeners` once per
  distinct requested backend node. These enrichments are node-scoped, never
  reflection-slot-scoped.
- Lookups use an immutable frozen dictionary.
- No JSON serialization or deserialization occurs in the query session.
- No per-element browser call occurs after the index is prepared.
- Persistence, if requested, happens after Fill and is outside this API.

## CLI boundary

A CLI command must construct or receive the same
`IWebRuntimeDomQuerySession`; it must not introduce a JSON-specific query
implementation. A CLI command has not yet been added, so no command name is
documented here.

## HTML runtime root

`HtmlRuntimeDocumentRoot` connects this query API to the shared
`Iwesun.Runtime.Web.HtmlDocumentRoot` object model. Product buttons and CLI
hosts subclass the same root and implement only `BuildElementTreeAsync`.

The root owns:

- `HtmlRuntimeWebView2Context`, including the navigation URL, absolute-XPath
  click operations, direct DOM query session, and optional script session;
- optional CSS, layout, runtime-state, and event evidence connections;
- the mounted DOM element tree and whole-tree Fill receipts;
- the in-memory XAML object tree built by each element;
- the display receipt, real XamlFill/audit result, and writer receipt.

The required order is:

```text
Navigate → Build → Fill → BuildXaml → DisplayXaml → AuditXaml → WriteXaml
```

`WebRuntimeDomQueryBridge` performs the only conversion between the Web
reflection-slot contract and this module's query contract. Enum values are
mapped explicitly; ordinal casts are forbidden because the two public
contracts do not have identical link-kind catalogs.

`HtmlRuntimeWebView2Context.CreateLive` wires the production path:

1. `WebRuntimeCdpDomTreeReader` converts the CDP DOM document into typed
   element nodes and document/frame identities.
2. `WebRuntimeLiveDomTreeSession` validates and publishes that tree.
3. `WebRuntimeCdpDomEvidenceReader` performs one strongly typed whole-tree
   evidence read without injecting JavaScript.
4. `WebRuntimeLiveDomQuerySession` freezes the complete result index used by
   element Fill.

The protocol adapter may parse WebView2's internal JSON response, but JSON,
script text, and files never cross the public tree or Fill API. Nested document
roots use the owning iframe element identity as `DocumentScope`, while keeping
their local XPath unchanged.
