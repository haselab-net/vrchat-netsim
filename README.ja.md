# vrchat-netsim

[English](README.md) | 日本語

**NetSim** は、VRChat ワールド開発向けの Editor 専用マルチクライアント・ネットワークシミュレーターです。
Unity Editor（ClientSim）の中で複数のクライアントを同時に動かし、それらの間の Udon 通信を、遅延・ゆらぎ・欠落を
設定できる疑似サーバー経由でやり取りさせます。ワールドをアップロードしなくても、同期の不具合を再現・デバッグできます。

> 状態: 早期プレビュー（0.1.0）。API は変わる可能性があります。

## 機能

- 各クライアントは、ワールドのシーンを追加ロードした写し（それぞれ独立した物理シーン）として動作
- `SendCustomNetworkEvent`（All / Others / Owner / Self、`[NetworkCallable]` の引数付きを含む）
- 同期変数（Manual / Continuous、`RequestSerialization`）。オーナー以外の書き込みが失われる挙動も再現
- 所有権（`SetOwner` / `GetOwner` / `IsOwner` / `OnOwnershipTransferred`）とマスターの移譲
- 途中参加とマスター退出
- `VRCObjectSync` の簡易エミュレーションと、テストボットによる持ち運び（拾う → 歩く → 置く）
- 遅延 / ゆらぎ（順序の入れ替わり）/ 欠落率の設定
- Play モードの出入りやドメインリロードをまたいで無人で一括実行できるスイート

## サンプルワールドで試す

1. パッケージをインストールし（下記）、Package Manager のパッケージページから **Sample World** をインポートします。
2. インポートしたサンプルの `SampleWorld.unity` を開き、Play モード（ClientSim）に入ります。
3. `Haselab.NetSim.NetSimScenarioRunner.RunWhenReady("sample-world");` を実行します。
4. `Logs/NetSim/` のレポートを読みます。このワールドには正しい同期の書き方と、わざと仕込んだバグ（所有権を取らない
   書き込み、「所有権を取ってから書く」の競合、ネットワークイベントの中にしかない状態）が入っています。レポートの最後の
   チェックリストで、NetSim が仕込んだバグだけを見つけたことを確認できます。詳しくは
   [サンプルの README](Packages/net.haselab.netsim/Samples~/SampleWorld/README.ja.md) を参照してください。

## クイックスタート（自分のワールド）

1. パッケージをインストールし（下記）、Package Manager のパッケージページから **Example Scenarios** をインポートします。
2. 自分のワールドのシーンを開き、Play モード（ClientSim）に入ります。
3. 小さな Editor スクリプトや「C# を実行する」ツールなどからシナリオを実行します。

```csharp
Haselab.NetSim.NetSimScenarioRunner.RunWhenReady("example-consistency",
    new Haselab.NetSim.NetSimConfig { latencyMin = 0.1f, latencyMax = 0.3f, eventDropRate = 0.02f });
```

4. `Logs/NetSim/` のレポートを読みます。

例のシナリオは、ワールドの内容を知らないランダムなボットを使います。自分のワールドの遊び方に合わせたシナリオと
ボットを書いてください。書き方は[マニュアル](Packages/net.haselab.netsim/Documentation~/README.ja.md)にあります。

## 必要環境

- Unity 2022.3
- VRChat SDK3 Worlds 3.10.x（ClientSim と UdonSharp を同梱）
- Harmony（VRChat SDK 同梱の `0Harmony`）

NetSim は Editor 専用のアセンブリに入っており、ワールドのビルドには含まれません。

## インストール

### Unity Package Manager（git URL）

`Window > Package Manager > + > Add package from git URL...`

```
https://github.com/haselab-net/vrchat-netsim.git?path=Packages/net.haselab.netsim
```

### VRChat Creator Companion

VPM リスティングは最初のリリース後に用意する予定です。

## リポジトリ構成

```
Packages/net.haselab.netsim/
  package.json
  Editor/                     NetSim 本体とスイート実行（Editor 専用 asmdef）
  Samples~/SampleWorld/       バグを仕込んだサンプルワールドとそのシナリオ（Package Manager からインポート）
  Samples~/ExampleScenarios/  ワールドに依存しない例のシナリオ / ランダムなテストボット（Package Manager からインポート）
  Documentation~/             マニュアル: 仕組み、シナリオの書き方、限界
  CHANGELOG.md
```

## 開発について

このプロジェクトは [Claude Code](https://claude.com/claude-code) を使って開発しました。コードとドキュメントの多くは、
人間の指示とレビューのもとで Claude（Anthropic）が書いています。

## ライセンス

[MIT](LICENSE)
