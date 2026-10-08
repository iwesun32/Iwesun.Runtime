# iwesun Runtime integration specification framework

[中文](../SPECIFICATION.md) · [Complete catalog](../DOCUMENTATION_CATALOG.md) · [Documentation download](https://github.com/iwesun32/Iwesun.Runtime/releases/tag/docs-v1.0.47-r1)

iwesun Runtime is a coordinated specification and implementation: public libraries, host integration rules, managed state and lifecycle contracts, diagnostic protocols, authorization boundaries, and acceptance procedures. Installing DLLs or running a sample does not establish correct integration. The quickstart is an introduction, not a substitute for the relevant contracts.

This framework is bound to Runtime **1.0.47-beta.1**, Networks **3.0.0-beta.6**, documentation revision **r1**. It organizes the existing public specification without redefining APIs. Detailed technical references remain primarily Chinese originals. This English framework and the English user guide are not a full translation of that corpus.

## Interpreting versions and evidence

| Question | Authority |
| --- | --- |
| What was shipped and tested? | The [current release manifest](../IWESUN_RUNTIME_1.0.47_BETA_RELEASE_MANIFEST.md) and [release guide](RELEASE_GUIDE.md) |
| What semantics must consumers preserve? | The module's accepted design, API and lifecycle contracts, read with its current index and release status |
| Why was a decision made? | Dated migrations, release notes, RFCs and incident records in their original version context |
| Is a planned capability complete? | Current implementation status and acceptance evidence; a plan or requirement is not completion evidence |
| Is a particular host ready? | Acceptance in that host's target environment, not an inference from library tests alone |

Historical paths, versions, test counts and “not yet released” statements retain their original context. Report conflicts between current contracts, implementation and evidence with exact versions instead of choosing a permissive interpretation. This remains a beta: the full real-WFP/next-hop matrix and real-browser/long-running workloads are not all certified.

## Required reading for managed hosts

| Stage | References | Integration question |
| --- | --- | --- |
| Architecture | [User manual](../IWESUN_RUNTIME_USER_GUIDE.md), [design](../IWESUN_RUNTIME_DESIGN.md) | Which components are used, and where does host ownership begin? |
| Startup and injection | [Integration guide](../05-runtime-tooling/RUNTIME_INTEGRATION_GUIDE.md), [injector standardization](../05-runtime-tooling/INJECTOR_STANDARDIZATION.md), [sample host](../05-runtime-tooling/SAMPLE_HOST.md) | Who owns DI, startup, activation, registration and resources? |
| Managed execution | [Process/thread injection](../05-runtime-tooling/PROCESS_THREAD_INJECTION.md), [thread/task management](../05-runtime-tooling/THREAD_TASK_MANAGEMENT.md) | How are execution units registered, cancelled, observed and cleaned up? |
| State | [State classification](../05-runtime-tooling/STATE_CLASSIFICATION.md), [business state](../05-runtime-tooling/STATE_CLASSIFICATION_ONLINE.md), [hierarchy](../05-runtime-tooling/STATE_MACHINE_HIERARCHY.md) | How do coarse lifecycle state and business detail differ? |
| Shutdown | [Shutdown migration](../IWESUN_RUNTIME_1.0.43_SHUTDOWN_MIGRATION.md), [Windows Service](../IWESUN_RUNTIME_WINDOWS_SERVICE.md), [guardian and pipe registry](../05-runtime-tooling/GUARDIAN_PIPE_REGISTRY_DESIGN.md) | How are request acceptance, execution, timeout and process exit distinguished? |
| Diagnostics and access | [Diagnostics](../RUNTIME_DIAGNOSTICS.md), [CLI](../IWESUN_RUNTIME_CLI.md), [remote authorization](../IWESUN_RUNTIME_REMOTE_ACCESS.md) | Are silent defaults, allowlists, account permissions and build-mode differences preserved? |
| Acceptance | [Release manifest](../IWESUN_RUNTIME_1.0.47_BETA_RELEASE_MANIFEST.md) and the selected module's migration/validation references | Do binaries, contracts, rollback baselines and the target environment match? |

Consumers using only independent Data or Networks functionality need not enable the entire host stack; their selected module contracts still apply.

## Non-negotiable integration boundaries

- Use matching published binaries and configuration. Do not mix Debug and Release or use cross-repository project references to bypass versioned consumption.
- Follow `RuntimeHostTemplate` startup and activation. Register and clean up managed execution units; replacing primitive type names with `RProcess`, `RThread` or `RTask` alone is not a complete migration.
- Command acceptance does not prove shutdown completion. Preserve evidence of unfinished units and the final host outcome when cleanup times out.
- Keep diagnostics off by default, route observations through Runtime, and restore `quiet` after inspection. Do not introduce temporary console/file debug paths.
- Reflection access is explicitly allowlisted and constrained by build capabilities. Debug capability does not imply Release capability. Breakpoints are cooperative waits, not arbitrary thread suspension.
- Use existing CLI, protocol models and clients; do not hand-roll frames or hardcode host pipe names. Keep command configuration, metadata and user overrides consistent with the CLI contract.
- C# owns external file loading, deserialization, migration, validation and error reporting. XAML contains controls, styles, templates and bindings to validated in-memory values only.

## Module-specific contracts

| Module | Required references | Key boundaries |
| --- | --- | --- |
| Data / RecordStore | [Index](../../modules/Data/docs/README.md), [design](../../modules/Data/docs/01-design/RECORD_STORE_DESIGN.md), [public API](../../modules/Data/docs/02-api/RECORD_STORE_PUBLIC_API.md), [derived tables](../../modules/Data/docs/01-design/RECORD_STORE_DERIVED_TABLES.md), [publication lifecycle](../../modules/Data/docs/02-api/RECORD_STORE_ADD_PUBLICATION_LIFECYCLE.md) | Business keys differ from `StoreRecordId`; respect constraints, indexes, merging, subscriptions, clearing, snapshots, persistence and optional access gates |
| Networks | [Index](../../modules/Networks/docs/README.md), [accepted final design](../../modules/Networks/docs/01-design/PRECISION_NETWORK_ACCESS_CONTROL_FINAL_DESIGN.md), [tracked request/reply](../../modules/Networks/docs/02-endpoints/TRACKED_REQUEST_REPLY.md), [logical contracts](../../modules/Networks/docs/03-reference/LOGICAL_CONTRACTS.md), [migration matrix](../../modules/Networks/docs/03-reference/NETWORKS_3_0_API_MIGRATION_MATRIX.md) | Preserve four-level GUID correlation; requested access is not actual evidence; distinguish protocol, access and execution outcomes; no implicit fallback; obey endpoint-specific backpressure, retry, response-window and cancellation rules |
| WebView2 | [Index](../../modules/WebView2/docs/README.md), [control](../../modules/WebView2/docs/WEB_RUNTIME_CONTROL.md), [CDP tree](../../modules/WebView2/docs/CDP_DOM_NODE_TREE.md), [page/HTTP evidence](../../modules/WebView2/docs/FULL_PAGE_EVIDENCE_API.md), [status](../../modules/WebView2/docs/WEBVIEW2_RELEASE_STATUS.md) | Live nodes differ from auxiliary trees; identities belong to a revision; refresh after invalidation as specified; page content is data, not authority; respect thread ownership, sensitive information and resource cleanup |
| CLI / RemoteConsole | [CLI](../IWESUN_RUNTIME_CLI.md), [RemoteConsole](../IWESUN_RUNTIME_REMOTE_CONSOLE.md), [account authorization](../IWESUN_RUNTIME_REMOTE_ACCESS.md) | Verify command capability, risk, identity and authorization independently |
| AI integration | [Integration skill](../../skills/iwesun-runtime-integration/SKILL.md) and its references, [repository rules](../../AGENTS.md) | Skills implement conventions; they do not expand business authorization or bypass evidence-reading boundaries |

Use the Networks index for Socket, HTTP/DoH, TCP/UDP/Ping, name resolution, WFP, address types, multi-response and recovery contracts. Proxy/NAT/forwarding requirements and designs do not establish delivery of complete applications.

## Acceptance and migration

1. Pin runtime binaries, build configuration, documentation tag, host environment and selected modules.
2. Preserve previous binaries, configuration and persistent data; review breaking changes and migration matrices.
3. Validate samples and module tests, then actual-host startup, registration, business execution and repeated start/stop.
4. Test normal shutdown, cancellation, cleanup failures and timeouts; inspect unfinished units and final process outcomes, not just CLI success.
5. Check silent defaults, selective output, denied unauthorized operations and Debug/Release differences, then restore silence.
6. Validate the selected functionality: Data constraints/publication/persistence; Networks actual access/outcomes/multiple responses/cancellation; WebView2 real UI threads/navigation/evidence/resource lifecycle.
7. Record passed, failed and unexecuted checks separately with environment prerequisites. Library tests do not replace host acceptance.

Specialized references: [WFP Windows runbook](../../modules/Networks/docs/02-endpoints/WFP_WINDOWS_VALIDATION_RUNBOOK.md), [Ping outcome validation](../../modules/Networks/docs/03-reference/NETWORKS_3_0_PING_TERMINAL_BETA_TEST_GUIDE.md), [UDP data-plane validation](../../modules/Networks/docs/03-reference/NETWORKS_3_0_UDP_DATA_PLANE_BETA_TEST_GUIDE.md), [WebView2 sample](../../modules/WebView2/docs/WEBVIEW2_SAMPLE_HOST.md).

## Complete corpus and offline package

The [complete catalog](../DOCUMENTATION_CATALOG.md) enumerates the public texts and supporting templates, CLI configurations, integration skill and licenses. The [r1 documentation release](https://github.com/iwesun32/Iwesun.Runtime/releases/tag/docs-v1.0.47-r1) preserves source paths and provides an HTML navigation page, per-file SHA-256 manifest and download checksums. Use a Markdown reader offline; source-code references require the same tagged repository. The installer index describes the separate MSI/runtime layout.

Unfinished Tables, the Web development project, private handoffs and ignored archives are excluded. Historical references do not restore removed compatibility APIs. See [LICENSE](../../LICENSE), [governance](../../GOVERNANCE.md) and [contribution rules](../../CONTRIBUTING.md).
