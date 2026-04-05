using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;
using System.Text;

// ─── DTOs (JsonUtility-serializable) ───────────────────────────────

[Serializable] public class CreateRoomResponse   { public string gameId; public string message; }
[Serializable] public class JoinRoomResponse      { public bool success; public string message; public string gameId; public string playerId; }
[Serializable] public class AvailableColorsResponse { public string[] colors; }
[Serializable] public class GenericResponse       { public bool success; public string message; }
[Serializable] public class RollDiceResponse      { public bool success; public string message; public int diceValue; }

[Serializable] public class RoomInfo
{
    public string   gameId;
    public PlayerData[] players;
    public int      currentPlayer;
    public int      diceValue;
    public string   gamePhase;      // "waiting" | "playing" | "finished"
    public string   winner;
    public bool     gameStarted;
    public string[] availableColors;
    public bool     canRollDice;
    public bool     canMovePiece;
    public int      selectedPieceId;
    public int      decisionDuration;
    public string   lastUpdated;
    public int      version;
    public LastMoveData lastMove;
}

[Serializable] public class PlayerData
{
    public string     id;
    public string     name;
    public string     color;
    public PieceData[] pieces;
    public float      actionTimeLeft;  // 0-100
    public string     action;          // "roll_dice" | "rolling" | "select_piece" | "move_piece"
    public int        diceValue;
}

[Serializable] public class PieceData
{
    public int    id;
    public string position;        // "sp1"-"sp4", "p0"-"p51", "cp1"-"cp5", "ep"
    public bool   isInStartZone;
    public bool   isInBoard;
    public bool   isInColorPath;
    public bool   isInEndPath;
    public int    boardPosition;
    public int    colorPathPosition;
    public int    endPathPosition;
}

[Serializable] public class LastMoveData
{
    public string     moveId;
    public MoveData[] moves;
    public string     playerColor;
    public int        diceValue;
    public string     timestamp;
}

[Serializable] public class MoveData
{
    public int    pieceId;
    public string playerColor;
    public string fromPosition;
    public string toPosition;
    public string moveType;
}

// Helper for POST body
[Serializable] class JoinBody    { public string name; public string color; }
[Serializable] class PieceIdBody { public int pieceId; }

// ─── Singleton HTTP Client + Session State ─────────────────────────

public class LudoClient : MonoBehaviour
{
    public static LudoClient Instance { get; private set; }

    /// <summary>Se dispara al volver al primer plano (Android/iOS: multitarea, panel de notificaciones, etc.).</summary>
    public static event Action OnApplicationResumed;

    public string baseUrl = "https://api-ludo.deepnova.app/ludo";

    static float s_lastResumeEventTime = -1000f;
    const float ResumeEventMinInterval = 0.2f;

    static void TryInvokeApplicationResumed()
    {
        float now = Time.realtimeSinceStartup;
        if (now - s_lastResumeEventTime < ResumeEventMinInterval)
            return;
        s_lastResumeEventTime = now;
        OnApplicationResumed?.Invoke();
    }

    void OnApplicationPause(bool pauseStatus)
    {
        if (!pauseStatus)
            TryInvokeApplicationResumed();
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
            TryInvokeApplicationResumed();
    }

    // Session state (persists across scenes)
    [NonSerialized] public string gameId;
    [NonSerialized] public string playerId;
    [NonSerialized] public string playerColor;
    [NonSerialized] public string playerName;
    [NonSerialized] public bool   isHost;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        var go = new GameObject("LudoClient");
        Instance = go.AddComponent<LudoClient>();
        DontDestroyOnLoad(go);
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ── Color helpers ──────────────────────────────────────────────

    public static int ColorToIndex(string color)
    {
        switch (color)
        {
            case "red":    return 0;
            case "blue":   return 1;
            case "yellow": return 2;
            case "green":  return 3;
            default:       return -1;
        }
    }

    public static string IndexToColor(int index)
    {
        switch (index)
        {
            case 0: return "red";
            case 1: return "blue";
            case 2: return "yellow";
            case 3: return "green";
            default: return "";
        }
    }

    public static PlayerColor StringToPlayerColor(string color)
    {
        switch (color)
        {
            case "red":    return PlayerColor.Red;
            case "blue":   return PlayerColor.Blue;
            case "yellow": return PlayerColor.Yellow;
            case "green":  return PlayerColor.Green;
            default:       return PlayerColor.Red;
        }
    }

    public static string PlayerColorToString(PlayerColor c)
    {
        switch (c)
        {
            case PlayerColor.Red:    return "red";
            case PlayerColor.Blue:   return "blue";
            case PlayerColor.Yellow: return "yellow";
            case PlayerColor.Green:  return "green";
            default: return "red";
        }
    }

    // ── API Methods ────────────────────────────────────────────────

    public void CreateRoom(Action<string> onSuccess, Action<string> onError = null)
    {
        StartCoroutine(Post($"{baseUrl}/create-game", "{}", (json) =>
        {
            var r = JsonUtility.FromJson<CreateRoomResponse>(json);
            onSuccess?.Invoke(r.gameId);
        }, onError));
    }

    public void GetRoomInfo(string gameId, string playerId, Action<RoomInfo> onSuccess, Action<string> onError = null)
    {
        string url = string.IsNullOrEmpty(playerId)
            ? $"{baseUrl}/game/{gameId}"
            : $"{baseUrl}/game/{gameId}?playerId={playerId}";
        StartCoroutine(Get(url, (json) =>
        {
            var r = JsonUtility.FromJson<RoomInfo>(json);
            onSuccess?.Invoke(r);
        }, onError));
    }

    public void GetAvailableColors(string gameId, Action<string[]> onSuccess, Action<string> onError = null)
    {
        StartCoroutine(Get($"{baseUrl}/available-colors/{gameId}", (json) =>
        {
            var r = JsonUtility.FromJson<AvailableColorsResponse>(json);
            onSuccess?.Invoke(r.colors);
        }, onError));
    }

    public void JoinRoom(string gameId, string playerName, string color, Action<string> onSuccess, Action<string> onError = null)
    {
        var body = JsonUtility.ToJson(new JoinBody { name = playerName, color = color });
        StartCoroutine(Post($"{baseUrl}/game/{gameId}/join", body, (json) =>
        {
            var r = JsonUtility.FromJson<JoinRoomResponse>(json);
            if (r.success)
                onSuccess?.Invoke(r.playerId);
            else
                onError?.Invoke(r.message);
        }, onError));
    }

    public void StartGame(string gameId, Action onSuccess, Action<string> onError = null)
    {
        StartCoroutine(Post($"{baseUrl}/game/{gameId}/start", "{}", (json) =>
        {
            onSuccess?.Invoke();
        }, onError));
    }

    public void RollDice(string gameId, string playerId, Action<int> onSuccess, Action<string> onError = null)
    {
        StartCoroutine(Post($"{baseUrl}/game/{gameId}/player/{playerId}/roll-dice", "{}", (json) =>
        {
            var r = JsonUtility.FromJson<RollDiceResponse>(json);
            onSuccess?.Invoke(r.diceValue);
        }, onError));
    }

    public void SelectPiece(string gameId, string playerId, int pieceId, Action onSuccess, Action<string> onError = null)
    {
        var body = JsonUtility.ToJson(new PieceIdBody { pieceId = pieceId });
        StartCoroutine(Post($"{baseUrl}/game/{gameId}/player/{playerId}/select-piece", body, (json) =>
        {
            onSuccess?.Invoke();
        }, onError));
    }

    // ── HTTP Helpers ───────────────────────────────────────────────

    IEnumerator Get(string url, Action<string> onSuccess, Action<string> onError)
    {
        float t0 = Time.realtimeSinceStartup;
        using (var req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();
            float ms = (Time.realtimeSinceStartup - t0) * 1000f;
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[HTTP] GET {url} FAILED ({ms:F0}ms): {req.error}");
                onError?.Invoke(req.error);
            }
            else
            {
                onSuccess?.Invoke(req.downloadHandler.text);
            }
        }
    }

    IEnumerator Post(string url, string jsonBody, Action<string> onSuccess, Action<string> onError)
    {
        float t0 = Time.realtimeSinceStartup;
        using (var req = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();
            float ms = (Time.realtimeSinceStartup - t0) * 1000f;
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[HTTP] POST {url} FAILED ({ms:F0}ms): {req.error}");
                onError?.Invoke(req.error);
            }
            else
            {
                onSuccess?.Invoke(req.downloadHandler.text);
            }
        }
    }
}
