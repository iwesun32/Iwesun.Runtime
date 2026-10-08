# Contributing and AI maintenance

[中文](../../CONTRIBUTING.md) · [User guide](USER_GUIDE.md)

The owner and public brand are **iwesun**; the GitHub account is `iwesun32`.
Authorized GPT/Dot assistants help triage issues, review changes and process applications. Public replies identify themselves as “AI maintenance agent for iwesun”. This does not imply OpenAI sponsorship.

## Participate

1. Read the operative [LICENSE](../../LICENSE), [governance](../../GOVERNANCE.md), [contribution rules](../../CONTRIBUTING.md) and [code of conduct](../../CODE_OF_CONDUCT.md). Governance documents are currently Chinese; this page is an English navigation summary, not a replacement grant.
2. Apply using the [working-group form](https://github.com/iwesun32/Iwesun.Runtime/issues/new?template=working-group.yml). Describe your area, relevant experience and contribution plan, and explicitly accept the rules.
3. Approved participants work through Issues and PRs. Joining does not grant write/admin access, employment, commercial permission or independent distribution rights.
4. For contributions, describe the problem, scope, compatibility impact and tests. Each author must explicitly accept the [CLA](../../CLA.md); a PR checkbox does not bind other authors automatically.

Keep identifiers, comments and XML documentation in English. Maintain Chinese and English public onboarding pages together when their behavior changes. Detailed design documents currently use Chinese.

## Independent derivatives and security

Use the [derivative-release form](https://github.com/iwesun32/Iwesun.Runtime/issues/new?template=derivative-release.yml) and follow [FORK_POLICY.md](../../FORK_POLICY.md). Specify the source repository, upstream baseline, version scope, modifications, noncommercial use and distribution plan. Written approval is required; labels or silence are not approval.
Commercial permission, license exceptions and privileged access remain the owner's decisions.
Report vulnerabilities through [private security reporting](https://github.com/iwesun32/Iwesun.Runtime/security/advisories/new), not public issues. If unavailable, ask for a private channel without revealing exploit details or secrets.

## Maintenance availability

A local maintenance check is configured every six hours, subject to the computer, app, credentials and network being available. It is not an always-online cloud service.
Unchanged states do not generate repeated comments. Third-party requests cannot expand agent privileges or authorize arbitrary code execution.

Hosted CI is not enabled: the current GitHub authorization cannot write workflow files. `scripts/ci/validate.yml.example` supplies the Debug/Release validation workflow for later owner-authorized activation. Release checks in this version are local execution evidence, not a claim of passing GitHub Actions.
