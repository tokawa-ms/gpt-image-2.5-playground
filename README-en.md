<img src="src/GptImagePlayground/Assets/AppIcon.svg" width="80" alt="GPT-image-2.5 Playground icon">

# GPT-image-2.5 Playground

[日本語](README.md) | English · [MIT License](LICENSE) © 2026 tokawa-ms

A Windows desktop app for experimenting with image models on Microsoft Foundry / Azure OpenAI. Generate images from a prompt, or add reference images to edit and combine them. Built with WinUI 3 and C#, with parameters tailored to `gpt-image-2.5-sunburst`.

The app authenticates through Microsoft Entra ID using Azure CLI or Visual Studio, without API keys. It does not create Azure resources or deploy models. Check model availability, regions, and quotas in your own Azure environment.

This is an independently developed sample, not an official Microsoft or OpenAI product. The MIT license applies to this repository's code; dependencies and bundled runtimes retain their respective licenses.

## Features

- Prompt-only generation and editing with up to 16 reference images, including drag and drop and reference reordering.
- Aspect ratio presets, custom resolutions, quality, PNG / JPEG / WebP output, and background options.
- Image previews, actual output dimensions, and saving the original image data.
- Local history that preserves prompts, parameters, and reference images for reuse.
- English and Japanese UI, including validation errors, progress messages, and history details.

> [!IMPORTANT]
> Image generation and editing incur Azure charges. Cancellation stops the app from waiting; it does not guarantee that server-side generation or billing stops. Requests are never retried automatically, including after failures.

## Prerequisites

| Purpose | Requirements |
| --- | --- |
| Running the app | Windows 10 version 2004 (build 19041) or later, x64 |
| Building and testing | .NET SDK 10, Windows SDK, and MSIX build tools. A Visual Studio WinUI development environment can also be used |
| Development launch | Windows Developer Mode and package registration |
| Image generation | A compatible Azure model deployment, access permissions, and network access to the resource |
| Authentication | Azure CLI or a signed-in Visual Studio installation |

The app targets `net9.0-windows10.0.19041.0`; tests target `net10.0`. The MSIX includes .NET 9 and Windows App SDK, so end users do not need to install these runtimes separately. Review the [.NET support lifecycle](https://dotnet.microsoft.com/platform/support/policy/dotnet-core) before using the app operationally.

## Build and run

Get the source and open PowerShell at the repository root. Restoring NuGet packages requires internet access. Close any running build of the app before rebuilding to avoid locked output files.

```powershell
.\Build.ps1
```

This runs automated tests, builds Release, creates an unsigned x64 MSIX, and checks its contents. Packages are written to `artifacts\msix`; test results are written to `artifacts\tests\contracts.trx`.

It also creates a ZIP in `artifacts\distribution` containing the MSIX, READMEs, MIT license, dependency and bundled-runtime licenses and notices, and a SHA-256 checksum. Notices are collected from restored packages. Move an existing distribution folder aside before exporting the same version again. Build scripts require PowerShell 7.

To run tests and build for development separately:

```powershell
dotnet test tests\GptImagePlayground.Tests\GptImagePlayground.Tests.csproj -c Release
dotnet build src\GptImagePlayground\GptImagePlayground.csproj -c Debug -p:Platform=x64
```

After building, register the development package and launch **GPT-image-2.5 Playground** from the Start menu.

```powershell
Add-AppxPackage -Register "$PWD\src\GptImagePlayground\bin\x64\Debug\net9.0-windows10.0.19041.0\win-x64\AppxManifest.xml"
```

Run with package identity rather than launching the generated `.exe` directly. Registration points to the build folder, so do not move or delete that folder while the package is registered. Do not register Debug and Release builds of the same package simultaneously.

With WinApp CLI 0.6 or later, you can also launch using:

```powershell
winapp run .\src\GptImagePlayground\GptImagePlayground.csproj -c Debug --arch x64 --debug-output
```

## Connect to Azure

1. Deploy the target model in Foundry / Azure OpenAI and note its endpoint and deployment name. The default deployment name is `gpt-image-2.5-sunburst`.
2. Assign the user the **Cognitive Services OpenAI User** role on the resource. Allow time for role changes to propagate and check network restrictions.
3. Sign in to the resource's tenant. Azure CLI is the default credential source.

```powershell
az login --tenant YOUR_TENANT_ID
az account set --subscription YOUR_SUBSCRIPTION_ID
```

4. In **Settings**, enter the endpoint, deployment name, and credential source, then choose **Save and apply settings**. Saving alone does not generate an image or contact Azure.

Example endpoint:

```text
https://your-resource.services.ai.azure.com/openai/v1/images/generations
```

Supported endpoints are HTTPS URLs on public Azure's `.services.ai.azure.com`, `.openai.azure.com`, and `.cognitiveservices.azure.com` domains. You can use the resource root, `/openai/v1/`, or an image generation/edit URL. Project URLs, arbitrary external hosts, URLs with queries, and sovereign clouds are not supported.

`DefaultAzureCredential` uses only the selected **AzureCli** or **VisualStudio** credential source, without falling back to another source. Select VisualStudio explicitly if you want to use it. Restart the app after changing the CLI account or tenant. API keys and access tokens are not stored in settings or history.

## Usage

In **Generate / Edit**, enter a prompt and select **Generate image**. To change the size, quality, output format, or background, open **Advanced settings**, which is collapsed by default. Collapsing it preserves your selections. With no reference images, the app creates a new image. With one or more references, it performs an edit. References are sent in the displayed order; original files are never modified.

Use **Save as** in the preview panel to save an image. If generation fails, the previous result remains available. If generation succeeds but saving history fails, you can still save the new image manually.

Select an entry in **History** and open it in Generate / Edit to restore its prompt, parameters, reference images and order, and generated output. Regeneration uses the currently saved connection settings, not the connection stored in that history entry.

### Display language

Open **Settings → Language / 表示言語** and choose **English** or **日本語**. Your choice is saved automatically and takes effect after you close and restart the app. You can change it before configuring an Azure connection.

On first launch, the app uses Japanese if your Windows preferred language is Japanese, and English otherwise. Prompts, file names, API parameter values, and original service error messages are not translated. Changing the language does not change connection settings or history.

### Parameters and limits

These are the values the app validates and sends. They do not guarantee that every combination will be accepted by a particular deployment.

| Parameter | Values and limits |
| --- | --- |
| Prompt | Required; up to 32,000 characters |
| Reference images | 0–16 PNG / JPEG files, each smaller than 50,000,000 bytes |
| Size | `auto` or `WIDTHxHEIGHT`. Both dimensions must be multiples of 16 and no greater than 3840, with a 1:3–3:1 aspect ratio and 655,360–8,294,400 total pixels |
| Quality | `auto`, `low`, `medium`, `high`, `xhigh`, `max` |
| Output format | `png`, `jpeg`, `webp` |
| Background | `auto`, `opaque`, `transparent`. JPEG cannot be combined with transparency |
| Output count | One image |

Resolutions with more total pixels than 2560×1440 show an experimental high-resolution warning. Both 3840×2160 and 2160×3840 are supported. Separately, input decoding is limited to 32,768 pixels on either side and 100 million total pixels.

Without references, the app sends JSON to `/openai/v1/images/generations`. With references, it sends `multipart/form-data` to `/openai/v1/images/edits`, using `image[]` fields. There are no UI controls for masks, streaming, `input_fidelity`, `moderation`, or `output_compression`.

## Local data and publishing precautions

The Settings tab shows the data folder, typically `%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalState`.

| Location | Contents |
| --- | --- |
| Bundled `appsettings.json` | Initial defaults; the endpoint is empty |
| `settings.json` | Saved endpoint, deployment, credential source, default size and quality |
| `imports\<session-id>` | Reference image copies and thumbnails |
| `history\<id>` | Output, reference copies, prompts, connection settings, and parameters |
| Windows per-app language preference | Selected display language, independent of `settings.json` |

> [!WARNING]
> Images and prompts are stored without app-level encryption. Generation sends the prompt and reference images to Azure. Follow your organization's data handling policies. Service error messages may quote your input; review screenshots and diagnostics before publishing them.

Reference copies and history are not automatically deleted. To remove data, close the app and delete only the unwanted data in the folder shown in Settings. History contains independent copies and remains available after deleting `imports`.

The root `.gitignore` excludes build, MSIX, and test outputs; logs; `.env`; environment-specific `appsettings.*.json`; `settings.json`; copied `LocalState`, `imports`, and `history` folders; certificates and private keys; and personal publishing profiles. The default `appsettings.json` with its empty endpoint, translation resources, icons, and three bundled file-system publishing profiles remain publishable.

Do not add real connection details, credentials, generated images, or local history to the repository. `.gitignore` does not remove tracked files or earlier commits. Review staged changes and history before publishing. If credentials have been exposed, revoke and replace them rather than only deleting their files.

## Distributing an MSIX

`Build.ps1` produces an unsigned package by default. Distribution to another device requires a code-signing certificate matching the Publisher in `Package.appxmanifest` (default: `CN=AppPublisher`). Set Publisher to match the distributor's certificate. The MIT copyright holder and the Windows package signer are separate identities.

To sign the MSIX in the distribution ZIP with a certificate in the current user's certificate store:

```powershell
.\Build.ps1 -CertificateThumbprint YOUR_CERTIFICATE_THUMBPRINT
```

Private keys are never exported. `Publisher.cer` in the ZIP is the public certificate. Signatures are not timestamped, so validation and distribution are intended within the certificate's validity period. For general distribution, use a certificate trusted by recipients or consider Microsoft Store distribution.

Example signing and verification commands using Windows SDK's `signtool.exe`:

```powershell
signtool sign /fd SHA256 /sha1 YOUR_CERTIFICATE_THUMBPRINT .\path\to\GptImagePlayground.msix
signtool verify /pa /v .\path\to\GptImagePlayground.msix
```

A self-signed certificate must be explicitly trusted on test devices. Never include private keys or certificate passwords in the source.

For test distribution, recipients must independently verify the publisher and certificate thumbprint before importing `Publisher.cer` into `Cert:\LocalMachine\TrustedPeople` from an administrator PowerShell session. No automatic certificate-trust operation is included. Extract the ZIP, review and accept the bundled license terms, then install with `Add-AppxPackage -Path .\GptImagePlayground_1.0.4.0_x64.msix`. Unsigned ZIPs are for subsequent signing, not normal installation as-is.

Before distributing binaries, review the licenses and redistribution conditions of each dependency, including Windows App SDK, Windows SDK, and WebView2, and include any required copyright, license, and NOTICE files. The source code's MIT license does not replace the terms of these components.

Distribute the entire ZIP, including `ThirdPartyNotices`, which also includes notices for build-only dependencies. Use and redistribution of Microsoft redistributable components are governed by their bundled Microsoft license terms; the app's MIT license grants no additional rights to those components.

## Troubleshooting

| Symptom | What to check |
| --- | --- |
| HTTP 400 / tenant mismatch | Check the credential source and resource tenant, sign in again, and restart the app. Changing image size will not fix a tenant mismatch |
| HTTP 401 / 403 | Sign-in state, RBAC, and resource network restrictions |
| HTTP 404 | Resource URL, deployment name, and API availability |
| HTTP 429 | Quota and rate limits. Requests are not retried automatically |
| Settings cannot be loaded | Correct and save them in Settings. Generation is disabled until then |
| WebP preview is unavailable | Check the OS codec. The original image data can still be saved using Save as |
| MSIX installation fails | Check the package signature and certificate trust |

## Development

```text
src\GptImagePlayground          WinUI 3, XAML, DI, Windows imaging and file pickers
src\GptImagePlayground.Core     Models, validation, REST, authentication, settings, history, MVVM, translations
tests\GptImagePlayground.Tests  API contracts, persistence, ViewModel, and localization tests
Build.ps1                      Tests, MSIX build, and package content checks
```

Tests use fake HTTP handlers and credentials; they do not connect to Azure or incur generation charges. UI copy lives in Core's `Strings.resx` (English) and `Strings.ja.resx` (Japanese). Tests check that keys and format arguments match.

The icon source is `src\GptImagePlayground\Assets\AppIcon.svg`. The main page, taskbar, and title bar share the same artwork, with transparent PNG / ICO backgrounds. Images are copied to the build output for development package registration, and `Build.ps1` checks that all icons in both the output and MSIX match the source assets. Generated PNG / ICO files are included, so normal builds do not require Python. After changing the icon, regenerate the assets with:

```powershell
python -m pip install resvg-py==0.5.0 Pillow==11.3.0
python .\src\GptImagePlayground\Assets\GenerateIcons.py
```

For model and API specifications, see the [Azure image generation guide](https://learn.microsoft.com/azure/foundry/openai/how-to/dall-e), [v1 API and authentication](https://learn.microsoft.com/azure/foundry/openai/api-version-lifecycle), and [Azure OpenAI RBAC](https://learn.microsoft.com/azure/ai-foundry/openai/how-to/role-based-access-control).
