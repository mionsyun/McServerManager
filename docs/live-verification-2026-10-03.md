# 実ファイルによるMOD依存検証（2026-10-03）

## 結果

最終ソースの単体試験211件が成功し、WPFアプリと既存Windowsテストプロジェクトもクロスビルドでエラー0件でした。Windowsの実画面・実行試験は別途必要です。

実際のModrinth APIと正規配布JARを使用した17ケースが、判定理由・対象依存ID・取得した固定版の集合まで期待結果と一致しました。
これは宣言・取得元・hash・版条件の検証です。MODの実行、サーバー起動、インストール、Windows実画面の試験は行っていません。

## 確認できた例

- Chunkyの前提不足を検出し、Fabric APIの固定版を指定すると実JARの「fabric」別名と同梱49モジュールを照合
- 同じFabric APIの実ファイルを導入済みとして与え、SHA-512による取得元照会と再利用を確認
- Waystones → Balm → Fabric APIの実際の依存連鎖を重複なく確認
- No Chat Reportsの必須依存と任意依存を区別し、任意依存は自動取得しないことを確認
- LedgerとCardboardの本物の競合をAPIとfabric.mod.jsonの両方から検出
- Minecraft、Fabric Loader、Javaの版違いと、NeoForgeファイルをFabricとして使う誤りを停止
- CardboardのJARだけが要求するicommonを、名前からprojectへ推測せず未解決として停止
- iCommonの複数Minecraft版向け同梱候補は、未対応の候補選択として停止

## テストした固定ファイル

| MOD | Project ID | Version ID | Bytes |
|---|---|---|---:|
| iCommon API | SVKv1SZo | 7UUgrDDA | 404167 |
| No Chat Reports | qQyHxfxd | D8K0KJXM | 309410 |
| Balm | MBAkmtvl | EghJxg4h | 805265 |
| Fabric API | P7dR8mSH | Mys3P7lK | 2452735 |
| Lithium | gvQqBUqZ | N08Z8wog | 797454 |
| Chunky | fALzjamp | RVFHfo1D | 342979 |
| Fabric Language Kotlin | Ha28R6CL | eRRZzGMc | 8142858 |
| Waystones | LOpKHB2A | nSAwjAk4 | 1248666 |
| Cardboard | MLYQ9VGP | q1Hxw5tl | 4901961 |
| FerriteCore | uXXizFIs | sOzRw3CG | 123450 |
| Ledger | LVN9ygNV | t1AtqfxZ | 20475069 |
| FerriteCore | uXXizFIs | x7kQWVju | 121559 |

すべての正確なSHA-256/SHA-512/SHA-1、API URL、検証日時、ケース別結果は [機械可読レポート](live-verification-2026-10-03.json) にあります。
独立した期待結果は、別のAPI取得・標準ライブラリでのアーカイブ読み取りと公式仕様から作成しました。失敗ケースは、単なる通信失敗でも合格になることを避けるため、想定した検出コードと依存IDを要求しています。

## 残る範囲

- 導入済み比較は、隔離した検証用フォルダの実ファイルを使ったものです。利用者のPCや既存サーバー環境の検証ではありません
- 全MODの互換性や安全性を保証するものではありません。未宣言依存、MODの不具合、起動時の問題は残ります
- テンプレートからの自動作成・MOD一括適用は無効です。固定ランタイム取得、トランザクション、取消・復旧、停止状態での完了などは別段階です
- CurseForgeと他loaderの解決、複数同梱候補の完全な選択は未対応です
- 本物のMODバイナリは再配布していません。公開するのはソース、試験方法、固定IDと検証事実のみです
