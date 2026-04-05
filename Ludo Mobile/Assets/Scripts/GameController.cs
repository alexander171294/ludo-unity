using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;

public class GameController : MonoBehaviour
{
    public List<PlayerController> players;       // 0=red, 1=blue, 2=yellow, 3=green
    public List<GameObject> boardPositions;       // p0-p51

    [Tooltip("Separación en Y local entre fichas apiladas en la misma casilla (relativo al transform de la casilla).")]
    public float stackYOffsetPerChip = 14f;

    [Tooltip("Pausa entre casillas al animar un movimiento (local u otro jugador).")]
    public float chipStepSeconds = 0.07f;

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

    // Valor de dado visto en el poll anterior (por color). lastMove a veces no cambia si el rival no mueve ficha.
    Dictionary<int, int> _prevDiceByColor = new Dictionary<int, int>();

    readonly HashSet<Chip> _chipsPathAnimating = new HashSet<Chip>();
    readonly Dictionary<Chip, Coroutine> _chipPathCoroutines = new Dictionary<Chip, Coroutine>();

    class PathAnimSpec
    {
        public string snapFrom;
        public List<string> steps = new List<string>();
    }

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
        foreach (var kv in _chipPathCoroutines)
        {
            if (kv.Value != null)
                StopCoroutine(kv.Value);
        }
        _chipPathCoroutines.Clear();
        _chipsPathAnimating.Clear();
    }

    // chips puede ser [] serializado en la escena (no null) — hay que instanciar igual.
    static bool NeedsChipInit(PlayerController pc) => pc != null && (pc.chips == null || pc.chips.Length == 0);

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
                        if (NeedsChipInit(pc))
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

        // Clicks / flags (canMovePiece, action, canRollDice) must refresh every poll — a fetch
        // right after select-piece can return the same version and would otherwise leave chips disabled.
        ApplyInteractionState(info, localColorIndex);

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

        Dictionary<(int ci, int pieceId), PathAnimSpec> pathAnims = null;
        if (info.lastMove != null && info.lastMove.moves != null && info.lastMove.moves.Length > 0
            && info.lastMove.moveId != _lastMoveId)
        {
            pathAnims = BuildPathAnimsFromLastMove(info.lastMove);
            _lastMoveId = info.lastMove.moveId;
            int moveColorIndex = LudoClient.ColorToIndex(info.lastMove.playerColor);
            bool isLocalPlayerMove = (moveColorIndex == localColorIndex);
            Debug.Log($"[LUDO] New move: {info.lastMove.playerColor} rolled {info.lastMove.diceValue} (local={isLocalPlayerMove})");
        }

        _lastVersion = info.version;

        Dictionary<string, int> positionCount = new Dictionary<string, int>();

        for (int p = 0; p < info.players.Length; p++)
        {
            var pd = info.players[p];
            int ci = LudoClient.ColorToIndex(pd.color);
            if (!_colorToPlayer.TryGetValue(ci, out var pc)) continue;

            if (NeedsChipInit(pc))
                pc.InitChips(this);

            for (int i = 0; i < pd.pieces.Length; i++)
            {
                if (i >= pc.chips.Length) break;
                var piece = pd.pieces[i];
                var chip = FindChipForPiece(pc, i, piece.id);
                if (chip == null) continue;

                chip.gameObject.SetActive(true);
                Transform target = ResolvePosition(piece.position, ci);
                if (target == null) continue;

                string posKey = StackKeyForPosition(piece.position, ci);
                if (!positionCount.ContainsKey(posKey))
                    positionCount[posKey] = 0;
                int stackIndex = positionCount[posKey];
                positionCount[posKey] = stackIndex + 1;
                stackIndex = Mathf.Min(stackIndex, 15);
                Vector3 offset = Vector3.up * (stackIndex * stackYOffsetPerChip);

                PathAnimSpec panim = null;
                bool hasPath = pathAnims != null
                    && pathAnims.TryGetValue((ci, piece.id), out panim)
                    && panim != null
                    && panim.steps != null
                    && panim.steps.Count > 0;

                if (hasPath)
                {
                    StopChipPathIfRunning(chip);
                    Coroutine co = StartCoroutine(RunChipStepPath(chip, ci, panim.snapFrom, panim.steps, target, offset));
                    _chipPathCoroutines[chip] = co;
                }
                else if (_chipsPathAnimating.Contains(chip))
                {
                    // No cortar el recorrido si el poll avanza versión antes de terminar la animación.
                }
                else
                {
                    StopChipPathIfRunning(chip);
                    chip.MoveTo(target, offset);
                }
            }
        }

        // Handle game finished
        if (info.gamePhase == "finished")
        {
            StopPolling();
            OnGameFinished(info.winner);
        }
    }

    void ApplyInteractionState(RoomInfo info, int localColorIndex)
    {
        for (int p = 0; p < info.players.Length; p++)
        {
            var pd = info.players[p];
            int ci = LudoClient.ColorToIndex(pd.color);
            if (!_colorToPlayer.TryGetValue(ci, out var pc)) continue;

            if (NeedsChipInit(pc))
                pc.InitChips(this);

            bool isCurrentTurn = (p == info.currentPlayer);

            bool showMoveHints = (ci == localColorIndex) && isCurrentTurn && info.canMovePiece
                && (pd.action == "select_piece" || pd.action == "move_piece");
            int diceForHints = pd.diceValue;
            bool diceAllowsSpawnExit = (diceForHints == 1 || diceForHints == 6);

            for (int i = 0; i < pd.pieces.Length; i++)
            {
                if (i >= pc.chips.Length) break;
                var piece = pd.pieces[i];
                var chip = FindChipForPiece(pc, i, piece.id);
                if (chip == null) continue;

                bool onSpawn = !string.IsNullOrEmpty(piece.position) && piece.position.StartsWith("sp");
                bool onEnd = !string.IsNullOrEmpty(piece.position)
                    && (piece.position == "ep" || piece.position.StartsWith("ep"));

                // Backend usa select_piece (elegir ficha) y move_piece (ejecutar / elegir destino según reglas del servidor).
                bool pieceTurn = info.canMovePiece
                              && (pd.action == "select_piece" || pd.action == "move_piece");
                bool clickable = (ci == localColorIndex) && isCurrentTurn && pieceTurn && !onEnd;
                chip.SetClickable(clickable);

                bool showChipIndicator = showMoveHints && !onEnd && (!onSpawn || diceAllowsSpawnExit);
                chip.SetIndicatorActive(showChipIndicator);
            }
        }

        if (localColorIndex >= 0 && _colorToPlayer.TryGetValue(localColorIndex, out var localPC))
        {
            if (info.canRollDice)
                EnableDiceClick(localPC);
            else
                DisableDiceClick(localPC);
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
            int dv = pd.diceValue;
            bool hadPrevDice = _prevDiceByColor.TryGetValue(ci, out int prevDice);

            if (ci != localColorIndex && hadPrevDice && dv >= 1 && dv <= 6 && dv != prevDice
                && (isCurrentTurn || prevDice == 0))
            {
                pc.dice.gameObject.SetActive(true);
                pc.dice.RollTo(dv, 8);
                _diceShowUntil[ci] = Time.time + DICE_SHOW_DURATION;
            }

            _prevDiceByColor[ci] = dv;

            bool diceTimerActive = _diceShowUntil.TryGetValue(ci, out float showUntil) && Time.time < showUntil;
            bool isLocalPlayer = (ci == localColorIndex);
            pc.UpdateState(pd, isCurrentTurn, info.gamePhase, isLocalPlayer, diceTimerActive);
        }
    }

    static string StackKeyForPosition(string position, int ci)
    {
        if (string.IsNullOrEmpty(position))
            return "_" + ci;
        string posKey = position + "_" + ci;
        if (position.StartsWith("p") && !position.StartsWith("ps"))
            posKey = position;
        return posKey;
    }

    Chip FindChipForPiece(PlayerController pc, int arrayIndex, int serverPieceId)
    {
        if (pc.chips == null) return null;
        for (int j = 0; j < pc.chips.Length; j++)
        {
            if (pc.chips[j] != null && pc.chips[j].pieceId == serverPieceId)
                return pc.chips[j];
        }
        if (arrayIndex >= 0 && arrayIndex < pc.chips.Length)
            return pc.chips[arrayIndex];
        return null;
    }

    void StopChipPathIfRunning(Chip chip)
    {
        if (chip == null) return;
        if (_chipPathCoroutines.TryGetValue(chip, out Coroutine co) && co != null)
            StopCoroutine(co);
        _chipPathCoroutines.Remove(chip);
        _chipsPathAnimating.Remove(chip);
    }

    IEnumerator RunChipStepPath(Chip chip, int ci, string snapFrom, List<string> steps, Transform finalTarget, Vector3 finalOffset)
    {
        _chipsPathAnimating.Add(chip);
        float stepDelay = chipStepSeconds > 0f ? chipStepSeconds : 0.05f;
        try
        {
            if (!string.IsNullOrEmpty(snapFrom))
            {
                Transform st = ResolvePosition(snapFrom, ci);
                if (st != null)
                    chip.MoveTo(st, Vector3.zero);
            }

            for (int s = 0; s < steps.Count; s++)
            {
                bool isLast = (s == steps.Count - 1);
                Transform t = ResolvePosition(steps[s], ci);
                if (t == null) continue;
                Vector3 off = isLast ? finalOffset : Vector3.zero;
                chip.MoveTo(t, off);
                if (s < steps.Count - 1)
                    yield return new WaitForSeconds(stepDelay);
            }

            if (finalTarget != null)
                chip.MoveTo(finalTarget, finalOffset);
        }
        finally
        {
            _chipsPathAnimating.Remove(chip);
            _chipPathCoroutines.Remove(chip);
        }
    }

    Dictionary<(int ci, int pieceId), PathAnimSpec> BuildPathAnimsFromLastMove(LastMoveData lm)
    {
        var dict = new Dictionary<(int ci, int pieceId), PathAnimSpec>();
        foreach (var m in lm.moves)
        {
            int ci = LudoClient.ColorToIndex(m.playerColor);
            var key = (ci, m.pieceId);
            if (!dict.TryGetValue(key, out PathAnimSpec pa))
            {
                pa = new PathAnimSpec { snapFrom = m.fromPosition };
                dict[key] = pa;
            }
            pa.steps.AddRange(BuildStepTargets(m.fromPosition, m.toPosition, ci));
        }
        return dict;
    }

    List<string> BuildStepTargets(string from, string to, int ci)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(to) || from == to)
            return list;
        List<string> ring = TryExpandMainRingForward(from, to);
        if (ring != null && ring.Count > 0)
        {
            list.AddRange(ring);
            return list;
        }
        List<string> colorPath = TryExpandColorPath(from, to, ci);
        if (colorPath != null && colorPath.Count > 0)
        {
            list.AddRange(colorPath);
            return list;
        }
        list.Add(to);
        return list;
    }

    static bool IsEndCellId(string posId)
    {
        return !string.IsNullOrEmpty(posId) && (posId == "ep" || posId.StartsWith("ep"));
    }

    static bool TryParseColorPathIndex(string posId, out int index1Based)
    {
        index1Based = 0;
        if (string.IsNullOrEmpty(posId) || !posId.StartsWith("cp")) return false;
        if (!int.TryParse(posId.Substring(2), out index1Based) || index1Based < 1) return false;
        return true;
    }

    int GetColorPathMaxIndex(int ci)
    {
        if (_colorToPlayer.TryGetValue(ci, out var pc) && pc.colorPositions != null && pc.colorPositions.Count > 0)
            return pc.colorPositions.Count;
        return 5;
    }

    /// <summary>
    /// Pasos intermedios en columna de color (cp1→cpN) y hacia meta (ep). El anillo ya lo cubre TryExpandMainRingForward.
    /// </summary>
    List<string> TryExpandColorPath(string from, string to, int ci)
    {
        int maxCp = GetColorPathMaxIndex(ci);
        bool fromCp = TryParseColorPathIndex(from, out int fromCi);
        bool toCp = TryParseColorPathIndex(to, out int toCi);
        bool fromRingOrSpawn = TryGetBoardCellIndex(from, out _)
            || (!string.IsNullOrEmpty(from) && from.StartsWith("sp"));

        // cp_a → cp_b (misma columna, hacia delante)
        if (fromCp && toCp)
        {
            if (toCi <= fromCi) return null;
            toCi = Mathf.Min(toCi, maxCp);
            var steps = new List<string>();
            for (int k = fromCi + 1; k <= toCi; k++)
                steps.Add("cp" + k);
            return steps.Count > 0 ? steps : null;
        }

        // Anillo o salida (sp) → casilla de color (entrada a columna)
        if (fromRingOrSpawn && toCp)
        {
            toCi = Mathf.Min(toCi, maxCp);
            var steps = new List<string>();
            for (int k = 1; k <= toCi; k++)
                steps.Add("cp" + k);
            return steps.Count > 0 ? steps : null;
        }

        // cp_* → meta
        if (fromCp && IsEndCellId(to))
        {
            var steps = new List<string>();
            for (int k = fromCi + 1; k <= maxCp; k++)
                steps.Add("cp" + k);
            steps.Add("ep");
            return steps;
        }

        return null;
    }

    List<string> TryExpandMainRingForward(string from, string to)
    {
        if (boardPositions == null || boardPositions.Count == 0)
            return null;
        if (!TryGetBoardCellIndex(from, out int a) || !TryGetBoardCellIndex(to, out int b))
            return null;
        int n = boardPositions.Count;
        var list = new List<string>();
        int cur = a;
        for (int guard = 0; cur != b && guard < n + 2; guard++)
        {
            cur = (cur + 1) % n;
            list.Add("p" + cur);
        }
        return list.Count > 0 ? list : null;
    }

    bool TryGetBoardCellIndex(string posId, out int idx)
    {
        idx = -1;
        if (string.IsNullOrEmpty(posId)) return false;
        if (posId.StartsWith("p") && !posId.StartsWith("ps"))
        {
            string numStr = posId.Substring(1);
            if (int.TryParse(numStr, out idx) && idx >= 0 && idx < boardPositions.Count)
                return true;
        }
        idx = -1;
        return false;
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
