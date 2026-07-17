# Unity Project Inventory Tool

Unityプロジェクト内の **VPM / UPM / Built-in Package / Editorツール / Assets** を棚卸しするために作成した、個人用のUnity Editor拡張です。

自分のUnity／VRChatプロジェクト管理用に作成したものを、参考・再利用目的で公開しています。

> [!IMPORTANT]
> 本ツールは現状有姿で提供されます。動作保証、個別の導入支援、環境固有の問題調査、継続的な更新や機能追加は保証していません。利用は自己責任でお願いします。

## Target environment

- Unity 2022.3系
- VRCSDK / VPMを使用するUnityプロジェクト
- 開発時の基準環境: Unity 2022.3.22f1

他のUnityバージョンでも動作する可能性はありますが、網羅的な検証はしていません。

## Features

- `Packages/vpm-manifest.json`からVPMの直接依存・間接依存・ロック情報を取得
- Unity Package ManagerからUPMとBuilt-in Packageを取得
- `Assets`配下をフォルダ単位で集計
- Script、Editor Script、Prefab、Material、Shader、Texture、Plugin、asmdefを集計
- `Editor`フォルダ、`using UnityEditor`、`MenuItem`からEditorツールを推定
- VPM / UPM / Built-in / Editor Tool / Asset等をカテゴリ単位または項目単位で選択
- 選択した項目だけをCSV / JSONへ出力
- Google Apps Script経由で選択項目をGoogleスプレッドシートへ送信
- PackageInfoと`package.json`からDocumentation、Repository、Homepage、Author、Changelog、Licenseを取得
- URLが取得できないAssetには、公式URLを推測せず手動検索リンクだけを作成

## Installation

1. このリポジトリの`Assets/ProjectInventory`を、対象Unityプロジェクトの`Assets`へコピーします。
2. Unityのコンパイル完了後、`Tools > Project Inventory`を開きます。
3. `Refresh / Rescan`を押します。

更新時は、対象プロジェクト内の既存`Assets/ProjectInventory`を削除してから新しいフォルダへ置き換えてください。

## Selection and export

`Export / upload selection`から以下を操作できます。

- `Select all` / `Clear all`
- `Select visible` / `Clear visible` / `Invert visible`
- カテゴリ単位の一括選択
- 一覧左端の`Use`による項目単位の選択

初期選択:

- ON: VPM Package、UPM Package、Editor Tool、Imported Asset / Unknown、Scan Warning
- OFF: Built-in Package、Project Content

カテゴリの選択状態は`EditorPrefs`へ保存されます。CSV、JSON、Google Sheetsにはチェック済み項目だけが出力されます。

## Link policy

検索結果の上位ページを自動的に「公式URL」として登録することはありません。

直接リンク候補として使うのは以下です。

1. Unity Package Managerの`PackageInfo`に含まれるURL
2. インストール済みパッケージ直下の`package.json`に含まれるURL
3. `Packages/manifest.json`に明示されたGit依存URL
4. `com.unity.*`に対するUnity公式ドキュメントの決定的なURL形式

> [!WARNING]
> パッケージメタデータ内のURLは、そのパッケージ提供者が記述した値であり、本ツールが独立に安全性や正当性を検証したものではありません。Unity画面では完全なURLを確認し、Google Sheetsではリンク先ドメインを表示します。開く前にドメインを確認してください。

`.unitypackage`由来と思われるAssetにURLメタデータがない場合は、Google / GitHub / BOOTHの手動検索リンクだけを作成します。検索先は検証済みURLとして扱いません。

## Google Sheets setup

1. 出力先のGoogleスプレッドシートを作成します。
2. URLの`/d/`と`/edit`の間にあるSpreadsheet IDをコピーします。
3. Google Apps Scriptで新規プロジェクトを作成し、`GoogleAppsScript/Code.gs`を貼り付けます。
4. 以下を自分の値へ変更します。

```javascript
SPREADSHEET_ID: 'YOUR_SPREADSHEET_ID',
SHARED_SECRET: 'A_LONG_RANDOM_SECRET'
```

5. Apps Scriptをウェブアプリとしてデプロイします。
   - 実行するユーザー: 自分
   - アクセスできるユーザー: 全員
6. 発行された`/exec` URLをUnity側の`Web App URL`へ入力します。
7. 同じ秘密文字列をUnity側の`Shared secret`へ入力します。
8. 送信対象を選択し、`Upload ... selected item(s) to Google Sheets`を押します。

Apps Scriptを変更した場合は、`デプロイ > デプロイを管理 > 編集 > 新バージョン`から再デプロイしてください。

`/exec` URLをブラウザで開き、次のJSONが表示されれば入口は稼働しています。

```json
{"ok":true,"message":"Unity Project Inventory webhook is active."}
```

## Security and privacy

公開リポジトリ、Issue、スクリーンショット、チャット等へ以下を載せないでください。

- 個人用に設定済みの`Code.gs`
- Spreadsheet ID
- Apps Script Web App URL
- Shared secret

配布版`Code.gs`はプレースホルダーのままです。共有シークレットは十分に長いランダム値を使用し、一度公開した値は再利用しないでください。

Unity側のWeb App URLとShared secretはローカルの`EditorPrefs`へ保存されます。Google Sheets送信時には、選択した棚卸し情報に加えてローカルのプロジェクト名・プロジェクトパス・Unityバージョンも送信され、シートのメモへ記録されます。

## Limitations

Unityは通常、`.unitypackage`を「いつ・どの配布元からインポートしたか」という信頼できる履歴を保持しません。そのため`Assets`配下の分類は現存ファイルを基にしたヒューリスティックです。

- `VPM Package`: VPM manifestと登録済みPackageInfoで判定
- `UPM Package`: Unity Package Manager登録情報で判定
- `Built-in Package`: `PackageSource.BuiltIn`で判定
- `Editor Tool`: Editorスクリプト、UnityEditor参照、MenuItem等で推定
- `Project Content`: 一般的な制作フォルダ名から推定
- `Imported Asset / Unknown`: 購入Asset、配布ツール、自作フォルダが混在する可能性あり

Asset側の`package.json`は、誤検出を減らすため棚卸しグループ直下に存在するものだけをメタデータとして採用します。

## Support policy

- 個別の導入支援は行いません
- すべてのUnity環境やAssetへの対応は保証しません
- Issueへの返信や修正は保証しません
- 開発・更新は作者自身の必要性を優先します

## License

MIT License. See [LICENSE](LICENSE).

## Disclaimer

This project is an independent community tool and is not affiliated with, endorsed by, or sponsored by VRChat Inc.
