---
name: release
description: Cut a RocksDb.Net release - pre-flight checks, tag, rehearse the pipeline without publishing, create the GitHub Release, then verify the package actually reached nuget.org. Use when the user says "release", "cut a release", "publish vX.Y.Z", "test the release pipeline", or invokes /release.
---

# Releasing RocksDb.Net

The package version is `<RocksDbVersion>.<Revision>`, so `11.8.1.1` wraps RocksDb
11.8.1. Breaking changes land only when `RocksDbVersion` changes; a revision bump
never breaks source or binary compatibility. See `CHANGELOG.md` under Versioning.

**A push to nuget.org cannot be undone.** A version can be unlisted but never
deleted or reused, so a mistaken push burns that version string permanently.
Everything below is arranged around that one fact.

## Before anything else

1. **`RocksDb.Net.Runtimes` must already be published** for this version. The
   wrapper declares a bounded dependency (e.g. `[11.8.1.2, 11.8.2)`), so if the
   runtimes package is missing the release installs but cannot load. Check it,
   and check the range is satisfiable:

   ```bash
   curl -s https://api.nuget.org/v3-flatcontainer/rocksdb.net.runtimes/index.json
   curl -s https://api.nuget.org/v3-flatcontainer/rocksdb.net/index.json   # target must be absent
   ```

2. **The `nuget-release` environment must exist with required reviewers.** The
   `publish` job is gated on it. If the environment does *not* exist, GitHub
   creates it implicitly on first use **with no protection rules**, and the job
   runs unimpeded — so verify rather than assume:

   ```bash
   gh api repos/zcsizmadia/RocksDb.Net/environments/nuget-release \
     --jq '[.protection_rules[] | {type, reviewers: [.reviewers[]?.reviewer.login]}]'
   ```

   To create it: `PUT .../environments/nuget-release` with
   `{"reviewers":[{"type":"User","id":<user id>}]}`.

## Pre-flight checks, locally

```bash
dotnet build -c Release --no-incremental -warnaserror     # must be 0 warnings
dotnet test -c Release --no-build                         # all TFMs: net8.0/9.0/10.0
dotnet docfx docs/docfx.json --warningsAsErrors           # proves every cref resolves
```

`--no-incremental` matters: analyzers do not re-run on an incremental build, so
analyzer errors reach CI otherwise.

**actionlint only *integrates* with shellcheck, it does not bundle it.** Without
the shellcheck binary on PATH, half the workflow lint is silently skipped and
the run still exits 0. Put it on PATH first, then sanity-check that it is
actually wired in by reverting a known issue and confirming actionlint reports
it:

```bash
curl -fsSL -o shellcheck.zip https://github.com/koalaman/shellcheck/releases/download/v0.10.0/shellcheck-v0.10.0.zip
unzip -o -q shellcheck.zip && export PATH="$PWD:$PATH"
./actionlint -no-color -oneline    # version pinned in .github/workflows/lint.yml
```

Markdown lint (`npx markdownlint-cli2@0.23.2`) may be unreachable if npm points
at a private registry; CI runs it regardless.

## Step 1 - version, and regenerate

Bump `RocksDbVersion` in `Directory.Build.props` **first**, then regenerate:

```bash
dotnet run --project NativeMethodsGenerator
```

The generator reads the version from `Directory.Build.props` and downloads the
headers, so bumping first and regenerating is the whole upgrade sequence. Never
hand-edit `RocksDb.Net/Native/NativeMethods.g.cs` — fix the generator instead.
`PinnedVersionTests` fails if the generated files and the prop disagree.

`.github/workflows/lint.yml` also checks the README badge, the binding count in
the README and in `docs/api/index.md`, and stale version strings in markdown.

## Step 2 - land everything on main and wait for green

`build.yml` triggers on **push to main and pull_request only — not on tags**.
The release downloads the package artifact pinned to the tagged **commit SHA**,
so the tag must point at a commit main has already built. Tagging alone produces
no artifact.

```bash
gh api repos/zcsizmadia/RocksDb.Net/commits/main --jq .sha
gh run list --workflow=build.yml --limit 5 --json headSha,status,conclusion
gh api 'repos/zcsizmadia/RocksDb.Net/actions/artifacts?per_page=100' \
  --jq '[.artifacts[] | select(.name=="nuget-package" and .expired==false and .workflow_run.head_sha=="<sha>")] | length'
```

## Step 3 - tag

The tag must equal the project version exactly; a leading `v` is stripped:

```bash
dotnet msbuild RocksDb.Net/ -getProperty:Version -nologo -v:q    # e.g. 11.8.1.1
git tag v11.8.1.1 <sha> && git push origin v11.8.1.1
```

Pushing a tag triggers **nothing** — the Release workflow fires on
`release: published` — so tagging is safe on its own.

**Prerelease tags do not work yet.** `build.yml` packs with no `VersionSuffix`,
so the artifact is `RocksDb.Net.11.8.1.1.nupkg` while a `-preview.1` tag says
otherwise, and the run fails at "Check nupkg version". Fixing prerelease
packaging means passing `VersionSuffix` through `build.yml`.

## Step 4 - rehearse, publishing nothing

```bash
gh workflow run release.yml -f tag=v11.8.1.1 -f publish=false
```

Then read the steps, not just the conclusion:

```bash
id=$(gh run list --workflow=release.yml --limit 1 --json databaseId --jq '.[0].databaseId')
gh api "repos/zcsizmadia/RocksDb.Net/actions/runs/$id/jobs" \
  --jq '.jobs[] | "JOB \(.name): \(.conclusion)", (.steps[] | "  \(.number). \(.name) -> \(.conclusion)")'
```

Expected: `validate` all green, "Attach Assets to GitHub Release" **skipped**
(there is no release), and **`publish` skipped**. If `publish` shows `waiting`
rather than `skipped`, the job-level `if:` is wrong — a rehearsal should never
reach the environment gate at all. The gate is the backstop, not the mechanism.

Optionally prove the version guard fires, by tagging a version the project does
not declare and dispatching against it. **Tell the user first** — it leaves a
red run in the Actions history, which is alarming to find unexplained. Delete
the tag afterwards.

## Step 5 - create the GitHub Release

This is what triggers the real thing. Write notes covering breaking changes with
migrations, highlights, and requirements; link `CHANGELOG.md`.

```bash
gh release create v11.8.1.1 --title "v11.8.1.1" --notes-file notes.md --latest
```

**The workflow definition for a `release` event is read from the default
branch**, not from the tagged commit. So a modified `release.yml` on a branch
changes nothing about a release — any pipeline change must be merged to main
before it takes effect.

## Step 6 - approve the gate

`validate` runs, attaches the `.nupkg` to the release, and then `publish` holds
at `nuget-release`, showing "Waiting for review". **Nothing is published until a
reviewer approves.**

Do not approve on the user's behalf unless they explicitly ask. The gate exists
so a human decides at the irreversible step; performing the release and
approving it yourself makes the control decorative. Point them at the run and
let them click **Review deployments** -> tick `nuget-release` -> **Approve and
deploy**.

Cancelling instead leaves the tag, the release and the attached asset in place
with nothing published, and the same tag can be re-run later.

## Step 7 - verify, and do not trust the green tick

`dotnet nuget push --skip-duplicate` **reports success without publishing** when
the version already exists, so a green job is not proof. Read the push output
and look for `Created` (HTTP 201):

```bash
jid=$(gh api "repos/zcsizmadia/RocksDb.Net/actions/runs/$id/jobs" \
  --jq '.jobs[] | select(.name=="publish") | .id')
gh api "repos/zcsizmadia/RocksDb.Net/actions/jobs/$jid/logs" --allow-escape-sequences \
  | tr -d '\r' | grep -iE "pushing|created|pushed|already exists"
```

`Created https://www.nuget.org/api/v2/package/` means a real upload.
"already exists" means nothing was published.

Then confirm it is indexed. nuget.org runs validation before a version becomes
listed and resolvable, so expect several minutes of lag — an absent version
immediately after a successful push is normal, not a failure:

```bash
curl -s https://api.nuget.org/v3-flatcontainer/rocksdb.net/index.json
```

## Facts that cost time to learn

- A `release` event reads the workflow from the **default branch**.
- `build.yml` does not run on tags; artifacts are fetched by commit SHA.
- An environment with no protection rules does not gate anything, and GitHub
  creates missing environments silently.
- `--skip-duplicate` can turn a no-op into a green tick.
- actionlint without shellcheck skips the shell half of the lint, quietly.
- nuget.org index lag is not a failed publish.
- `can_admins_bypass` defaults to true on the environment; set it false if an
  admin should not be able to skip the wait.
- `NUGET_API_KEY` is a repository secret. Moving it to a `nuget-release`
  environment secret would stop every other job being able to read it.
