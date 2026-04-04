using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;

public class GameController : MonoBehaviour
{
    public List<PlayerController> players;       // 0=red, 1=blue, 2=yellow, 3=green
    public List<GameObject> boardPositions;       // p0-p51

    // ── Internal state ─────────────────────────────────────────────
    int      _lastVersion = -1;
    string   _lastMoveId;
    bool     _polling;
    Coroutine _pollRoutine;

    // Map: colorIndex → PlayerController
    Dictionary<int, PlayerController> _colorToPlayer;

    void Start()
    {
        var client = LudoClient.Instance;
        if (client == null || string.IsNullOrEmpty(client.gameId))
        {
            Debug.LogError("GameController: No active game session");
            return;
        }

        // Build color → PlayerController map
        _colorToPlayer = new Dictionary<int, PlayerController>();
        for (int i = 0; i < players.Count; i++)
        {
            int ci = LudoClient.ColorToIndex(LudoClient.PlayerColorToString(players[i].playerColor));
            _colorToPlayer[ci] = players[i];
        }

        // Deactivate all players initially (will be activated by poll)
        foreach (var pc in players)
            pc.Deactivate();

        // First fetch to initialize chips + state, then start polling
        FetchAndInit();
    }

    void OnDestroy()
    {
        StopPolling();
    }

    void FetchAndInit()
    {
        var client = LudoClient.Instance;
        client.GetRoomInfo(client.gameId, client.playerId,
            onSuccess: (info) =>
            {
                // Initialize chips for players that are in the game
                foreach (var pd in info.players)
                {
                    int ci = LudoClient.ColorToIndex(pd.color);
                    if (_colorToPlayer.TryGetValue(ci, out var pc))
                    {
                        if (pc.chips == null)
                            pc.InitChips(this);
                    }
                }
                UpdateFromRoomInfo(info);
                StartPolling();
            },
            onError: (err) =>
            {
                Debug.LogError("Initial fetch error: " + err);
                // Retry after a delay
                StartCoroutine(RetryInit());
            }
        );
    }

    IEnumerator RetryInit()
    {
        yield return new WaitForSeconds(2f);
        FetchAndInit();
    }

    // ── Polling ────────────────────────────────────────────────────

    void StartPolling()
    {
        StopPolling();
        _polling = true;
        _pollRoutine = StartCoroutine(PollLoop());
    }

    void StopPolling()
    {
        _polling = false;
        if (_pollRoutine != null)
        {
            StopCoroutine(_pollRoutine);
            _pollRoutine = null;
        }
    }

    IEnumerator PollLoop()
    {
        while (_polling)
        {
            yield return new WaitForSeconds(1f);
            var client = LudoClient.Instance;
            client.GetRoomInfo(client.gameId, client.playerId,
                onSuccess: UpdateFromRoomInfo,
                onError: (err) => Debug.LogWarning("Poll error: " + err)
            );
        }
    }

    // ── State Update ───────────────────────────────────────────────

    void UpdateFromRoomInfo(RoomInfo info)
    {
        if (info.version == _lastVersion) return;
        _lastVersion = info.version;

        var client = LudoClient.Instance;
        int localColorIndex = LudoClient.ColorToIndex(client.playerColor);

        // Update each player
        for (int p = 0; p < info.players.Length; p++)
        {
            var pd = info.players[p];
            int ci = LudoClient.ColorToIndex(pd.color);
            if (!_colorToPlayer.TryGetValue(ci, out var pc)) continue;

            // Init chips if new player appeared mid-game
            if (pc.chips == null)
                pc.InitChips(this);

            bool isCurrentTurn = (p == info.currentPlayer);
            pc.UpdateState(pd, isCurrentTurn, info.gamePhase);

            // Move chips to correct positions
            for (int i = 0; i < pd.pieces.Length; i++)
            {
                if (i >= pc.chips.Length) break;
                var piece = pd.pieces[i];
                var chip = pc.chips[i];

                chip.gameObject.SetActive(true);
                Transform target = ResolvePosition(piece.position, ci);
                if (target != null)
                    chip.MoveTo(target);

                // Clickable only if it's our turn, we can move, and action is select_piece
                bool clickable = (ci == localColorIndex)
                              && isCurrentTurn
                              && info.canMovePiece
                              && pd.action == "select_piece";
                chip.SetClickable(clickable);
            }
        }

        // Animate dice if lastMove changed
        if (info.lastMove != null && info.lastMove.moveId != _lastMoveId)
        {
            _lastMoveId = info.lastMove.moveId;
            int moveColorIndex = LudoClient.ColorToIndex(info.lastMove.playerColor);
            if (_colorToPlayer.TryGetValue(moveColorIndex, out var movePC))
            {
                movePC.dice.gameObject.SetActive(true);
                movePC.dice.RollTo(info.lastMove.diceValue, 8);
            }
        }

        // Handle dice click for local player
        if (localColorIndex >= 0 && _colorToPlayer.TryGetValue(localColorIndex, out var localPC))
        {
            if (info.canRollDice)
                EnableDiceClick(localPC);
            else
                DisableDiceClick(localPC);
        }

        // Handle game finished
        if (info.gamePhase == "finished")
        {
            StopPolling();
            OnGameFinished(info.winner);
        }
    }

    // ── Position Resolution ────────────────────────────────────────

    Transform ResolvePosition(string posId, int colorIndex)
    {
        if (string.IsNullOrEmpty(posId)) return null;

        // Board positions: p0-p51
        if (posId.StartsWith("p") && !posId.StartsWith("ps"))
        {
            string numStr = posId.Substring(1);
            if (int.TryParse(numStr, out int idx) && idx >= 0 && idx < boardPositions.Count)
                return boardPositions[idx].transform;
        }

        // Player-specific positions: sp, cp, ep
        if (_colorToPlayer.TryGetValue(colorIndex, out var pc))
            return pc.GetPositionTransform(posId);

        return null;
    }

    // ── Dice Interaction ───────────────────────────────────────────

    void EnableDiceClick(PlayerController pc)
    {
        pc.dice.gameObject.SetActive(true);
        var btn = pc.dice.GetComponent<Button>();
        if (btn == null) btn = pc.dice.gameObject.AddComponent<Button>();
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(OnDiceClicked);
    }

    void DisableDiceClick(PlayerController pc)
    {
        var btn = pc.dice.GetComponent<Button>();
        if (btn != null)
            btn.onClick.RemoveAllListeners();
    }

    void OnDiceClicked()
    {
        var client = LudoClient.Instance;
        int localColorIndex = LudoClient.ColorToIndex(client.playerColor);
        if (_colorToPlayer.TryGetValue(localColorIndex, out var localPC))
            DisableDiceClick(localPC);

        client.RollDice(client.gameId, client.playerId,
            onSuccess: (diceValue) =>
            {
                // Dice animation will be triggered by the next poll when lastMove updates
                Debug.Log("Dice rolled: " + diceValue);
            },
            onError: (err) => Debug.LogError("RollDice error: " + err)
        );
    }

    // ── Chip Interaction ───────────────────────────────────────────

    public void OnChipClicked(int pieceId)
    {
        var client = LudoClient.Instance;

        // Disable all chip clicks immediately to prevent double-clicks
        int localColorIndex = LudoClient.ColorToIndex(client.playerColor);
        if (_colorToPlayer.TryGetValue(localColorIndex, out var localPC) && localPC.chips != null)
        {
            foreach (var c in localPC.chips)
                c.SetClickable(false);
        }

        client.SelectPiece(client.gameId, client.playerId, pieceId,
            onSuccess: () => Debug.Log("Piece selected: " + pieceId),
            onError: (err) => Debug.LogError("SelectPiece error: " + err)
        );
    }

    // ── Game Finished ──────────────────────────────────────────────

    void OnGameFinished(string winnerId)
    {
        // Find winner name
        var client = LudoClient.Instance;
        string winnerMsg = (winnerId == client.playerId) ? "You win!" : "Game Over!";
        Debug.Log("Game finished: " + winnerMsg + " (winner: " + winnerId + ")");

        // TODO: Show a winner overlay UI — for now the game state is frozen with final positions
    }
}
