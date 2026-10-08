# Runtime 1.0.47-beta.1 release guide

[中文](../IWESUN_RUNTIME_1.0.47_BETA_RELEASE_GUIDE.md) · [User guide](USER_GUIDE.md) · [Downloads](https://github.com/iwesun32/Iwesun.Runtime/releases)

Date: 2026-10-09. Runtime: `1.0.47-beta.1`; Networks: `3.0.0-beta.6`. Author and brand: iwesun.

## Included

Diagnostics, Data/RecordStore, Networks, WebView2, CLI, RemoteConsole, samples, an AI integration skill, configuration templates, technical documentation, licensing and third-party notices. Public libraries include Debug and Release variants.
Tables is unfinished and excluded from public source and packages. The separate Web development project is excluded from this source snapshot and runtime payload.

This candidate incorporates managed-thread shutdown synchronization, duplicate shutdown-delivery prevention, request-scoped HTTP cancellation, Windows network-interface identity fixes and current WebView2 DOM/evidence work.
Networking is described as parallel endpoints with explicit correlation and access contracts, not as all the business features of a consuming application.

## Downloads and versions

| Asset | Purpose |
| --- | --- |
| `Iwesun.Runtime.1.0.47-beta.1.msi` | Windows installer |
| `Iwesun.Runtime.1.0.47-beta.1.zip` | Portable `app` / `data` payload |
| `Iwesun.Runtime.Networks.3.0.0-beta.6.nupkg` | Local-feed Networks package |
| `Iwesun.Runtime.Networks.3.0.0-beta.6.snupkg` | Networks symbols |
| `SHA256SUMS.txt` | Download integrity checks |
| Tagged source | Source, tests, documentation and governance |

Packages are distributed through GitHub Releases, not uploaded to nuget.org. The MSI is not Authenticode-signed and does not bundle prerequisite .NET or WebView2 runtime installers.
Runtime assembly/file version: `1.0.47.0`. Networks assembly/file version: `3.0.0.0`, product information: `3.0.0-beta.6`.

## Validation

- Data: 101 tests in each of Debug and Release.
- Networks: 212 tests in each configuration.
- WebView2: 53 contract tests in each configuration.
- Diagnostics: 30 Debug and 25 Release functional scenarios.
- A host compiled against Runtime 1.0.46 passes startup/shutdown compatibility checks with this candidate.
- Root builds and staged-payload verification are release gates. The payload checks include synchronized DLLs, sample compilation, CLI startup, legal notices and exclusion of Web/Tables.
- The [release manifest](../IWESUN_RUNTIME_1.0.47_BETA_RELEASE_MANIFEST.md) records final gate status. Gitleaks scans are limited rule-based evidence, not a guarantee that all sensitive information is absent.

These checks do not certify all real browsers, WFP/next-hop combinations, network environments or long-running loads. Beta is not a promise of production readiness.

## Upgrade and rollback

Save the previous Runtime and consuming-application packages. Coordinate host shutdown before upgrading. Select matching Debug/Release DLLs and rebuild the consumer's own deployment; the Runtime installer does not replace DLLs copied by other applications.
For portable installations, preserve business data and configuration. Run the supplied verification script with the intended app/data directories.
To roll back, stop consumers, restore the saved Runtime and application packages, confirm versions/configurations, and follow the application's own data-migration rules.

## License

The initial public release uses the bundled Iwesun Runtime Noncommercial Source-Available License 1.0. Preserve attribution, license and third-party notices. Commercial use and independent modified distributions need the applicable permission.
Previously valid grants are not retroactively revoked. Read [LICENSE](https://github.com/iwesun32/Iwesun.Runtime/blob/main/LICENSE) for the operative terms.
