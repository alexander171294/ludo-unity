using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public enum PlayerColor
{
    Red,
    Blue,
    Green,
    Yellow
}

public class Lobby : MonoBehaviour
{
    public GameObject step_initialMenu;              // default enabled
    public GameObject step_pickColorAndNickname;      // default disabled
    public GameObject step_lobbyWaitingPlayers;       // default disabled

    // ── Initial menu ───────────────────────────────────────────────
    public Button JoinButton;
    public GameObject JoinButtonDisabled;
    public TMP_InputField RoomCodeInput;
    public Button createRoomButton;
    public TextMeshProUGUI errorText;

    // ── Pick color and nickname ────────────────────────────────────
    public List<Button> colorButtons;                          // 0=Red,1=Blue,2=Yellow,3=Green (orden en escena / índices API)
    public List<Image> colorButtonsTaken;                      // overlay when taken
    public List<Image> colorButtonsSelectedIndicator;          // highlight when selected

    public TMP_InputField nicknameInput;
    public Button playButton;
    public GameObject playButtonDisabled;

    // ── Lobby waiting players ──────────────────────────────────────
    public List<GameObject> colorsIndicator;                   // indexed by PlayerColor
    public Button startGameButton;
    public GameObject startGameButtonDisabled;
    public TextMeshProUGUI codeToShareText;
    public Button copyCodeToShareButton;

    // ── Internal state ─────────────────────────────────────────────
    int   _selectedColorIndex = -1;
    Coroutine _pollRoutine;

    // ================================================================
    void Start()
    {
        ShowStep(step_initialMenu);

        // ── Step 1 listeners ──
        RoomCodeInput.onValueChanged.AddListener(OnRoomCodeChanged);
        createRoomButton.onClick.AddListener(OnCreateRoom);
        JoinButton.onClick.AddListener(OnJoinRoom);

        // ── Step 2 listeners ──
        for (int i = 0; i < colorButtons.Count; i++)
        {
            int idx = i;
            colorButtons[i].onClick.AddListener(() => OnColorSelected(idx));
        }
        nicknameInput.onValueChanged.AddListener(OnNicknameChanged);
        playButton.onClick.AddListener(OnPlay);

        // ── Step 3 listeners ──
        copyCodeToShareButton.onClick.AddListener(OnCopyCode);
        startGameButton.onClick.AddListener(OnStartGame);

        // Initial UI state
        ClearInitialError();
        UpdateJoinButtonState();
        UpdatePlayButtonState();
    }

    void ClearInitialError()
    {
        if (errorText != null)
            errorText.text = "";
    }

    void SetInitialError(string message)
    {
        if (errorText != null)
            errorText.text = message ?? "";
    }

    void OnDestroy()
    {
        StopPolling();
    }

    // ================================================================
    // STEP 1 — Initial Menu
    // ================================================================

    void OnRoomCodeChanged(string _)
    {
        ClearInitialError();
        UpdateJoinButtonState();
    }

    void UpdateJoinButtonState()
    {
        bool hasCode = !string.IsNullOrWhiteSpace(RoomCodeInput.text);
        JoinButton.gameObject.SetActive(hasCode);
        JoinButtonDisabled.SetActive(!hasCode);
    }

    void OnCreateRoom()
    {
        ClearInitialError();
        createRoomButton.interactable = false;
        LudoClient.Instance.CreateRoom(
            onSuccess: (gameId) =>
            {
                LudoClient.Instance.gameId = gameId;
                LudoClient.Instance.isHost = true;
                createRoomButton.interactable = true;
                EnterPickColorStep();
            },
            onError: (err) =>
            {
                SetInitialError(string.IsNullOrEmpty(err) ? "No se pudo crear la sala." : err);
                Debug.LogError("CreateRoom error: " + err);
                createRoomButton.interactable = true;
            }
        );
    }

    void OnJoinRoom()
    {
        ClearInitialError();
        string code = RoomCodeInput.text.Trim();
        JoinButton.interactable = false;
        LudoClient.Instance.GetRoomInfo(code, null,
            onSuccess: (info) =>
            {
                JoinButton.interactable = true;
                if (info.gamePhase != "waiting")
                {
                    SetInitialError("La partida ya comenzó o terminó.");
                    Debug.LogWarning("Game already started or finished");
                    return;
                }
                LudoClient.Instance.gameId = code;
                LudoClient.Instance.isHost = false;
                EnterPickColorStep();
            },
            onError: (err) =>
            {
                SetInitialError(string.IsNullOrEmpty(err) ? "Sala no encontrada." : err);
                Debug.LogError("JoinRoom error: " + err);
                JoinButton.interactable = true;
            }
        );
    }

    // ================================================================
    // STEP 2 — Pick Color & Nickname
    // ================================================================

    void EnterPickColorStep()
    {
        _selectedColorIndex = -1;
        ShowStep(step_pickColorAndNickname);
        nicknameInput.text = "";
        UpdatePlayButtonState();
        RefreshAvailableColors();
    }

    void RefreshAvailableColors()
    {
        LudoClient.Instance.GetAvailableColors(LudoClient.Instance.gameId, (colors) =>
        {
            HashSet<string> available = new HashSet<string>(colors);
            string[] colorNames = { "red", "blue", "yellow", "green" };
            for (int i = 0; i < colorButtons.Count; i++)
            {
                bool isTaken = !available.Contains(colorNames[i]);
                colorButtons[i].interactable = !isTaken;
                colorButtonsTaken[i].gameObject.SetActive(isTaken);
                colorButtonsSelectedIndicator[i].gameObject.SetActive(false);

                // If our selection was taken, deselect
                if (isTaken && _selectedColorIndex == i)
                    _selectedColorIndex = -1;
            }
            UpdatePlayButtonState();
        });
    }

    void OnColorSelected(int index)
    {
        _selectedColorIndex = index;
        for (int i = 0; i < colorButtonsSelectedIndicator.Count; i++)
            colorButtonsSelectedIndicator[i].gameObject.SetActive(i == index);
        UpdatePlayButtonState();
    }

    void OnNicknameChanged(string _) => UpdatePlayButtonState();

    void UpdatePlayButtonState()
    {
        bool ready = _selectedColorIndex >= 0 && !string.IsNullOrWhiteSpace(nicknameInput.text);
        playButton.gameObject.SetActive(ready);
        playButtonDisabled.SetActive(!ready);
    }

    void OnPlay()
    {
        string[] colorNames = { "red", "blue", "yellow", "green" };
        string color = colorNames[_selectedColorIndex];
        string nick = nicknameInput.text.Trim();

        playButton.interactable = false;
        LudoClient.Instance.JoinRoom(LudoClient.Instance.gameId, nick, color,
            onSuccess: (playerId) =>
            {
                LudoClient.Instance.playerId = playerId;
                LudoClient.Instance.playerColor = color;
                LudoClient.Instance.playerName = nick;
                playButton.interactable = true;
                EnterLobbyWaitingStep();
            },
            onError: (err) =>
            {
                Debug.LogError("JoinRoom error: " + err);
                playButton.interactable = true;
                // Refresh colors in case someone else took it
                RefreshAvailableColors();
            }
        );
    }

    // ================================================================
    // STEP 3 — Lobby Waiting Players
    // ================================================================

    void EnterLobbyWaitingStep()
    {
        ShowStep(step_lobbyWaitingPlayers);
        codeToShareText.text = LudoClient.Instance.gameId;

        // Disable all color indicators initially
        for (int i = 0; i < colorsIndicator.Count; i++)
            colorsIndicator[i].SetActive(false);

        UpdateStartButton(0);
        StartPolling();
    }

    void StartPolling()
    {
        StopPolling();
        _pollRoutine = StartCoroutine(PollLobby());
    }

    void StopPolling()
    {
        if (_pollRoutine != null)
        {
            StopCoroutine(_pollRoutine);
            _pollRoutine = null;
        }
    }

    IEnumerator PollLobby()
    {
        while (true)
        {
            yield return new WaitForSeconds(1f);
            bool waiting = true;
            LudoClient.Instance.GetRoomInfo(LudoClient.Instance.gameId, LudoClient.Instance.playerId,
                onSuccess: (info) =>
                {
                    UpdateLobbyPlayers(info);

                    if (info.gamePhase == "playing")
                    {
                        StopPolling();
                        SceneManager.LoadScene("Game");
                        waiting = false;
                    }
                },
                onError: (err) => Debug.LogError("Poll error: " + err)
            );
        }
    }

    void UpdateLobbyPlayers(RoomInfo info)
    {
        // Reset all indicators
        for (int i = 0; i < colorsIndicator.Count; i++)
            colorsIndicator[i].SetActive(false);

        // Activate indicators for joined players
        foreach (var player in info.players)
        {
            int idx = LudoClient.ColorToIndex(player.color);
            if (idx >= 0 && idx < colorsIndicator.Count)
            {
                colorsIndicator[idx].SetActive(true);
                SetNickToPlayerIndicatorGameObject(colorsIndicator[idx], player.name);
            }
        }

        UpdateStartButton(info.players.Length);
    }

    void UpdateStartButton(int playerCount)
    {
        bool canStart = LudoClient.Instance.isHost && playerCount >= 2;
        startGameButton.gameObject.SetActive(canStart);
        startGameButtonDisabled.SetActive(!canStart);
    }

    void OnCopyCode()
    {
        GUIUtility.systemCopyBuffer = LudoClient.Instance.gameId;
    }

    void OnStartGame()
    {
        startGameButton.interactable = false;
        LudoClient.Instance.StartGame(LudoClient.Instance.gameId,
            onSuccess: () =>
            {
                SceneManager.LoadScene("Game");
            },
            onError: (err) =>
            {
                Debug.LogError("StartGame error: " + err);
                startGameButton.interactable = true;
            }
        );
    }

    // ================================================================
    // Helpers
    // ================================================================

    void ShowStep(GameObject step)
    {
        step_initialMenu.SetActive(step == step_initialMenu);
        step_pickColorAndNickname.SetActive(step == step_pickColorAndNickname);
        step_lobbyWaitingPlayers.SetActive(step == step_lobbyWaitingPlayers);
    }

    void SetNickToPlayerIndicatorGameObject(GameObject playerIndicator, string nick)
    {
        playerIndicator.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = nick;
    }
}
