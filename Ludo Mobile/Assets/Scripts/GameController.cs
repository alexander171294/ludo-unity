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

    // Dice visibility timer per color index — keeps dice visible after roll animation
    Dictionary<int, float> _diceShowUntil = new Dictionary<int, float>();
    const float DICE_SHOW_DURATION = 2.5f;

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
            yield return new WaitForSeconds(0.5f);
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
        var client = LudoClient.Instance;
        int localColorIndex = LudoClient.ColorToIndex(client.playerColor);

        // ALWAYS update timers + dice visibility (these change even without version bump)
        UpdateTimersAndDice(info, localColorIndex);

        // Skip heavy updates if version hasn't changed
        if (info.version == _lastVersion) return;

        Debug.Log($"[LUDO] State v{_lastVersion}→v{info.version} phase={info.gamePhase} " +
                  $"turn={info.currentPlayer} canRoll={info.canRollDice} canMove={info.canMovePiece}");

        // Log local player details
        for (int lp = 0; lp < info.players.Length; lp++)
        {
            var lpd = info.players[lp];
            if (LudoClient.ColorToIndex(lpd.color) == localColorIndex)
            {
                Debug.Log($"[LUDO] Local player ({lpd.color}) idx={lp} action={lpd.action} dice={lpd.diceValue} " +
                          $"pieces=[{string.Join(",", System.Array.ConvertAll(lpd.pieces, p => p.position))}]");
                break;
            }
        }

        _lastVersion = info.version;

        // Count how many chips land on each position for stacking offset
        Dictionary<string, int> positionCount = new Dictionary<string, int>();

        // Update each player + move chips
        for (int p = 0; p < info.players.Length; p++)
        {
            var pd = info.players[p];
            int ci = LudoClient.ColorToIndex(pd.color);
            if (!_colorToPlayer.TryGetValue(ci, out var pc)) continue;

            // Init chips if new player appeared mid-game
            if (pc.chips == null)
                pc.InitChips(this);

            bool isCurrentTurn = (p == info.currentPlayer);

            // Move chips to correct positions with stack offset
            for (int i = 0; i < pd.pieces.Length; i++)
            {
                if (i >= pc.chips.Length) break;
                var piece = pd.pieces[i];
                var chip = pc.chips[i];

                chip.gameObject.SetActive(true);
                Transform target = ResolvePosition(piece.position, ci);
                if (target != null)
                {
                    // Stack offset: count chips already placed at this position
                    string posKey = piece.position + "_" + ci;
                    // For board positions (shared), use just the position as key
                    if (piece.position.StartsWith("p") && !piece.position.StartsWith("ps"))
                        posKey = piece.position;

                    if (!positionCount.ContainsKey(posKey))
                        positionCount[posKey] = 0;
                    int stackIndex = positionCount[posKey];
                    positionCount[posKey] = stackIndex + 1;

                    Vector3 offset = Vector3.up * (stackIndex * 15f);
                    chip.MoveTo(target, offset);
                }

                // Clickable only if it's our turn, we can move, and action is select_piece
                bool clickable = (ci == localColorIndex)
                              && isCurrentTurn
                              && info.canMovePiece
                              && pd.action == "select_piece";
                chip.SetClickable(clickable);

                if (clickable)
                    Debug.Log($"[LUDO] Chip {piece.id} ({pd.color}) clickable at {piece.position}");
            }
        }

        // Animate dice if lastMove changed (only for OTHER players — local already animated on click)
        if (info.lastMove != null && info.lastMove.moveId != _lastMoveId)
        {
            _lastMoveId = info.lastMove.moveId;
            int moveColorIndex = LudoClient.ColorToIndex(info.lastMove.playerColor);
            bool isLocalPlayerMove = (moveColorIndex == localColorIndex);
            Debug.Log($"[LUDO] New move: {info.lastMove.playerColor} rolled {info.lastMove.diceValue} (local={isLocalPlayerMove})");
            if (!isLocalPlayerMove && _colorToPlayer.TryGetValue(moveColorIndex, out var movePC))
            {
                movePC.dice.gameObject.SetActive(true);
                movePC.dice.RollTo(info.lastMove.diceValue, 8);
                _diceShowUntil[moveColorIndex] = Time.time + DICE_SHOW_DURATION;
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

    // Called every poll, even if version hasn't changed — keeps timers and dice fresh
    void UpdateTimersAndDice(RoomInfo info, int localColorIndex)
    {
        for (int p = 0; p < info.players.Length; p++)
        {
            var pd = info.players[p];
            int ci = LudoClient.ColorToIndex(pd.color);
            if (!_colorToPlayer.TryGetValue(ci, out var pc)) continue;

            bool isCurrentTurn = (p == info.currentPlayer);
            bool diceTimerActive = _diceShowUntil.TryGetValue(ci, out float showUntil) && Time.time < showUntil;
            pc.UpdateState(pd, isCurrentTurn, info.gamePhase, diceTimerActive);
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
        Debug.Log("[LUDO] >>> DICE CLICKED");
        int localColorIndex = LudoClient.ColorToIndex(client.playerColor);
        if (_colorToPlayer.TryGetValue(localColorIndex, out var localPC))
            DisableDiceClick(localPC);

        client.RollDice(client.gameId, client.playerId,
            onSuccess: (diceValue) =>
            {
                Debug.Log($"[LUDO] <<< DICE ROLLED = {diceValue}");
                // Animate dice immediately for local player
                if (_colorToPlayer.TryGetValue(localColorIndex, out var pc))
                {
                    pc.dice.gameObject.SetActive(true);
                    pc.dice.RollTo(diceValue, 8);
                    _diceShowUntil[localColorIndex] = Time.time + DICE_SHOW_DURATION;
                }
                // Fetch immediately to update state without waiting for poll
                FetchNow();
            },
            onError: (err) => Debug.LogError("RollDice error: " + err)
        );
    }

    // ── Chip Interaction ───────────────────────────────────────────

    public void OnChipClicked(int pieceId)
    {
        var client = LudoClient.Instance;
        Debug.Log($"[LUDO] >>> CHIP CLICKED pieceId={pieceId} gameId={client.gameId} playerId={client.playerId}");

        // Disable all chip clicks immediately to prevent double-clicks
        int localColorIndex = LudoClient.ColorToIndex(client.playerColor);
        if (_colorToPlayer.TryGetValue(localColorIndex, out var localPC) && localPC.chips != null)
        {
            foreach (var c in localPC.chips)
                c.SetClickable(false);
        }

        client.SelectPiece(client.gameId, client.playerId, pieceId,
            onSuccess: () =>
            {
                Debug.Log($"[LUDO] <<< SELECT PIECE OK pieceId={pieceId}");
                FetchNow();
            },
            onError: (err) => Debug.LogError($"[LUDO] <<< SELECT PIECE FAILED pieceId={pieceId}: {err}")
        );
    }

    // ── Immediate Fetch (after actions) ──────────────────────────

    void FetchNow()
    {
        var client = LudoClient.Instance;
        client.GetRoomInfo(client.gameId, client.playerId,
            onSuccess: UpdateFromRoomInfo,
            onError: (err) => Debug.LogWarning("FetchNow error: " + err)
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
