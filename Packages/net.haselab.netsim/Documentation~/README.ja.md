# NetSim マニュアル

[English](README.md) | 日本語

NetSim は、1 回の Unity Editor の Play セッション（ClientSim）の中で複数の VRChat クライアントを動かし、それらの間の
Udon 通信を、遅延・ゆらぎ・欠落のある疑似サーバー経由でやり取りさせます。NetSim ウィンドウから手でクライアントを
操作することも、シナリオ（組み込みのもの、または自分で書く C# のコルーチン）にクライアントの追加、ボットによる操作、
クライアント間の違いのレポートをさせることもできます。シナリオはウィンドウ、コード、コマンドラインから実行できます。

これはデバッグの補助であり、VRChat のネットワークを忠実に再実装したものではありません。重要な結果は、実際の VRChat
クライアントを複数使ったテスト（複数クライアントでの「Build & Test」。任意のパッケージ
[NetSim Real Client Test](../../net.haselab.netsim.realclient/README.ja.md) がボットで自動化します）で確かめてください。

## クイックスタート

以下の内容をサンプルワールドで順に試す[スクリーンショット付きのチュートリアル](https://github.com/haselab-net/vrchat-netsim/blob/main/docs/tutorial/README.ja.md)があります。

1. パッケージをインストールし（リポジトリの README を参照）、自分のワールドのシーンを開きます。
2. **Tools > NetSim > NetSim Window** を開きます。
3. ネットワークの条件を選びます（*Typical* や *Bad Wi-Fi* などのプリセット、または自分で値を入れる）。
4. 手で試す（**Manual test** タブ）か、シナリオに遊ばせます（**Scenarios** タブ）。結果は **Reports** タブに並び、
   `Logs/NetSim/` に書き出されます。

どちらもコードは要りません。同期のバグがわかっているワールドで試すには、**Sample World** サンプルをインポートして
`SampleWorld.unity` を開き、`sample-world` シナリオを実行します。

## NetSim ウィンドウ

### ネットワークの条件

片道の遅延（区間ごと）、欠落率、シードなど `NetSimConfig` の項目です（[後述](#ネットワークの条件遅延ゆらぎ欠落)）。
`ProjectSettings/NetSimSettings.json` に保存され、手動のセッションと、設定を指定せずに始めたすべてのシナリオ
（コードやコマンドラインから始めたものも）で使われます。チームでコミットして共有できます。手動のセッション中に変えると、
すぐに反映されます（シードを除く）。

### 手動のテスト（Manual test）

1. Play モードに入ります（ボタンでも入れます）。自分は ClientSim のプレイヤー **C0** で、いつもどおり Game ビューで遊べます。
2. **Start NetSim** を押し、ほかのプレイヤーの数だけ **+ Client** を押します。各クライアントはシーンの写しで、
   Hierarchy では `C1 Remote1`, `C2 Remote2`, … という別々のシーンとして表示されます（`★` はマスター）。
3. **View** で、Game ビューにどのクライアントの写しを映すかを選びます（*View all* は全部の写しを重ねて描きます）。
   Game ビューでのマウスやキーボードの操作は、常に C0 として働きます。
4. Hierarchy でオブジェクトを選ぶと（どの写しのものでもよい）、**Selected object** に各クライアントでのオーナーと
   同期変数が並び、マスターと違う値は赤で表示されます。各クライアントとして **Interact**（インタラクト）、
   **Own**（所有権を取る）、任意のイベントの実行ができます。2 つのクライアントの *Interact* を続けて押すのが、
   「2 人が同時に押した」を再現するいちばん手軽な方法です。
5. **Interact as another client** には、あるクライアントがいまインタラクトできる物が並びます。
6. **Compare now** は、マスターとほかのクライアントで値が違う同期変数を一覧にします（*every 2 s* で自動更新）。
   **Write snapshot report** は、差分、見た目の差分、通信の統計、NetSim のログを `Logs/NetSim/snapshot_*.md` に保存します。
7. **Leave** でクライアントを退出させます。マスターを退出させるとマスター移譲のテストになります。
   **+ Late joiner** は途中参加者を加えます。
8. **Stop NetSim** で写しとそのプレイヤーが消え、C0 だけに戻ります。

ClientSim の案内で **Accept** を押したあとは、Game ビューがマウスをつかんだままになります。**Esc** を押してマウスを
放してからウィンドウを操作してください。

### シナリオ（Scenarios）

シナリオを選び、**Run now** を押します。毎回新しい Play セッションで実行し（Play モードなら一度止めて入り直すので、
前に試した状態が結果に影響しません）、終わったあとも Play モードのままです。シナリオの写しは、次に NetSim を始めるか
*Remove the copies now* を押すまで残り、Hierarchy で `(stopped)` と表示されます。**Add to batch** で実行を集め（*+ this scenario × every preset* で全プリセット分）、
**Run batch** で無人で順に実行します（各回は新しい Play セッション）。**Copy command line for this batch** で、
同じことをするコマンドラインをコピーできます（[コマンドライン](#コマンドライン)）。

組み込みのシナリオ（コード不要。どのワールドでも動きます）:

| シナリオ | 内容 | PASS の条件 |
|---|---|---|
| `join-check` | クライアントが参加するだけ（誰も操作しない） | 全クライアントの同期変数が一致 |
| `random-play` | 各クライアントが *Play time* の間、ランダムにボタンやインタラクト対象を押す | 終わったあと全員が一致 |
| `late-join` | 遊んだあとで 1 人が途中参加 | 全員が一致し、途中参加者にも同じ物が見えている |
| `master-leave` | 遊んでいる途中でマスターが退出 | 残った全員が一致し、動き続ける |

*Clients*（C0 を含む合計）、*Play time*、*Bot activity* は組み込みのシナリオに効きます。組み込みのシナリオは
プレイヤーが押せる物だけを押しますが、ゲームの中身は知りません。自分のワールド用に書いたシナリオ
（[シナリオの書き方](#シナリオの書き方)）のほうが、はるかに多くの状態を試せます。

### レポート（Reports）

各シナリオは `Logs/NetSim/<シナリオ>[_<ラベル>]_<時刻>.md` を書き、最後の `## Verdict` に **PASS** / **FAIL** が
付きます（[レポートの読み方](#レポートの読み方)）。**Reports** タブに新しい順に並び、Editor の中で読めます。

## 仕組み

### Unity Editor は 1 つだけ

3〜4 クライアントで試す場合でも、起動する Unity Editor は **1 つ** です。Editor を複数起動したり、プロジェクトを複製
したり、VRChat クライアントを起動したりする必要はありません。すべてのクライアントが同じ Play セッションの中で動きます。

- 全クライアントの状態を直接読んで比べられるので、シナリオが違いを自動で見つけてレポートに書き出せます。
- 実時間より速く進められます（`timeScale`。サンプルワールドの約 90 秒のシナリオは実時間 20 秒ほどで終わります）。
- `seed` を固定すると、遅延・欠落・ボットの行動に同じ乱数が使われるので、問題をずっと再現しやすくなります
  （フレームのタイミングは実行ごとに少し変わります）。

その代わり、全クライアント分の物理・アニメーション・オーディオがその Editor で動くので負荷が増えます（「限界」を参照）。
また通信は簡略化したモデルなので、重要な結果は実際の VRChat クライアントを複数使って確かめてください（「Build & Test」）。

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

### ネットワークの条件（遅延・ゆらぎ・欠落）

疑似ネットワークは、NetSim ウィンドウ（`ProjectSettings/NetSimSettings.json` に保存）か、シナリオを開始するときに渡す
`NetSimConfig` で設定します。

```csharp
Haselab.NetSim.NetSimScenarioRunner.RunWhenReady("sample-world", new Haselab.NetSim.NetSimConfig {
    latencyMin = 0.1f, latencyMax = 0.4f,      // 1 区間あたりの片道の遅延（秒）。幅がゆらぎになる
    eventDropRate = 0.05f,                     // ネットワークイベントの配送の 5% が失われる
    serializationDropRate = 0.05f,             // 同期変数の配送の 5% が失われる
    objectSyncDropRate = 0.05f,                // オブジェクトの位置の更新の 5% が失われる
    seed = 2,
});
```

| フィールド | 既定値 | 意味 |
|---|---|---|
| `latencyMin` / `latencyMax` | 0.05 / 0.15 秒 | 各区間（クライアント -> サーバー、サーバー -> クライアント）の片道の遅延。メッセージごとに一様に選ばれる。クライアント間のメッセージは 2 区間を通る。この幅がゆらぎで、種類の違うメッセージ同士は追い越すことがある（同じ送信者と受信者の間のネットワークイベントは順序を保つ） |
| `eventDropRate` | 0 | `SendCustomNetworkEvent` の、ある受信者への配送が失われる確率 |
| `serializationDropRate` | 0 | 同期変数（Manual / Continuous）の、ある受信者への配送が失われる確率 |
| `objectSyncDropRate` | 0 | オブジェクトの位置の更新の、ある受信者への配送が失われる確率 |
| `continuousInterval` | 0.2 秒 | Continuous 同期とオブジェクト同期の送信間隔 |
| `lateJoinStateDelay` | 0.3 秒 | 途中参加者が状態のスナップショットを受け取るまでの遅れ |
| `seed` | 1 | 遅延・欠落・ボットの乱数のシード |
| `trace` | "" | カンマ区切りのパスの一部。一致するオブジェクトの通信をログに出す |
| `carry` | true | ボットがオブジェクトを運ぶ（拾う -> 歩く -> 置く）。`false` にすると瞬間移動させる |

欠落は受信者ごとに決まるので、あるクライアントだけがメッセージを受け取り損ねることがあります。時間はシミュレーション上の
時間です。`timeScale: 4` にすると Editor は 4 倍速で進みますが、ワールドのスクリプトにとって 0.2 秒の遅延は 0.2 秒のままです。
スイート（`NetSimSuite`）では、同じ値を各ステップの文字列で指定します（「実行」を参照）。各レポートの先頭には実行時の設定が
書かれ、集計には失われたメッセージの数（`ev.dropped`, `ser.dropped`, `objsync.dropped`）が載ります。

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

ファイルはどの `Editor` フォルダに置いてもかまいません（NetSim のアセンブリは自動で参照されます）。アセンブリ定義を使う
場合は、Editor 専用にして `Haselab.NetSim.Editor` を参照してください。**Scenario Template** サンプルをひな形にできます。
スクリプトがコンパイルされると、シナリオは NetSim ウィンドウの一覧に現れます。`[NetSimScenario("doors", Description = "...")]`
と書くと、説明が 1 行表示されます。

よく使うメンバー:

| メンバー | 用途 |
|---|---|
| `ctx.AddClients(n, prefix, lateJoin)` | リモートクライアントを追加し、ビヘイビアが開始するまで待つ |
| `ctx.Check(name, ok)` | 期待を記録する。すべて `## Verdict` に並び、全部が満たされたときだけ PASS |
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
だけを運びます。組み込みの `NetSimBots.Random` は、ワールドについて何も知らずにこれを行います。ゲームの目的を知っているボットの
ほうが、はるかに多くの問題を見つけます。

## シナリオの実行

NetSim ウィンドウからは [シナリオ（Scenarios）](#シナリオscenarios) を参照してください。コードからは:

```csharp
// 新しい Play セッションで実行（Play モードに入る。Play 中なら入り直す）。終わったあとも Play モードのまま
Haselab.NetSim.NetSimScenarioRunner.RunInPlayMode("doors");
// いまの Play セッションで、いまのワールドの状態のまま実行
Haselab.NetSim.NetSimScenarioRunner.RunInPlayMode("doors", freshPlaySession: false);
// Play モードで、条件を指定して（null = ウィンドウの設定、timeScale < 0 = ウィンドウの速さ）
Haselab.NetSim.NetSimScenarioRunner.RunWhenReady("doors",
    new Haselab.NetSim.NetSimConfig { latencyMin = 0.1f, latencyMax = 0.4f, eventDropRate = 0.02f }, timeScale: 4);
```

複数のシナリオを無人で実行する（Play モードの再起動やドメインリロードをまたぎ、Editor が非アクティブでも動きます）:

```csharp
Haselab.NetSim.NetSimSuite.Start(new[] {
    // scenario|label|latMin|latMax|eventDrop|serializationDrop|objectSyncDrop|seed|timeScale[|trace[|teleport]]
    "doors|fast|0.03|0.08|0|0|0|1|4",
    "doors|slow-lossy|0.2|0.6|0.05|0.05|0.05|2|4",
    "doors",                                   // 省いた項目はウィンドウの設定になる
});
```

- レポート: `Logs/NetSim/<scenario>_<label>_<time>.md`、スイートの進捗と、終わった各ステップの判定:
  `Logs/NetSim/suite.log`。コードからは `NetSimSuite.Results` / `NetSimSuite.Finished` で結果を受け取れます。
- `swap:<dir>` ステップは、`<dir>` 以下のファイル（プロジェクトと同じ構成）をすべてプロジェクトにコピーして再コンパイル
  します。これを使うと、1 つのスイートの中で、ワールドのスクリプトの 2 つの版に対して同じシナリオを実行できます。

### コマンドライン

スクリプト、CI、AI エージェント向けです。そのプロジェクトを別の Unity Editor で開いていないこと。`-quit` は付けません。

```
Unity.exe -batchmode -projectPath <プロジェクト> -executeMethod Haselab.NetSim.NetSimCli.Run
          -netsimScene Assets/MyWorld.unity
          -netsimRun random-play -netsimRun "late-join|slow|0.2|0.6|0.05|0.05|0.05|2|4"
          -logFile <プロジェクト>/Logs/NetSim/cli-editor.log
```

- `-netsimRun` にはシナリオ名かスイートのステップ（上記）を渡し、何回でも指定できます。指定しないと、NetSim ウィンドウに
  保存したバッチの一覧を実行します。ステップで省いた項目は `ProjectSettings/NetSimSettings.json` の値になります。
- `-netsimList` は、知っているシナリオを `Logs/NetSim/scenarios.txt` に書いて終了します。
- 終了コード: **0** 全シナリオが合格（またはチェックなし）、**2** チェックの不合格あり、**1** エラー（知らないシナリオ、
  コンパイルエラー、例外、始まらなかったシナリオ）。`Logs/NetSim/cli-result.txt` に、ステップごとに
  `<判定>\t<ステップ>\t<レポート>` の 1 行が書かれます。
- `-batchmode` を付けないと、同じコマンドで Editor の画面を開いて実行し、終了します。

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
