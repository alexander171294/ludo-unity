using UnityEngine;
using UnityEngine.UI;
using TMPro;
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

    public GameObject step_initialMenu; // default enabled
    public GameObject step_pickColorAndNickname; // default disabled
    public GameObject step_lobbyWaitingPlayers; // default disabled

    // Initial menu variables //
    public Button JoinButton;
    public GameObject JoinButtonDisabled; // default enabled, waiting for room code to be filled
    public TMP_InputField RoomCodeInput;
    public Button createRoomButton;

    // Pick color and nickname variables //
    // Listas indexadas como PlayerColor (0 = Red … 3 = Yellow)
    public List<Button> colorButtons;
    public List<Image> colorButtonsTaken;
    public List<Image> colorButtonsSelectedIndicator;


    public TMP_InputField nicknameInput;
    public Button playButton;
    public GameObject playButtonDisabled; // default enabled, waiting for nickname and color to be selected

    // Lobby waiting players variables //
    // default disabled, waiting for players to join the lobby
    // colorsIndicator indexado como PlayerColor
    public List<GameObject> colorsIndicator; // used to indicate the colors of the players in the lobby, have inside a TextMeshPro child gameobject called nick
    public Button startGameButton; // default disabled, waiting at least 2 players to be ready
    public GameObject startGameButtonDisabled; // default enabled, waiting at least 2 players to be ready
    public TextMeshProUGUI codeToShareText;
    public Button copyCodeToShareButton;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    // example function to set the nick to a player indicator gameobject
    void SetNickToPlayerIndicatorGameObject(GameObject playerIndicator, string nick)
    {
        playerIndicator.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = nick;
    }
}
