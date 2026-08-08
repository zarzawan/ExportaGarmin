# SignPath Foundation application draft

This document contains the non-personal answers prepared for the official
application form. It deliberately omits the maintainer's legal first name,
last name and email address.

## Form fields

**Project Name**

ExportaGarmin

**Repository URL**

https://github.com/zarzawan/ExportaGarmin

**Homepage URL**

https://github.com/zarzawan/ExportaGarmin

**Download URL**

https://github.com/zarzawan/ExportaGarmin/releases/latest

**Privacy Policy URL**

https://github.com/zarzawan/ExportaGarmin/blob/main/PRIVACY.md

**Wikipedia URL**

Leave blank. The project does not have a Wikipedia article.

**Tagline**

Your Garmin data, organized and prepared for private training analysis with
the AI tool of your choice.

**Description**

ExportaGarmin is a free and open-source portable Windows application that lets
people download data from their own Garmin Connect account and organize it for
manual review, especially while preparing for running events. It creates a
semantic text export for AI-assisted analysis and an optional human-readable
Excel report. It has no telemetry, advertising, subscriptions or automatic
uploads.

**Reputation**

ExportaGarmin is a recently established project, first published on 28 July
2026. It has ten public Windows releases, a protected main branch, automated
tests and release builds, 200 regression tests, two GitHub stars and 21 asset
downloads as of 8 August 2026. It is actively maintained, but it does not yet
have independent media coverage or a large user community. Its upstream base,
sirredbeard/garmin-data-export, was publicly released in March 2026.

**Maintainer Type**

Individual maintainer(s)

**Build System**

GitHub Actions

**Company Name**

Leave blank. ExportaGarmin is not maintained by a company.

**Primary Discovery Channel**

AI / LLM tools

**Exact source**

ChatGPT

## Additional eligibility information

ExportaGarmin is licensed under the OSI-approved Apache License 2.0. It is
free of charge and has no commercial dual licensing, advertisements,
subscriptions, donations, affiliate links or commercial purpose. The portable
package includes Python 3.11.9, .NET 10 LTS and pinned Python dependencies. All
audited components use permissive or other OSI-approved licenses; their
license metadata and notices are included in the package.

The repository is a Git descendant and derivative work of
https://github.com/sirredbeard/garmin-data-export. This provenance is stated
prominently in the README and code-signing policy and is not hidden. The
upstream project uses Apache License 2.0. The Python backend and original .NET
console layer started upstream and have been substantially extended. The
graphical Windows launcher, profile isolation, journal, race reports, automatic
privacy controls, human-readable Excel report and portable packaging are
maintained by zarzawan in ExportaGarmin. GitHub does not currently mark the
repository as a formal fork.

The requested Authenticode signature would cover only `ExportaGarmin.exe`, the
graphical launcher created and maintained in this repository. The artifact
configuration does not sign the Python runtime, Python modules, `.pyd` files,
.NET or Python third-party components, or binaries released by the upstream
project. SignPath Foundation should decide whether this limited signing scope
is acceptable in view of the upstream relationship.

The program connects to Garmin Connect only when the user explicitly logs in,
checks a saved session or requests an export. Garmin does not provide a public
personal API for this purpose, so the open-source `garminconnect` and `garth`
libraries reproduce Garmin Connect requests. Session tokens and downloaded
data stay under the user's Windows account.

The program does not send data automatically to ChatGPT, Claude, NotebookLM,
Google Drive or any other AI or storage service. Users manually decide whether
to upload an exported file. There is no project-operated server or telemetry.

Official releases are built from public source by GitHub Actions on
GitHub-hosted Windows runners. The workflow installs pinned dependencies,
verifies the fixed Python runtime SHA-256, runs the full test suite, publishes a
self-contained .NET launcher and creates a portable ZIP plus SHA-256. After
acceptance, the unsigned ZIP will be uploaded as a GitHub Actions artifact,
submitted through SignPath's official GitHub Action, manually approved,
timestamped, verified and only then published as the final Release asset.

**Project roles**

- Committer and maintainer: GitHub user `zarzawan`.
- Reviewer: GitHub user `zarzawan`; external contributions require pull
  requests and review.
- Signing approver: GitHub user `zarzawan`; every signing request will require
  manual approval.

The maintainer must confirm separately that MFA is enabled for GitHub and will
enable MFA for the SignPath account.

## Personal fields still required

- First name for the individual SignPath account.
- Last name for the individual SignPath account.
- Email address for account creation and application notifications.
- Explicit confirmation that GitHub MFA is enabled.
- Explicit acceptance of the SignPath Foundation Code of Conduct and the
  required processing of personal data.

The optional marketing-consent checkbox should remain unchecked.
