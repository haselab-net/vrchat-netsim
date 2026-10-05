# 例のシナリオ

[English](README.md) | 日本語

ワールドに依存しないシナリオです。各クライアントで、ランダムな「モンキー」ボットが見えている UI ボタンを押し、
有効なインタラクト対象を操作します。

| シナリオ | 確認すること |
|---|---|
| `example-consistency` | 3 クライアントが（シミュレーション上の）120 秒遊ぶ。最後に全クライアントの同期変数が一致する |
| `example-latejoin` | 60 秒後に 1 クライアントが参加する。他のクライアントと一致する（同期変数と見た目の状態） |
| `example-masterleave` | ゲームの途中でマスターが退出する。残ったクライアントは一致したままで、停止するものがない |

Play モードで実行:

```csharp
Haselab.NetSim.NetSimScenarioRunner.RunWhenReady("example-consistency");
```

本当の問題を見つけるには、`ExampleScenarios.cs` をコピーし、`MonkeyBot` を自分のゲームを遊ぶボットに置き換えてください。
