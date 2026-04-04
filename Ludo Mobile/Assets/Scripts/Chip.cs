using UnityEngine;
using UnityEngine.UI;

public class Chip : MonoBehaviour
{
    public Button chipButton;

    [HideInInspector] public int pieceId;
    [HideInInspector] public int colorIndex;

    GameController _controller;

    public void Setup(int pieceId, int colorIndex, GameController controller)
    {
        this.pieceId = pieceId;
        this.colorIndex = colorIndex;
        _controller = controller;
    }

    public void MoveTo(Transform target)
    {
        transform.position = target.position;
    }

    public void SetClickable(bool clickable)
    {
        chipButton.onClick.RemoveAllListeners();
        if (clickable)
            chipButton.onClick.AddListener(() => _controller.OnChipClicked(pieceId));
        chipButton.interactable = clickable;
    }
}
