# Third-party notices

The candidate uses .NET 10 and Windows Forms, published as a self-contained Windows x64 executable. The initial local SDK is 10.0.400 with runtime patch 10.0.11. Exact versions are recorded with each build; the SDK is a developer tool and is not distributed in the portable app.

| Component | Notice |
| --- | --- |
| .NET runtime | [Upstream licence](https://github.com/dotnet/runtime/blob/v10.0.11/LICENSE.TXT), plus bundled notices |
| Windows Forms / Windows Desktop runtime | [Upstream licence](https://github.com/dotnet/winforms/blob/v10.0.11/LICENSE.TXT), plus applicable notices |
| Windows system fonts | Used from the operating system; no font files are redistributed |
| Official Codex | External prerequisite, not bundled or relicensed by this project |
| GitHub Actions | Build-time official actions, not part of the portable app |

Preserve the verbatim files in the portable package's licenses/ directory:

- DOTNET-LICENSE.txt
- DOTNET-THIRD-PARTY-NOTICES.txt
- WINDOWSDESKTOP-LICENSE.txt
- MICROSOFT-THIRD-PARTY-NOTICES.txt

These notices came from the existing local runtime/tool distribution. The proposed project MIT licence does not replace any third-party terms. The exact files remain the authoritative notices for the included components.

No external icon package or redistributed font is introduced. Code-drawn product controls do not imply OpenAI affiliation. OpenAI and Codex names identify the compatible external product.

Official actions are pinned to verified release commits:

- [actions/checkout v4.2.2](https://github.com/actions/checkout/commit/11bd71901bbe5b1630ceea73d27597364c9af683)
- [actions/setup-dotnet v4.3.1](https://github.com/actions/setup-dotnet/commit/67a3573c9a986a3f9c594539f4ab511d57bb3ce9)
- [actions/upload-artifact v4.6.2](https://github.com/actions/upload-artifact/commit/ea165f8d65b6e75b540449e92b4886f43607fa02)

Their repository licences govern their use. Listing them does not claim a workflow run or public publication.
