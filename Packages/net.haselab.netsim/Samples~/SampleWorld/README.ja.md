# サンプルワールド

[English](README.md) | 日本語

正しい同期の書き方と、わざと仕込んだ同期のバグが入った小さなワールドです。NetSim がバグだけを見つけることを確認する
シナリオが付いています。

| オブジェクト | 書き方 | NetSim の期待される結果 |
|---|---|---|
| `GoodCounter` | オーナー以外はネットワークイベントでオーナーに依頼し、オーナーだけが同期値を書く | 一致 |
| `RacyCounter` | 「所有権を取ってから書く」（2 人がほぼ同時に押すとバグになる） | 競合のあとでクライアント間の値がずれる |
| `LostWriteCounter` | 所有権を取らずに同期値を書く（バグ） | 同期変数の差分 |
| `SyncedToggle` | 状態を同期変数に持ち、オーナーだけが変える | 一致（途中参加者も） |
| `EventToggle` | 状態をネットワークイベントだけで変える（バグ） | 途中参加者だけランプの状態が違う |
| `Ball` + `Goal` | `VRCObjectSync` 付きのピックアップをトリガーまで運ぶ。ゴールのオーナーが数える | 全クライアントで同じゴール数 |

## 実行

1. `SampleWorld.unity` を開き、Play モード（ClientSim）に入ります。
2. シナリオを実行します（小さな Editor スクリプトや「C# を実行する」ツールなどから）。

```csharp
Haselab.NetSim.NetSimScenarioRunner.RunWhenReady("sample-world");
```

3. `Logs/NetSim/sample-world_*.md` を読みます。最後に `## Verdict` のチェックリストがあり、上の表の期待がすべて満たされると
   **PASS** になります。

**Example Scenarios** サンプルの例のシナリオ（`example-consistency`, `example-latejoin`, `example-masterleave`）も
このワールドで動きます。

このシナリオは、メッセージの欠落がない状態（既定の設定）を前提にしています。`eventDropRate > 0` にすると、
イベントだけのトグルは初めからいるクライアント同士でもずれ、依頼が失われた分だけ GoodCounter の数が少なくなります。
これらは欠落があるときのそれぞれの書き方の、想定どおりの挙動です。

## 実機の VRChat でのテスト

`SampleWorldBot.prefab` は、同じワールドを実機の VRChat クライアントで試すためのボットです（マニュアルの「実機の VRChat での
テスト」を参照）。各クライアントのボットが 2 分間、ランダムにステーションを押してボールを運び、そのあと操作をやめて
`state good=.. racy=.. lost=.. synced=.. eventLamp=.. goals=..` を書き続けます。

1. Steam を起動し、VRChat SDK にログインして（VRChat SDK > Show Control Panel）、`SampleWorld.unity` を保存します。
2. *Tools > NetSim > Sample World > Build & Test with Bot (3 clients)* を実行します。
3. 任意: 1 分ほどしてから途中参加者を追加します。`python vrc_clients.py launch 1`（パッケージの `Tools~` で実行）
4. `python analyze_logs.py --since "<開始時刻>" --watch --must-match good,synced,goals`

`good`, `synced`, `goals` は全クライアントで一致する必要があります（PASS）。`racy` と `lost` はずれることがあり、
`eventLamp` は途中参加者だけ違います。これらは仕込んだバグで、本物の VRChat の通信の上でも同じことが起きるかを確かめられます。
