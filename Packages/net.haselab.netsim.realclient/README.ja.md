# NetSim Real Client Test

[English](README.md) | 日本語

[NetSim](../net.haselab.netsim/Documentation~/README.ja.md) の任意の追加パッケージです。デバッグ用ボットを使って、
**実機の VRChat クライアント**を複数使ったテストをします。NetSim（Editor の中でのシミュレーション）にはこのパッケージは
不要です。0.2.0 までは NetSim に含まれていました。

インストールは NetSim と同じです（VCC のリスティング、または git URL に `?path=Packages/net.haselab.netsim.realclient`）。
`Tools~` フォルダ（`analyze_logs.py`, `vrc_clients.py`）はこのパッケージの中にあります
（`Packages/net.haselab.netsim.realclient/Tools~`。git URL で入れた場合は `Library/PackageCache/` の下）。

NetSim は速いぶん簡略化されています。最後の確認として、実機の VRChat クライアントで同じようなテストを行えます。
VRChat SDK の「Build & Test」でこの PC 上に複数のクライアントを起動し、ワールドの中の **ボット** が各クライアントで遊んで、
行動と見えている状態を VRChat のログに書きます。そのあとスクリプトでクライアント同士のログを比べます。

| 部品 | 役割 |
|---|---|
| 自分のボット（Udon / UdonSharp のプレハブ） | 各クライアントでローカルプレイヤーとして動き、操作したあと、同期されたワールドの状態を `state` 行に書く |
| `RealClientTest.BuildAndTest(botPrefab, clients, config)` | 開いているシーンにボットを加え、N クライアント（Force Non-VR）で Build & Test し、終わったらシーンを元に戻す |
| `RealClientBuildGuard` | シーンにボットがある間は、それ以外のビルド（アップロード）を止める |
| `Tools~/analyze_logs.py` | クライアントのログを読み、各クライアントの最後の状態と、値が違うキーを一覧にする。`--must-match` で合否を出す |
| `Tools~/vrc_clients.py` | 起動中のクライアントの一覧、同じインスタンスへの追加起動（途中参加）、指定したクライアントの終了（マスター退出など） |

必要なもの: Windows、Steam の起動、VRChat のインストール（一度は起動しておく）、VRChat SDK へのログイン
（VRChat SDK > Show Control Panel）。実時間で進むので時間がかかります（サンプルのボットは 2 分間遊びます）。

## ボットの書き方

ボットは、プレハブに付けた普通の UdonSharp のビヘイビアです（`[UdonBehaviourSyncMode(None)]`）。各クライアントが
`Networking.LocalPlayer` として自分の分を動かします。次のように書きます。

- ワールドのオブジェクトは実行時に探す（`GameObject.Find`, `transform.Find`）。プレハブからシーンのオブジェクトは参照できません。
- プレイヤーと同じように振る舞う。インタラクト対象には `SendCustomEvent("_interact")`、ピックアップは所有権を取ってから
  毎フレーム手の高さで動かす。他のプレイヤーが動かしている物には手を出さない。
- 次の形式でログを書く（目印は既定で `[NSBOT]`）。

```
[NSBOT] t=<Time.time> p=<ローカルの playerId> ready ...
[NSBOT] t=... p=... act <何をしたか>
[NSBOT] t=... p=... carry start <何を>   /   carry end (<理由>)
[NSBOT] t=... p=... state key=value key=value ...   （このクライアントから見た同期状態。変化したときと 10 秒ごと）
[NSBOT] t=... p=... done                            （ボットが操作をやめた。state 行は書き続ける）
```

実機で得られた注意点:

- プレイヤーが操作できる物だけを操作する。`activeInHierarchy`、コライダーが有効、`DisableInteractive` でない、を確認します。
  確認しないと人には押せない物まで押してしまいます。ワールド側の連打防止（「2 秒以内のクリックは無視」など）にも掛かります。
- ワールドの初期化が終わるまで待つ（`startDelay`、またはワールドの準備完了の目印）。`Utilities.IsValid(Networking.LocalPlayer)`
  も確認します。早すぎる操作はビヘイビアを停止させることがあります。
- transform を動かす持ち運びでは `OnPickup` / `OnDrop` / `OnPickupUseDown` は起きず、`VRC_Pickup.IsHeld` も false のままです。
  これらに頼るワールドの処理は試せません。運ぶ間は Rigidbody を kinematic にし（終わったら元に戻す）、途中のトリガーは
  発火します。`pickupable = false` の物は運ばないでください。
- 所有権を失った、非表示になった、ワールドが位置を戻した（リセットなど）ときは運搬を終える。
- 一つしかない物をボット同士が奪い合うと、ゲームが止まることがあります。直近数秒以内に他人が動かした物には手を出さず、
  奪われたらしばらく諦めるようにします。
- UdonSharp からは UI の `Button` を押せません。ボタンが呼ぶメソッドを直接呼びます。その場合 UI 自体は試せていません。

`state` には、全クライアントで同じになるはずの値だけを入れてください（クライアントごとの回数などは入れない）。
サンプルワールドの `SampleWorldBot.cs` をコピーして始めると簡単です。公開フィールドは、実行ごとに
`config: "actionProbability=0.5;playSeconds=300"` のように上書きできます。

## 実行

```csharp
Haselab.NetSim.RealClient.RealClientTest.BuildAndTest(botPrefab, 3, "playSeconds=180");
```

またはボットのプレハブを選択して *Tools > NetSim > Real Client Test > Build & Test with Selected Bot Prefab* を実行します。
そのあと、パッケージの `Tools~` フォルダ（VCC で入れた場合は `Packages/net.haselab.netsim.realclient/Tools~`、git URL で入れた場合は
`Library/PackageCache/` の下）で次を実行します。

```
python analyze_logs.py --since "2026-10-06 10:28" --watch --must-match good,synced,goals --out report.md
python vrc_clients.py launch 1        # 任意: 途中参加者を追加
python vrc_clients.py kill 1          # 任意: マスター（プレイヤー 1）を退出させる
```

注意:

- SDK は、保存されている VRChat のパスからクライアントを起動します。パスが未設定のとき（新しい PC など）は、`BuildAndTest` が
  Steam のライブラリから探して設定します。VRChat が入っていなければ、メッセージを出して止まります。
- `python vrc_clients.py killall` は、自分で遊んでいるものも含めて VRChat をすべて終了します。テスト用のクライアントだけを
  閉じるには `kill <player>` を使ってください。
- 全クライアントが 1 台の PC で動くので、通信の遅延はほぼ 0 です。VRChat 本物の所有権・同期・タイミングは試せますが、
  遅延や欠落の試験にはなりません。それには NetSim を使ってください。
- 特定のクライアント（マスターなど）を退出させたいときは、1 クライアントでビルドし、残りを
  `python vrc_clients.py launch 2`（6 秒間隔）で追加します。各クライアントが別々のログファイルを持つので、プレイヤー ID で終了させられます。
- SDK はビルドの前に開いているシーンを保存します。`BuildAndTest` はファイルを退避して終わったあとに戻すので、
  **開いているシーンの未保存の変更は失われます**。先に保存してください。
- 同じ秒に起動したクライアントは同じログファイルに書きます。ツールはボットが書くプレイヤー ID ごとにまとめます。
- 追加のクライアントのログに `SteamApi_Init returned false` や `VRCNP: Failed to create server` が出ますが、1 台の PC で
  複数起動したことによるもので、ワールドの動作には関係ありません。
- シーンはバージョン管理しておいてください。復元は退避したファイルのコピーなので、ビルド中のクラッシュで壊れたファイルは
  git から戻すのが確実です。
- コンソールに古いエラーが残って UdonSharp のコンパイルが止まったときは、コンソールをクリアしてから再コンパイルします
  （`UdonSharpProgramAsset.CompileAllCsPrograms`）。
- ビルドが途中で止まってシーンにボットが残った場合は、*Tools > NetSim > Real Client Test > Remove Bots From Scene* で
  取り除いてください。それまではビルドガードがアップロードを止めます。

## サンプル: Sample World Bot

先に NetSim パッケージの **Sample World** をインポートし、次にこのパッケージの **Sample World Bot** をインポートします。

`SampleWorldBot.prefab` は、同じワールドを実機の VRChat クライアントで試すためのボットです。各クライアントのボットが 2 分間、ランダムにステーションを押してボールを運び、そのあと操作をやめて
`state good=.. racy=.. lost=.. synced=.. eventLamp=.. goals=..` を書き続けます。

1. Steam を起動し、VRChat SDK にログインして（VRChat SDK > Show Control Panel）、`SampleWorld.unity` を保存します。
2. *Tools > NetSim > Sample World > Build & Test with Bot (3 clients)* を実行します。
3. 任意: 1 分ほどしてから途中参加者を追加します。`python vrc_clients.py launch 1`（パッケージの `Tools~` で実行）
4. `python analyze_logs.py --since "<開始時刻>" --watch --must-match good,synced,goals`

`good`, `synced`, `goals` は全クライアントで一致する必要があります（PASS）。`racy` と `lost` はずれることがあり、
`eventLamp` は途中参加者だけ違います。これらは仕込んだバグで、本物の VRChat の通信の上でも同じことが起きるかを確かめられます。
