<img src="src/GptImagePlayground/Assets/AppIcon.svg" width="80" alt="GPT-image-2.5 Playground アイコン">

# GPT-image-2.5 Playground

日本語 | [English](README-en.md) · [MIT License](LICENSE) © 2026 tokawa-ms

Microsoft Foundry / Azure OpenAI の画像モデルを試すための Windows デスクトップアプリです。プロンプトだけで画像を生成したり、参照画像を追加して編集・合成したりできます。`gpt-image-2.5-sunburst` 向けのパラメーターに対応し、WinUI 3 と C# で実装しています。

API キーは使わず、Azure CLI または Visual Studio の Microsoft Entra ID 資格情報で接続します。Azure リソースの作成やモデルのデプロイは行いません。利用できるモデル・リージョン・クォータは、ご自身の Azure 環境で確認してください。

個人で開発しているサンプルであり、Microsoft または OpenAI の公式製品ではありません。MIT ライセンスはこのリポジトリのコードに適用され、依存ライブラリや同梱ランタイムには各提供元のライセンスが適用されます。

## できること

- プロンプトからの画像生成と、最大16枚の参照画像を使った編集。ドラッグ＆ドロップと参照順序の変更に対応。
- 縦横比プリセット、カスタム解像度、画質、PNG / JPEG / WebP、背景の指定。
- 生成画像のプレビュー、実寸の表示、元の画像データの保存。
- プロンプト・パラメーター・参照画像を含むローカル履歴の保存と復元。
- 日本語・英語の UI。入力エラー、進捗、履歴詳細も選択した言語で表示。

> [!IMPORTANT]
> 画像生成・編集には Azure の利用料金が発生します。キャンセルはアプリ側の待機を中断するもので、サーバー側の生成や課金の停止を保証しません。失敗時を含め、リクエストの自動再送は行いません。

## 必要な環境

| 用途 | 必要なもの |
| --- | --- |
| 実行 | Windows 10 version 2004（build 19041）以降、x64 |
| ビルド・テスト | .NET SDK 10、Windows SDK と MSIX ビルドツール。Visual Studio の WinUI 開発環境も利用できます |
| 開発中の起動 | Windows の開発者モードと、パッケージ登録 |
| 画像生成 | 対応モデルの Azure デプロイ、利用者のアクセス権、リソースへのネットワーク接続 |
| 認証 | Azure CLI、またはサインイン済みの Visual Studio |

アプリは `net9.0-windows10.0.19041.0`、テストは `net10.0` をターゲットにしています。MSIX には .NET 9 と Windows App SDK を同梱するため、利用者がそれらのランタイムを別途インストールする必要はありません。運用に使う場合は [.NET のサポート期間](https://dotnet.microsoft.com/platform/support/policy/dotnet-core)も確認してください。

## ビルドと起動

ソースを取得し、リポジトリのルートで PowerShell を開きます。NuGet パッケージの復元にはインターネット接続が必要です。以前のビルドを起動している場合は、ファイルのロックを避けるためアプリを閉じてからビルドしてください。

```powershell
.\Build.ps1
```

自動テスト、Release ビルド、署名なし x64 MSIX の作成、パッケージ内容の検査を実行します。成果物は `artifacts\msix`、テスト結果は `artifacts\tests\contracts.trx` に出力します。

さらに `artifacts\distribution` に、MSIX、README、MIT ライセンス、依存パッケージと同梱ランタイムのライセンス・通知、SHA-256 をまとめた ZIP を作成します。依存情報は復元済みのパッケージから収集します。再実行時は同じ名前の配布フォルダーを移動しておいてください。ビルドスクリプトには PowerShell 7 を使用します。

開発用ビルドとテストを個別に実行する場合:

```powershell
dotnet test tests\GptImagePlayground.Tests\GptImagePlayground.Tests.csproj -c Release
dotnet build src\GptImagePlayground\GptImagePlayground.csproj -c Debug -p:Platform=x64
```

ビルド後、開発パッケージを登録し、スタートメニューの **GPT-image-2.5 Playground** から起動します。

```powershell
Add-AppxPackage -Register "$PWD\src\GptImagePlayground\bin\x64\Debug\net9.0-windows10.0.19041.0\win-x64\AppxManifest.xml"
```

生成された `.exe` を単独で起動せず、パッケージ ID 付きで実行してください。登録したビルドフォルダーはインストール先として参照されるため、登録中は移動・削除しないでください。同じパッケージの Debug / Release を同時に登録しないでください。

WinApp CLI 0.6 以降がある場合は、次の方法でも起動できます。

```powershell
winapp run .\src\GptImagePlayground\GptImagePlayground.csproj -c Debug --arch x64 --debug-output
```

## Azure 接続

1. Foundry / Azure OpenAI で対象モデルをデプロイし、エンドポイントとデプロイ名を確認します。既定のデプロイ名は `gpt-image-2.5-sunburst` です。
2. 対象リソースで利用者に **Cognitive Services OpenAI User** ロールを付与します。ネットワーク制限やロール反映の待ち時間にも注意してください。
3. リソースと同じテナントにサインインします。既定の認証元は Azure CLI です。

```powershell
az login --tenant YOUR_TENANT_ID
az account set --subscription YOUR_SUBSCRIPTION_ID
```

4. アプリの「設定」で、エンドポイント、デプロイ名、認証元を指定し、「設定を保存して適用」を押します。保存だけでは生成や Azure への通信は行いません。

エンドポイントの例:

```text
https://your-resource.services.ai.azure.com/openai/v1/images/generations
```

公開 Azure の `.services.ai.azure.com`、`.openai.azure.com`、`.cognitiveservices.azure.com` の HTTPS URL に対応します。リソースのルート、`/openai/v1/`、画像生成・編集 URL を受け付けます。プロジェクト URL、任意の外部ホスト、クエリ付き URL、ソブリンクラウドには対応していません。

`DefaultAzureCredential` は選択した **AzureCli** または **VisualStudio** だけを使用し、他の認証元へフォールバックしません。Visual Studio を使う場合は認証元を明示的に変更してください。CLI のアカウントやテナントを変更した後はアプリを再起動します。API キーやアクセストークンは設定・履歴に保存しません。

## 使い方

「生成・編集」でプロンプトを入力し、「画像を生成」を押します。サイズ・画質・出力形式・背景を変更する場合は、初期状態では折りたたまれている「詳細設定」を開きます。折りたたんでも設定値は保持されます。参照画像を追加しなければ新規生成、1枚以上追加すると編集になります。参照画像は表示順に送信され、元のファイルは変更しません。

生成後はプレビューの「名前を付けて保存」から画像を保存できます。生成に失敗した場合は前の結果を保持します。生成後の履歴保存だけが失敗した場合も、新しい画像を手動で保存できます。

「履歴」で項目を選び、生成・編集画面に開くと、プロンプト、パラメーター、参照画像と順序、生成結果を復元します。再生成には履歴の接続先ではなく、現在保存されている接続設定を使います。

### 表示言語

「設定」→「表示言語 / Language」で **日本語** または **English** を選択します。選択は自動保存され、アプリを閉じて再起動すると反映されます。Azure 接続が未設定でも変更できます。

初回起動時は Windows の優先言語が日本語なら日本語、それ以外は英語です。プロンプト、ファイル名、API のパラメーター値、サービスが返すエラーの原文は翻訳しません。言語を変更しても接続設定や履歴は変更されません。

### パラメーターと制限

以下はこのアプリが検証・送信する値です。すべての組み合わせを実サービスが受け付けることを保証するものではありません。

| 項目 | 値・制限 |
| --- | --- |
| プロンプト | 必須、最大32,000文字 |
| 参照画像 | 0～16枚、PNG / JPEG、各50,000,000 bytes未満 |
| サイズ | `auto` または `WIDTHxHEIGHT`。各辺16の倍数・3840以下、縦横比1:3～3:1、総画素数655,360～8,294,400 |
| 画質 | `auto`, `low`, `medium`, `high`, `xhigh`, `max` |
| 出力形式 | `png`, `jpeg`, `webp` |
| 背景 | `auto`, `opaque`, `transparent`。透明背景と JPEG の併用不可 |
| 出力枚数 | 1枚 |

2560×1440を超える総画素数は実験的な高解像度として注意を表示します。3840×2160と2160×3840の両方に対応します。入力画像には、これとは別に各辺32,768 px・総画素数1億以下の読み込み制限があります。

参照画像なしでは `/openai/v1/images/generations` に JSON、ありでは `/openai/v1/images/edits` に `multipart/form-data` を送信します。編集時の画像フィールドは `image[]` です。マスク、ストリーミング、`input_fidelity`、`moderation`、`output_compression` の指定 UI はありません。

## データの保存と公開時の注意

データ保存先は設定画面に表示されます。通常は `%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalState` です。

| 保存先 | 内容 |
| --- | --- |
| 同梱 `appsettings.json` | 初期値。エンドポイントは空 |
| `settings.json` | 保存した接続先、デプロイ名、認証元、既定のサイズ・画質 |
| `imports\<session-id>` | 参照画像のコピーとサムネイル |
| `history\<id>` | 出力、参照画像コピー、プロンプト、接続設定、パラメーター |
| Windows のアプリ別言語設定 | 選択した表示言語。`settings.json` とは独立 |

> [!WARNING]
> 画像とプロンプトはアプリ独自には暗号化せず保存します。生成時にはプロンプトと参照画像を Azure に送信します。組織のデータ取り扱い方針を確認してください。サービスのエラーメッセージに入力内容が含まれる場合もあるため、スクリーンショットや診断情報を公開する前に内容を確認してください。

参照画像のコピーと履歴は自動削除しません。削除する場合はアプリを閉じ、設定画面で確認した不要なデータだけを削除してください。履歴は独立したコピーなので、`imports` を削除しても残ります。

ルートの `.gitignore` は、ビルド・MSIX・テスト成果物、ログ、`.env`、環境別 `appsettings.*.json`、`settings.json`、コピーした `LocalState` / `imports` / `history`、証明書・秘密鍵、個人用の発行プロファイルを除外します。空のエンドポイントを含む `appsettings.json`、翻訳リソース、アイコン、同梱の3つのファイルシステム発行プロファイルは公開対象に残します。

実際の接続先、認証情報、生成画像、ローカル履歴はリポジトリに追加しないでください。`.gitignore` はすでに追跡されているファイルや過去のコミットには効きません。公開前にステージ済みの差分と履歴も確認し、漏えいした認証情報がある場合は削除だけでなく失効・再発行してください。

## MSIX の配布

`Build.ps1` の既定の出力は署名なしです。別の端末への配布には、`Package.appxmanifest` の Publisher（既定: `CN=AppPublisher`）と一致するコード署名証明書が必要です。配布者の証明書に合わせて Publisher を設定してください。MIT の著作権者と Windows パッケージの署名者は別の情報です。

現在のユーザーの証明書ストアにある証明書で、配布 ZIP 内の MSIX に署名する場合:

```powershell
.\Build.ps1 -CertificateThumbprint YOUR_CERTIFICATE_THUMBPRINT
```

秘密鍵はエクスポートしません。ZIP 内の `Publisher.cer` は公開証明書です。署名にタイムスタンプは付けないため、証明書の期限内での検証・配布を想定しています。一般配布には、配布先で信頼される発行元の証明書または Microsoft Store での配布を検討してください。

Windows SDK の `signtool.exe` で署名・検証する例:

```powershell
signtool sign /fd SHA256 /sha1 YOUR_CERTIFICATE_THUMBPRINT .\path\to\GptImagePlayground.msix
signtool verify /pa /v .\path\to\GptImagePlayground.msix
```

自己署名証明書はテスト端末で明示的に信頼する必要があります。秘密鍵や証明書のパスワードをソースに含めないでください。

テスト配布では、受領者が発行元と証明書の拇印を別の経路で確認してから、管理者 PowerShell で `Publisher.cer` を `Cert:\LocalMachine\TrustedPeople` にインポートします。自動で証明書を信頼する処理は含めていません。ZIP を展開し、同梱のライセンス条項を確認・同意したうえで、`Add-AppxPackage -Path .\GptImagePlayground_1.0.4.0_x64.msix` でインストールできます。署名なし ZIP は署名作業用であり、そのまま通常インストールすることはできません。

バイナリを配布する際は、Windows App SDK / Windows SDK / WebView2 を含む各依存コンポーネントのライセンスと再配布条件を確認し、必要な著作権表示・ライセンス・NOTICE を添付してください。ソースコードの MIT ライセンスだけで依存コンポーネントの条件を置き換えることはできません。

配布時は `ThirdPartyNotices` を含む ZIP 全体を渡してください。このフォルダーにはビルド時のみ使用する依存関係の通知も含めています。Microsoft の再配布コンポーネントの利用・再配布には同梱の Microsoft ライセンス条項が適用され、アプリの MIT ライセンスによって権利が追加されるものではありません。

## トラブルシューティング

| 症状 | 確認すること |
| --- | --- |
| HTTP 400 / テナント不一致 | 認証元とリソースのテナントを確認して再サインインし、アプリを再起動。画像サイズの変更では解消しません |
| HTTP 401 / 403 | サインイン状態、RBAC、リソースのネットワーク制限 |
| HTTP 404 | リソース URL、デプロイ名、対象 API の提供状況 |
| HTTP 429 | クォータとレート制限。自動再送はありません |
| 設定を読み込めない | 設定画面から修正して保存。修正まで生成は無効です |
| WebP のプレビューが表示されない | OS のコーデックを確認。画像データは「名前を付けて保存」で保存できます |
| MSIX をインストールできない | パッケージの署名と証明書の信頼を確認 |

## 開発

```text
src\GptImagePlayground          WinUI 3、XAML、DI、Windows の画像処理とピッカー
src\GptImagePlayground.Core     モデル、入力検証、REST、認証、設定、履歴、MVVM、翻訳リソース
tests\GptImagePlayground.Tests  API 契約、保存処理、ViewModel、ローカライズのテスト
Build.ps1                      テスト、MSIX ビルド、パッケージ内容の検査
```

テストは偽の HTTP ハンドラーと資格情報を使用し、Azure に接続せず、生成料金も発生しません。UI 文言は Core の `Strings.resx`（英語）と `Strings.ja.resx`（日本語）で管理します。キーと書式引数の一致をテストで確認しています。

アイコン原本は `src\GptImagePlayground\Assets\AppIcon.svg` です。メイン画面、タスクバー、タイトルバーには同じ絵柄を使用し、PNG / ICO の背景は透過です。開発パッケージの登録でも表示できるように画像をビルド出力へコピーし、`Build.ps1` で出力先と MSIX 内の全アイコンが原本と一致することを検査します。PNG / ICO は同梱済みで、通常のビルドに Python は不要です。アイコンを変更した場合は次で再生成できます。

```powershell
python -m pip install resvg-py==0.5.0 Pillow==11.3.0
python .\src\GptImagePlayground\Assets\GenerateIcons.py
```

モデルや API の仕様は [Azure の画像生成ガイド](https://learn.microsoft.com/azure/foundry/openai/how-to/dall-e)、[v1 API と認証](https://learn.microsoft.com/azure/foundry/openai/api-version-lifecycle)、[Azure OpenAI の RBAC](https://learn.microsoft.com/azure/ai-foundry/openai/how-to/role-based-access-control)を参照してください。
