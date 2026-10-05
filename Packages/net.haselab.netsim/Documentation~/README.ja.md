# NetSim マニュアル

[English](README.md) | 日本語

NetSim は、1 回の Unity Editor の Play セッション（ClientSim）の中で複数の VRChat クライアントを動かし、それらの間の
Udon 通信を、遅延・ゆらぎ・欠落のある疑似サーバー経由でやり取りさせます。シナリオ（C# のコルーチン）がクライアントを
追加し、ボットでワールドを操作し、クライアント間の違いをレポートします。

これはデバッグの補助であり、VRChat のネットワークを忠実に再実装したものではありません。重要な結果は、実際の VRChat
クライアントを複数使ったテスト（複数クライアントでの「Build & Test」）で確かめてください。

## 仕組み

### クライアント

- ワールドのシーンを、リモートクライアント 1 つにつき 1 回追加ロードします。写しはそれぞれ独立した物理シーンに置かれる
  ので、クライアント同士のトリガーやコライダーは干渉しません。
- 元のシーンが ClientSim のローカルプレイヤー（`C0`）、写しが ClientSim のリモートプレイヤー（`C1`, `C2`, ...）です。
- Udon が実行されるたびに（`UdonBehaviour.RunProgram` を Harmony でパッチ）、NetSim は実行中のビヘイビアのシーンから
  クライアントを判定し、次の問い合わせにクライアントごとの値を返します。
  - `Networking.LocalPlayer`, `Networking.IsMaster`, `VRCPlayerApi.isLocal`
  - `Networking.GetOwner` / `IsOwner` / `SetOwner`
  - `VRC_Pickup.currentPlayer` / `IsHeld`

### 疑似ネットワーク

| 機能 | 挙動 |
|---|---|
| ネットワークイベント（`SendCustomNetworkEvent`） | All / Others / Owner / Self、`[NetworkCallable]` の引数。送信者と受信者の組ごとに順序を保つ。欠落率を設定可能 |
| 同期変数（Manual / Continuous） | フレームの終わりに送信。サーバーが把握しているオーナー以外からのデータは拒否される（書き込みの消失）。遅れて届いた古いデータは捨てる。オーナーは自分のデータを受け取らない。欠落率を設定可能 |
| 所有権 | `SetOwner` は手元ではすぐに反映され、サーバーが確定したあとで全員に通知される（`OnOwnershipTransferred`）。マスターが退出すると、そのオブジェクトは新しいマスターに移る |
| 途中参加 | 新しいクライアントは、全ビヘイビアについてサーバーが持つ最新の状態を受け取る。シーンの準備ができる前に届いたメッセージは保留される |
| オブジェクトの位置（`VRCObjectSync` の簡易版） | オーナーの位置・回転を一定間隔で送り、他のクライアントで補間する |

遅延は片道で、メッセージごとに `latencyMin` から `latencyMax` の間で一様に選ばれます。そのため種類の違うメッセージ同士は
追い越すことがあります。

### 実行中に NetSim が変える Editor の挙動

- **UdonSharp のシーン処理** は、シーンがロードされると C# のフィールド値を *アクティブな* シーンに書き込みます。
  NetSim の実行中は、アクティブなシーンへの書き込みを止め、代わりに各写しに適用します。
- **`UdonManager.OnSceneLoaded`** は、追加ロードのたびに全シーンの遅延イベントと Update の登録を消してしまいます。
  写しについては、NetSim は新しいシーンだけを初期化します。
- 写しの **`VRC_SceneDescriptor`** が元のシーンのものを置き換えないようにします。
- ClientSim のリモートプレイヤーの **`VRCPlayerApi.GetTrackingData`** は例外を投げるため、NetSim はプレイヤーの位置を返します。

### オブジェクトの持ち運び

`NetSim.StartCarry(client, go, target)` は、拾う → 歩く → 置く を再現します。

- 所有権は運ぶ人に移り、`OnPickup` は運ぶ人のクライアントだけで呼ばれます。`VRC_Pickup.IsHeld` / `currentPlayer` も
  運ぶ人を返します。`pickupable = false` のオブジェクトは拾われません。
- オブジェクトは NavMesh の経路に沿って、手の高さ（`NetSim.HandHeight`、1 m）と歩く速さ（2.5 m/s）で、キネマティックな
  `Rigidbody.MovePosition` によって移動します。そのため途中のトリガーは、出入りを検知できます。
- 到着時に `OnDrop` が呼ばれます。ワールドのコードが `Pickup.Drop()` を呼んだ場合はその時点で呼ばれます。
- 他のクライアントからは、オブジェクト同期のエミュレーションを通じて動いて見えます。

## シナリオの書き方

```csharp
using System.Collections;
using Haselab.NetSim;

static class MyWorldScenarios
{
    [NetSimScenario("doors")]
    static IEnumerator Doors(NetSimScenarioContext ctx)
    {
        yield return ctx.AddClients(2);                                  // C1, C2（1 つずつ追加）
        var door = ctx.Master.goByPath["World/Door"];                    // 階層パスでオブジェクトを取得
        NetSim.As(ctx.Master, () => door.GetComponent<VRC.Udon.UdonBehaviour>().SendCustomEvent("_interact"));
        yield return ctx.Settle(3);                                      // シミュレーション上の秒数
        ctx.R($"- door open on C2: {NetSim.Var(NetSim.Clients[2], "World/Door", "isOpen")}");
        ctx.ReportConsistency("after opening the door");                // 同期変数の差分（マスター対その他）
    }
}
```

シナリオを含むアセンブリは Editor 専用にし、`Haselab.NetSim.Editor` を参照してください
（`Samples~/ExampleScenarios/Haselab.NetSim.Samples.Editor.asmdef` を参照）。

よく使うメンバー:

| メンバー | 用途 |
|---|---|
| `ctx.AddClients(n, prefix, lateJoin)` | リモートクライアントを追加し、ビヘイビアが開始するまで待つ |
| `NetSim.AddClient(name, lateJoin)` / `NetSim.RemoveClient(c)` | 参加 / 退出（マスターを退出させるとマスター移譲のテストになる） |
| `NetSim.MasterId = c.player.playerId` | ゲーム開始前にリモートクライアントをインスタンスの作成者にし、あとで退出できるようにする |
| `ctx.PlayUntil(stop, timeout, bot)` | シミュレーション上の 1 秒ごとに、各クライアントについて `bot(client, rng)` を呼ぶ。進行が止まると打ち切る（`ctx.ProgressSignature`, `ctx.StallSeconds`） |
| `NetSim.StartCarry(c, go, target)` | クライアント `c` としてオブジェクトを運ぶ |
| `NetSim.As(c, action)` | クライアント `c` としてコードを実行する（Unity UI など Udon 以外のコードを呼ぶときに必要） |
| `NetSim.Var(c, path, name)` | あるクライアントのプログラム変数を読む |
| `NetSim.DiffSynced(a, b)` / `ctx.ReportConsistency(title)` | 同期変数の差分 |
| `NetSim.DiffVisual(a, b, root)` / `ctx.LateJoinOnlyDifferences(early, late, root)` | アクティブ / コライダー / レンダラー / テキスト / インタラクト可否の差分 |
| `NetSim.HaltedBehaviours()` | Udon の例外で停止したビヘイビア |
| `NetSim.TraceSubstrings` または `NetSimConfig.trace` | パスが一致するオブジェクトの通信をログに出す |
| `NetSimScenarioRunner.CountedLogPrefixes` | ログ行（ゲーム内のイベントなど）を数えてレポートに載せる |
| `NetSimScenarioRunner.WorldReady` | 「ワールドの初期化が終わった」の判定を差し替える |

同じ階層パスが複数あるときは `~n` が付きます（`Root/Item~1`, `Root/Item~2`）。

### ボット

ボットは、シミュレーション上の 1 秒ごとに各クライアントについて呼ばれる関数です。クライアントはそれぞれ独立に動くので、
ボタンの同時押しや同じオブジェクトの取り合いが自然に起きます。ボットはプレイヤーと同じように振る舞うように書いて
ください。アクティブでコライダーが有効なオブジェクトだけを操作し、見えていて押せるボタンだけを押し、拾えるオブジェクト
だけを運びます。サンプルの `MonkeyBot` は、ワールドについて何も知らずにこれを行います。ゲームの目的を知っているボットの
ほうが、はるかに多くの問題を見つけます。

## 実行

Play モードで、シナリオを 1 つ実行する:

```csharp
Haselab.NetSim.NetSimScenarioRunner.RunWhenReady("doors",
    new Haselab.NetSim.NetSimConfig { latencyMin = 0.1f, latencyMax = 0.4f, eventDropRate = 0.02f }, timeScale: 4);
```

複数のシナリオを無人で実行する（Play モードの再起動やドメインリロードをまたぎ、Editor が非アクティブでも動きます）:

```csharp
Haselab.NetSim.NetSimSuite.Start(new[] {
    // scenario|label|latMin|latMax|eventDrop|serializationDrop|objectSyncDrop|seed|timeScale[|trace[|teleport]]
    "doors|fast|0.03|0.08|0|0|0|1|4",
    "doors|slow-lossy|0.2|0.6|0.05|0.05|0.05|2|4",
});
```

- レポート: `Logs/NetSim/<scenario>_<label>_<time>.md`、スイートの進捗: `Logs/NetSim/suite.log`
- `swap:<dir>` ステップは、`<dir>` 以下のファイル（プロジェクトと同じ構成）をすべてプロジェクトにコピーして再コンパイル
  します。これを使うと、1 つのスイートの中で、ワールドのスクリプトの 2 つの版に対して同じシナリオを実行できます。

### レポートの読み方

- **synced vars ... N differences**: 通信が落ち着いたあとも、あるクライアントがマスターと違う同期値を持っています。
  たいていは本物のバグです（書き込みの消失、`RequestSerialization` の呼び忘れ、一部のクライアントでだけネットワーク
  イベントによって状態が変わった、など）。
- **late joiner only**: 途中参加者にだけある差分です。同期変数から状態が完全には復元されていません。純粋にローカルな
  UI の状態（案内板のページ、設定メニューなど）もここに出ますが、これは想定どおりです。
- **halted UdonBehaviours**: そのクライアントで Udon の例外によりビヘイビアが停止しました。最初のいくつかのエラーが
  レポートの先頭に載ります。
- **STALL**: 進行状況のシグネチャが `StallSeconds` の間変わりませんでした。ゲームが止まっている可能性があります。

### 同時操作で値がずれる書き方

よく見かける「所有権を取ってから書く」書き方（`Networking.SetOwner` のあとに同期変数を変えて `RequestSerialization`）は、
1 人ずつ操作する分には正しく動きます。しかし 2 人がほぼ同時に操作すると、両方が所有権を取って書き込み、片方の書き込み
だけが残ります。負けた側は、次に値が変わるまで自分の値を表示し続けます。NetSim はこの状態を
**synced vars ... differences** として検出します。オーナー以外のプレイヤーはオーナーにネットワークイベントで変更を依頼し、
オーナーだけが書き込むようにすると避けられます。サンプルワールドの `RacyCounter` と `GoodCounter` を比べてください。

## 限界

- VRChat の本物のネットワークではありません。帯域、まとめ送り、信頼性、所有権の調停は簡略化しています。
- オブジェクト同期は最小限です。持ち運びは NavMesh の直線区間を移動するだけで、手の動き、投げる動作、プレイヤーの移動
  手段（テレポーターなど）は再現しません。
- NavMesh エージェントは、各クライアントで独立にシミュレーションされます（VRChat と同様に同期されません）。
- ClientSim のリモートプレイヤーの一部の API（アバター、トラッキング）は使えません。
- 物理、アニメーション、オーディオは各写しでも動くので Editor の負荷になります。実用的なのは 3〜4 クライアントです。
