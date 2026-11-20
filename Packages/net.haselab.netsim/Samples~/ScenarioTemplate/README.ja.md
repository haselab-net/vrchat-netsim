# シナリオのひな形

[English](README.md) | 日本語

`MyWorldScenarios.cs` は、自分のワールドを知っているシナリオのひな形です。クライアントを追加し、ボットに遊ばせ、
途中参加者を加え、全クライアントが一致するかを確かめます（`ctx.Check` -> レポートの `## Verdict` に PASS / FAIL）。

1. ファイルをプロジェクトの `Editor` フォルダにコピーし（ここに置いたままでもよい）、シナリオの名前を変えます。
2. `Bot` を、自分のワールドでプレイヤーがすること（ラウンドを始める、正しいボタンを押す、`NetSim.StartCarry` で物を運ぶ）
   に置き換え、全クライアントで同じになるべきもの（スコア、ゲームの状態など）のチェックを足します。
3. *Tools > NetSim > NetSim Window* > **Scenarios** から実行します。スクリプトがコンパイルされると一覧に現れます。

組み込みのシナリオ（`join-check`, `random-play`, `late-join`, `master-leave`）はコード不要ですが、ランダムにボタンを
押すだけです。シナリオの API は[マニュアル](../../Documentation~/README.ja.md#シナリオの書き方)を参照してください。
