using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class PlayerController : MonoBehaviour
{
    public PlayerColor playerColor;
    public List<GameObject> spawnPositions; // 0-3 (sp1, sp2, sp3, sp4)
    public List<GameObject> colorPositions; // 0-4 (cp1, cp2, cp3, cp4, cp5)
    public GameObject endPosition;
    public Dice dice;
    public TextMeshProUGUI nickText;
    public Image playerTimmer; // configured fill horizontal from 1 to 0 (no time left)
    public GameObject chipPrefab; // have Chip.cs Script attached to it
    public GameObject UI_playerCard; // this is visual indicator of the player in the game, disabled by default, must be enabled when the player is in this position
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
