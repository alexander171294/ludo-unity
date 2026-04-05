using UnityEngine;
using UnityEngine.UI;

public class Chip : MonoBehaviour
{
    public Button chipButton;

    [HideInInspector] public int pieceId;
    [HideInInspector] public int colorIndex;

    public GameObject activeIndicator; // default inactive

    GameController _controller;

    public void Setup(int pieceId, int colorIndex, GameController controller)
    {
        this.pieceId = pieceId;
        this.colorIndex = colorIndex;
        _controller = controller;
    }

    public void MoveTo(Transform target, Vector3 stackOffset = default)
    {
        if (target == null) return;
        // Hijo del PlayerController + worldPosition en otro root ⇒ conversión escala/padre puede
        // inflar localPosition (p. ej. Y ~2000). Casilla como padre + offset local = apilado estable.
        transform.SetParent(target, false);
        transform.localPosition = stackOffset;
        transform.localRotation = Quaternion.identity;
    }

    public void SetClickable(bool clickable)
    {
        chipButton.onClick.RemoveAllListeners();
        if (clickable)
            chipButton.onClick.AddListener(() => _controller.OnChipClicked(pieceId));
        // Don't toggle interactable — it changes the button's color tint (makes it transparent)
        // Instead, just control whether click listeners are attached
    }

    public void SetIndicatorActive(bool active)
    {
        if (activeIndicator != null)
            activeIndicator.SetActive(active);
    }
}
