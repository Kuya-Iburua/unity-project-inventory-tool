# Unity / VRChat Project Inventory Tool v2

Unity 2022.3系のVRCSDK/VPMプロジェクト向け棚卸しEditorツールです。

## v2で追加したもの

- Unity側で、出力・Google Sheets送信対象をチェックして選択
- VPM／UPM／Built-in／Editor Tool／Imported Asset／Project Contentをカテゴリ単位で一括選択
- 行単位の個別選択
- 表示中の行だけを選択／解除／反転
- CSV／JSON／Google Sheetsは「チェック済みの項目だけ」を出力
- PackageInfoと各`package.json`からURLメタデータを追加取得
- Documentation／Repository／Homepage／Author／Changelog／Licenseを分離して記録
- Git URLを閲覧用HTTPS URLへ安全に正規化
- URLが見つからないAssetは、公式URLを推測せず検索リンクだけを作成
- Google Sheets側に、Preferred LinkとURL根拠・信頼度を表示
- `doGet()`を追加し、ウェブアプリURLをブラウザから疎通確認可能

## リンクの安全方針

このツールは検索結果の1位を自動で「公式URL」にしません。

直接リンクとして採用するのは、原則として以下だけです。

1. Unity Package Managerの`PackageInfo`に含まれるURL
2. インストール済みパッケージのルート`package.json`に含まれるURL
3. `Packages/manifest.json`に書かれたGit依存のリポジトリURL
4. `com.unity.*`に対するUnity公式ドキュメントの決定的なURL形式

`.unitypackage`由来と思われるAssetでURLメタデータがない場合は、Google／GitHub／BOOTHの「手動検索リンク」だけを付けます。検索先は検証済みURLとして扱いません。

## Unityへの導入・更新

1. Unityを閉じるか、コンパイルが止まっている状態にします。
2. 既存の`Assets/ProjectInventory`を削除します。
3. このZIP内の`Assets/ProjectInventory`を、対象プロジェクトの`Assets`へコピーします。
4. Unityのコンパイル完了後、`Tools > Project Inventory`を開きます。
5. `Refresh / Rescan`を押します。

旧版のGoogle Web App URLとShared secretはEditorPrefsに保存されているため、通常は再入力不要です。

## Unity側の選択操作

`Export / upload selection`を開くと、以下を操作できます。

- `Select all`：全項目を選択
- `Clear all`：全項目を解除
- `Select visible`：現在の検索・カテゴリ絞り込みで表示中の項目だけ選択
- `Clear visible`：表示中だけ解除
- `Invert visible`：表示中だけ選択状態を反転
- カテゴリチェック：VPM、UPM、Built-in等をまとめて選択・解除
- 一覧左端の`Use`：項目単位で選択・解除

初期状態は以下です。

- 選択：VPM、UPM、Editor Tool、Imported Asset / Unknown、Scan Warning
- 未選択：Built-in Package、Project Content

カテゴリ単位で変更した初期値はEditorPrefsに保存されます。

## Googleスプレッドシート連携

1. 一覧を書き込みたいGoogleスプレッドシートを作成します。
2. URLの`/d/`と`/edit`の間にあるIDをコピーします。
3. Google Apps Scriptで新規プロジェクトを作り、`GoogleAppsScript/Code.gs`を貼り付けます。
4. 以下を書き換えます。

```javascript
SPREADSHEET_ID: 'スプレッドシートID',
SHARED_SECRET: '十分に長いランダム文字列'
```

5. Apps Scriptをウェブアプリとしてデプロイします。
   - 実行ユーザー：自分
   - アクセスできるユーザー：全員
6. 発行された`/exec` URLをUnityの`Web App URL`に貼ります。
7. Apps Scriptと同じ秘密文字列を`Shared secret`に入力します。
8. Unity側で送信対象をチェックします。
9. `Upload ... selected item(s) to Google Sheets`を押します。

## Apps Scriptを更新した場合

コードを保存しただけでは公開版へ反映されない場合があります。

1. `デプロイ > デプロイを管理`
2. 鉛筆アイコン
3. バージョンを`新バージョン`
4. 再デプロイ

シークレットウィンドウで`/exec` URLを開き、以下が出れば匿名アクセスの入口は正常です。

```json
{"ok":true,"message":"Unity Project Inventory webhook is active."}
```

## Google Sheetsに追加されるリンク列

- Preferred Link：最も用途が明確な直接リンク
- URL Basis：Documentation metadata、Repository metadata等の根拠
- Link Confidence：リンク採用方針上の信頼度
- Documentation
- Repository
- Homepage
- Author Site
- Changelog
- License
- Search Links：Google／GitHub／BOOTHの手動検索

## 重要な制限

Unityは通常、`.unitypackage`を「いつ・どの配布元からインポートしたか」という信頼できる履歴を残しません。そのためAssets配下は現存ファイルを基にしたヒューリスティック分類です。

- `VPM Package`：VPM manifestと登録済みPackageInfoで判定
- `UPM Package`：Unity Package Manager登録情報で判定
- `Built-in Package`：PackageSource.BuiltInで判定
- `Editor Tool`：Editorスクリプト、UnityEditor参照、MenuItem等で判定
- `Project Content`：一般的な制作フォルダ名から推定
- `Imported Asset / Unknown`：購入Asset、配布ツール、自作フォルダが混在する可能性あり

Asset側の`package.json`は、誤検出を避けるため棚卸しグループの直下にあるものだけをメタデータとして採用します。

## セキュリティ

Apps ScriptのWeb App URLと共有シークレットを知っている人は、そのスプレッドシートへデータを送信できます。

- 共有シークレットは十分長いランダム値にする
- チャットやSNSへ貼った値は再利用しない
- Git管理対象のファイルへ秘密文字列を書かない
- Unity側の値はローカルのEditorPrefsへ保存される
