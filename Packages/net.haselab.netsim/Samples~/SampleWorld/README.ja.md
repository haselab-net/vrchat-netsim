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

1. `SampleWorld.unity` を開きます。
2. *Tools > NetSim > NetSim Window* > **Scenarios** > `sample-world` > **Run now**
   （コードからは `Haselab.NetSim.NetSimScenarioRunner.RunInPlayMode("sample-world");`）。
3. レポート（**Reports** タブ、`Logs/NetSim/sample-world_*.md`）の最後に `## Verdict` のチェックリストがあり、上の表の期待が
   すべて満たされると **PASS** になります。

組み込みのシナリオ（`random-play`, `late-join` など）もこのワールドで動き、仕込んだバグを FAIL として報告します。

手でバグを見るには: **Manual test** > *Start NetSim* > *+ Client* を 2 回、Hierarchy で `RacyCounter` を選んで C1 と C2 の
*Interact* を続けて押すか、`LostWriteCounter` を C1 として *Interact* し、*Compare now* を押します。

このシナリオは、メッセージの欠落がない状態（既定の設定）を前提にしています。`eventDropRate > 0` にすると、
イベントだけのトグルは初めからいるクライアント同士でもずれ、依頼が失われた分だけ GoodCounter の数が少なくなります。
これらは欠落があるときのそれぞれの書き方の、想定どおりの挙動です。

同じワールドを実機の VRChat クライアントで試すには、任意のパッケージ NetSim Real Client Test の **Sample World Bot**
サンプルを参照してください。
