# 魔法・工業MODの実ファイル検証（2026-10-04）

## 結果

Minecraft 1.20.1 / Fabric Loader 0.19.5 / Java 17を対象に、正規配布の実JAR **12ファイル、計30,067,023 bytes**を取得・hash照合し、実行せずに依存宣言を確認しました。

初回の実API・実JAR検査で2件の誤検知が見つかり、修正しました。修正後は**取得済みの正規API応答と実ファイルをオフライン再生し、16ケースがすべて期待結果と一致**しました。追加取得がHTTP 403で止まったため、新しい通信への切り替えや迂回はしていません。

これは前提MOD・宣言条件の検査です。インストール、サーバー起動、ゲームプレイ、性能測定、Windows実画面の検証は行っていません。

| 候補 | 固定版 | 今回確認できた範囲 |
|---|---|---|
| Modern Industrialization | 1.8.6 / 18Bcl0A4 | Fabric API・Cloth Configを含む3ファイルで前提宣言が解決 |
| Tech Reborn | 5.8.15 / 5ordAPa5 | Fabric APIを含む2ファイルで前提宣言が解決。同一バイトの重複同梱を正しく扱う |
| Wizards | 3.1.3+1.20.1-fabric / sN5qsxEU | Spell Engine等を含む9ファイルで前提宣言が解決 |
| Spell Engine | 1.10.10+1.20.1-fabric / mvZvOSVL | 単独の固定6ファイル構成でも前提宣言が解決 |
| Paladins & Priests | 3.1.3+1.20.1-fabric / 1t4iuZkn | 内蔵Shieldを検出。API側が要求するShield単体が未取得なので未解決 |
| Spectrum | 1.8.13 / mDJAjTuc | APIサイズ87,723,541 bytesで現行64MiB上限を超え、取得前に停止 |

Wizardsを魔法、Modern IndustrializationまたはTech Rebornを工業の**検証済み候補**として比較できます。完成した導入テンプレートではありません。主役MODと必要なライブラリだけの構成であり、多数のコンテンツMODを混ぜた大型パックの検証でもありません。

## 実物から直した誤検知

1. Tech Rebornは、同じ`team_reborn_energy` JARを直接とRebornCore内部の両方に含みます。SHA-256・版が一致する同一内容の同梱だけを重複として許容し、全出現箇所の読取・階層・上限カウントは保持しました。別バイト・別版の候補選択やroot MODの重複は引き続き停止します。
2. Armor Model APIはclient専用の`fabric-rendering-v1`への依存を宣言しています。Fabric Loader 0.19.5は、schema 1で実在する環境無効候補の版が条件に一致し、active候補がない場合、この依存を緩和します。その公式規則に限定して対応しました。client親の下に隠れた子や別名からは推測しません。[公式Resolver](https://github.com/FabricMC/fabric-loader/blob/0.19.5/src/main/java/net/fabricmc/loader/impl/discovery/ModResolver.java#L57-L76)、[公式Discovery](https://github.com/FabricMC/fabric-loader/blob/0.19.5/src/main/java/net/fabricmc/loader/impl/discovery/ModDiscoverer.java#L173-L194)

APIの必須依存を任意へ格下げする変更はありません。Spell EngineのTrinketsはJARではrecommendsですがAPIではrequiredなので、Trinketsを除いた構成はAPI未解決として停止します。

## 16ケースで確認したこと

- MI・Tech Reborn・Wizardsの前提不足と、固定依存一式を指定した場合の解決
- MIの導入済み比較：隔離フォルダーの実Fabric APIをhashで識別・再利用
- Minecraft／Javaの版違い、Tech Rebornが要求するLoader版の不足
- Wizardsの推移的なSpell Power不足、API必須Trinkets不足、任意Runesを自動取得しないこと
- Spell Engineの独立した依存一式、Paladinsの「JAR同梱はあるがAPI必須projectは未解決」という区別
- Spectrumが現行容量制限で、ファイル取得前に停止すること

失敗ケースは単なる通信失敗で合格しないよう、検出コード・対象依存ID・実際の固定版集合・導入済み件数まで照合しました。オフライン再生は保存済み応答がなければ失敗し、ネットワークへフォールバックしません。

## 検証と残る範囲

- .NET 8単体・異常系：**308件成功、失敗0件、スキップ0件**（前回277件から31件追加）
- 独立した追加異常系14ケースも成功。WPFアプリと既存Windows試験プロジェクトのReleaseクロスビルドはエラー0件
- 既存のOpen.NAT互換性とnullable警告は残っています
- Runesの追加API取得はHTTP 403で未完了。Shield単体とCreate／Ars Nouveau／Mekanismの実ファイル試験は今回**未実施**です
- Spectrumの上限超過は検査器の容量制約であり、そのMODが危険・不適切だという判断ではありません。上限変更には別途メモリー／ストリーミング設計と試験が必要です
- 主役MODの配布情報はclient/server両方を要求しています。利用者のクライアント側構成や実接続は未検証です
- `.mrpack`、任意server-pack overrides、大型パックの自動導入は未対応です
- 本体のテンプレート適用・一括導入は無効のままです。Windowsの安全なファイル操作、停止排他、永続復旧、実画面の試験は[次段階](transaction-core-milestone.md)です
- ライセンスやAPI利用条件への適合、公開用カタログ、推奨スペックの実測を承認する検証ではありません

正確な取得・再生日時、project/version ID、サイズ、SHA-1/SHA-256/SHA-512、client/server宣言、全ケースの期待値と結果は[機械可読レポート](magic-technology-verification-2026-10-04.json)にあります。MODバイナリは再配布していません。

## 再現用入力

`McServerManager.LiveChecks/magic-technology-cases.json`には、ローカル導入済みファイルに依存しない15ケースの固定入力を収録しています。既存の`--live`実行は実際の配布元へ通信するため、取得可能性を確認してから明示的に実行してください。これは今回のオフライン再生と別の再実行です。

    dotnet run --project McServerManager.LiveChecks -c Release -- --live McServerManager.LiveChecks/magic-technology-cases.json report.json

[前回の17ケース実API検証](live-verification-2026-10-03.md)は別の取得時点の記録です。今回、前回分のライブ通信は再実行していません。
