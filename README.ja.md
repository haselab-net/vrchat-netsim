# vrchat-netsim

[English](README.md) | 日本語

**NetSim** は、VRChat ワールドの通信を Unity Editor の中でテストするツールです。1 回の Play セッション（ClientSim）の中で、
ワールドのシーンの写しを複数のクライアントとして同時に動かし、それらの間の Udon 通信を、遅延・ゆらぎ・欠落を設定できる
疑似サーバー経由でやり取りさせます。ワールドをアップロードしたり、Editor や VRChat クライアントを複数起動したりしなくても、
同期の不具合を再現・デバッグできます。

**NetSim ウィンドウ**（コード不要）、C# のシナリオ、コマンドライン（スクリプト・CI・AI エージェント）から使えます。

> 状態: 早期プレビュー（0.3.0）。API は変わる可能性があります。

## 機能

- **NetSim ウィンドウ**（*Tools > NetSim > NetSim Window*）: ネットワークのプリセット、Play 中のクライアントの追加・退出、
  Game ビューに映すクライアントの切り替え、任意のクライアントとしてのインタラクトや所有権の取得、クライアント間の比較、
  シナリオとバッチの実行、レポートの閲覧
- どのワールドでも動く組み込みのシナリオ: `join-check`, `random-play`, `late-join`, `master-leave`（PASS / FAIL）
- 各クライアントは、ワールドのシーンを追加ロードした写し（それぞれ独立した物理シーン）
- `SendCustomNetworkEvent`（All / Others / Owner / Self、`[NetworkCallable]` の引数付きを含む）
- 同期変数（Manual / Continuous、`RequestSerialization`）。オーナー以外の書き込みが失われる挙動も再現
- 所有権（`SetOwner` / `GetOwner` / `IsOwner` / `OnOwnershipTransferred`）とマスターの移譲
- 途中参加とマスター退出
- `VRCObjectSync` の簡易エミュレーションと、テストボットによる持ち運び（拾う → 歩く → 置く）
- メッセージの種類ごとの遅延 / ゆらぎ（順序の入れ替わり）/ 欠落率。ウィンドウ・コード・コマンドラインで共通の設定
  （`ProjectSettings/NetSimSettings.json`）
- Play モードの出入りやドメインリロードをまたいで無人で回るバッチ、終了コードを返すコマンドライン

## クイックスタート

1. パッケージをインストールし（下記）、自分のワールドのシーンを開きます。
2. *Tools > NetSim > NetSim Window* を開きます。
3. **Manual test**: *Enter Play Mode* → *Start NetSim* → *+ Client*（2 回）。Game ビューで C0 として遊び、Hierarchy で
   オブジェクトを選んで C1 と C2 の *Interact* を押し、*Compare now* を押します。
4. **Scenarios**: `random-play` を選んで *Run now*。結果は **Reports**（`Logs/NetSim/`）で読めます。

NetSim が本物のバグを見つける様子を見るには、Package Manager のパッケージページから **Sample World** サンプルを
インポートし、`SampleWorld.unity` を開いて `sample-world` シナリオを実行します。このワールドには正しい同期の書き方と
わざと仕込んだバグが入っていて、レポートのチェックリストで、NetSim が仕込んだバグだけを見つけたことを確認できます。

組み込みのシナリオはランダムにボタンを押すだけです。人と同じようにゲームを遊ぶシナリオのほうが、はるかに多くの問題を
見つけます。**Scenario Template** サンプルから始め、[マニュアル](Packages/net.haselab.netsim/Documentation~/README.ja.md)を
参照してください。

## スクリプト・CI・AI エージェントから

```
Unity.exe -batchmode -projectPath <プロジェクト> -executeMethod Haselab.NetSim.NetSimCli.Run \
          -netsimScene Assets/MyWorld.unity -netsimRun random-play -netsimRun late-join
```

終了コードは 0 = 全シナリオ合格、2 = チェックの不合格あり、1 = エラー。レポートは `Logs/NetSim/`、シナリオごとの結果は
`Logs/NetSim/cli-result.txt` に 1 行ずつ書かれます。ウィンドウの *Copy command line for this batch* でこのコマンドを
作れます。開いている Editor では、`Haselab.NetSim.NetSimScenarioRunner.RunInPlayMode("random-play")` で 1 つのシナリオを
同じように実行できます。[コマンドライン](Packages/net.haselab.netsim/Documentation~/README.ja.md#コマンドライン)を参照。

## 必要環境

- Unity 2022.3
- VRChat SDK3 Worlds 3.10.x（ClientSim と UdonSharp を同梱）
- Harmony（VRChat SDK 同梱の `0Harmony`）

NetSim は Editor 専用のアセンブリに入っており、ワールドのビルドには含まれません。

## インストール

### VRChat Creator Companion（VPM）

1. リスティングを追加します。[このページ](https://haselab-net.github.io/vrchat-netsim/)を開いて **Add to VRChat Creator Companion**
   を押すか、VCC の `Settings > Packages > Add Repository` に次の URL を入力します。

   ```
   https://haselab-net.github.io/vrchat-netsim/index.json
   ```

2. ワールドプロジェクトの **Manage Project** で **NetSim - Multi-client Sync Test for VRChat Worlds** を追加します。

### Unity Package Manager（git URL）

`Window > Package Manager > + > Add package from git URL...`

```
https://github.com/haselab-net/vrchat-netsim.git?path=Packages/net.haselab.netsim
```

URL の末尾にリリースのタグ（`#v0.3.0` など）を付けると、バージョンを固定できます。

## 任意: 実機の VRChat でのテスト

NetSim は速いぶん簡略化されています。別パッケージの **NetSim Real Client Test**（`net.haselab.netsim.realclient`）は、
複数の実機 VRChat クライアント（SDK の *Build & Test*）でデバッグ用ボットを動かし、ログを比べます。
[README](Packages/net.haselab.netsim.realclient/README.ja.md) を参照してください。上記の機能には不要です
（0.2.0 までは NetSim パッケージに含まれていました）。

## リポジトリ構成

```
Packages/net.haselab.netsim/            NetSim: Editor の中での複数クライアントの同期テスト
  Editor/                               シミュレーター、シナリオ、ウィンドウ、コマンドライン（Editor 専用 asmdef）
  Samples~/SampleWorld/                 同期のバグを仕込んだワールドとそのシナリオ
  Samples~/ScenarioTemplate/            自分のシナリオのひな形
  Documentation~/                       マニュアル
Packages/net.haselab.netsim.realclient/ 任意: 実機の VRChat でのテスト（ボット + Build & Test、Tools~/*.py）
.github/workflows/release.yml           タグ v<version> -> GitHub リリース（2 つの zip）と GitHub Pages の VPM リスティング
tools/build_listing.py                  リリースから VPM リスティングを作る
```

## 開発について

このプロジェクトは [Claude Code](https://claude.com/claude-code) を使って開発しました。コードとドキュメントの多くは、
人間の指示とレビューのもとで Claude（Anthropic）が書いています。

## ライセンス

[MIT](LICENSE)
