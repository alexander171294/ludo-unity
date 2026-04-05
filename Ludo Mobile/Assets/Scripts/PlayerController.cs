using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class PlayerController : MonoBehaviour
{
    public PlayerColor playerColor;
    public List<GameObject> spawnPositions;    // 0-3 (sp1, sp2, sp3, sp4)
    public List<GameObject> colorPositions;    // 0-4 (cp1, cp2, cp3, cp4, cp5)
    public GameObject endPosition;
    public Dice dice;
    public TextMeshProUGUI nickText;
    public Image playerTimmer;                 // fill horizontal 1→0
    public GameObject chipPrefab;
    public GameObject UI_playerCard;           // disabled by default
    public GameObject activeTurnIndicator;     // hijo "active": turno de este jugador en partida
    public GameObject youIndicator;         // texto "eres tu!"

    [HideInInspector, System.NonSerialized] public Chip[] chips; // runtime; no serializar (evita [] en escena que rompía InitChips)
    [HideInInspector] public bool isActive;    // player joined the game

    public void InitChips(GameController controller)
    {
        int ci = LudoClient.ColorToIndex(LudoClient.PlayerColorToString(playerColor));
        chips = new Chip[4];
        for (int i = 0; i < 4; i++)
        {
            var go = Instantiate(chipPrefab, spawnPositions[i].transform.position, Quaternion.identity, transform);
            var chip = go.GetComponent<Chip>();
            chip.Setup(i, ci, controller);
            chip.SetClickable(false);
            chips[i] = chip;
        }
    }

    public void UpdateState(PlayerData data, bool isCurrentTurn, string gamePhase, bool isLocalPlayer, bool diceTimerActive = false)
    {
        isActive = true;
        UI_playerCard.SetActive(true);
        nickText.text = data.name;

        // Timer
        float timeLeft = data.actionTimeLeft;
        playerTimmer.fillAmount = timeLeft / 100f;

        // Dado: el jugador local solo lo ve al tirar; rivales siguen viendo valor tras tirada (timer) o al mover.
        bool showDice;
        if (isLocalPlayer)
        {
            showDice = isCurrentTurn && (
                data.action == "roll_dice"
                || data.action == "rolling");
        }
        else
        {
            showDice = isCurrentTurn && (
                data.action == "roll_dice"
                || data.action == "rolling"
                || ((data.action == "select_piece" || data.action == "move_piece") && data.diceValue >= 1));
            if (diceTimerActive)
                showDice = true;
        }
        dice.gameObject.SetActive(showDice);
        if (showDice && data.diceValue >= 1 && data.diceValue <= 6 && !dice.IsRolling)
            dice.RollTo(data.diceValue, 0);

        if (activeTurnIndicator != null)
            activeTurnIndicator.SetActive(isCurrentTurn && gamePhase == "playing");

        if (youIndicator != null)
            youIndicator.SetActive(isLocalPlayer);
    }

    public void Deactivate()
    {
        isActive = false;
        UI_playerCard.SetActive(false);
        dice.gameObject.SetActive(false);
        if (activeTurnIndicator != null)
            activeTurnIndicator.SetActive(false);
        if (youIndicator != null)
            youIndicator.SetActive(false);
        if (chips != null)
        {
            foreach (var c in chips)
                if (c != null) c.gameObject.SetActive(false);
        }
    }

    public Transform GetPositionTransform(string posId)
    {
        if (posId.StartsWith("sp"))
        {
            int idx = int.Parse(posId.Substring(2)) - 1; // sp1→0, sp4→3
            if (idx >= 0 && idx < spawnPositions.Count)
                return spawnPositions[idx].transform;
        }
        else if (posId.StartsWith("cp"))
        {
            int idx = int.Parse(posId.Substring(2)) - 1; // cp1→0, cp5→4
            if (idx >= 0 && idx < colorPositions.Count)
                return colorPositions[idx].transform;
        }
        else if (posId == "ep" || posId.StartsWith("ep"))
        {
            return endPosition.transform;
        }
        return null;
    }
}
