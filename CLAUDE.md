# MintPlayer.AspNetCore.Tools

Rules for this repository. The CodeCoverage App's documentation lives in MintPlayer/MintPlayer.Spark,
under `docs/code-coverage/` (`backports.md`, `merge-queue.md`, `versioning.md`).

## Platform

- `master` targets **.NET 11 GA only**: `<TargetFramework>net11.0</TargetFramework>`, `global.json` SDK
  `11.0.100`, `Microsoft.*` framework packages `11.0.x`. No prerelease SDKs or packages, and no
  multi-targeting.
- .NET 10 is served by the **`release/dotnet-10.x`** line (`net10.0` only, SDK 10.0.x, packages 10.0.x).
  Don't add `net10.0` back to master.

## Releases and release branches: the CodeCoverage App (coverage.mintplayer.com)

The App (coverageproduction[bot]) owns the `release/dotnet-<major>.x` branches, the version tags
(`<PackageId>/v<version>`) and publishing.

- **Never push to a `release/*` branch, and never create, move or delete version tags by hand.** The App's
  ruleset refuses pushes from people, and the versions come from the App's tags.
- **The App cherry-picks the squash commit of a merged pull request into the release branches, based on
  the pull request's `backport: <line>` labels.** For example, `backport: dotnet-10.x` backports to
  `release/dotnet-10.x`. Don't open your own cherry-pick pull requests into a line for something that is
  on master.
  - When a pull request into master opens, the App labels it for each supported line it applies to.
    **Check those labels on every pull request you open:**
    - Keep the label of each line the change should reach: fixes, build and versioning changes.
    - Remove the label of each line it must not reach, for example a new feature that line shouldn't
      get.
  - Platform values (TFM, `Microsoft.*` framework package versions, `global.json` SDK, `dotnet-version`
    in workflows) are normalized to the line's own values during the backport. A platform move on master
    therefore never lands on an older line.
  - Missing label: add `backport: <line>` to the pull request, even after it merged, or comment
    `/backport <line>`. Both need write access.
  - Pull requests opened by a bot (onboarding, dependency updates) get no labels. Label them by hand when
    a line needs the change.
  - **Exception:** a pull request straight into a line is only for changes unique to that line, such as
    narrowing its TFM. Merge it through the queue with `/queue`.
- **Merge** through the merge queue: comment `/queue`, or label the pull request `merge-queue`.
- **Releasing** the current line (`release/dotnet-11.x`): a maintainer comments `/release` on master. Older
  lines release when a backport merges into them. Master itself publishes nothing.
- From Git Bash, prefix `gh … --body "/queue"` (and every other `/command`) with `MSYS_NO_PATHCONV=1`.
  Otherwise MSYS rewrites the leading slash to a Windows path.

## Versioning

- `MintPlayer.Versioning.MSBuild` (in `Directory.Build.props`) sets each package's version from its tags.
  Don't add `<Version>` to a project, and don't touch the `versioning:` section of `coverage.yml`.
- Dependency updates arrive as App pull requests from `mintplayer/dependencies`. Approve the groups waiting
  there with an `approve <group>` comment.
