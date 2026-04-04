using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

public class Dice : MonoBehaviour
{
    public bool visible = true;
    public List<Image> faces;
    public float rollSpeed = 1f;

    Coroutine _rollRoutine;

    void Start()
    {
        if (faces != null && faces.Count > 0)
            SetActiveFace(0);
    }

    /** value: cara 1..N (índice + 1). rounds: pasos de cambio; el último es siempre value. */
    public void RollTo(int value, int rounds)
    {
        if (_rollRoutine != null)
        {
            StopCoroutine(_rollRoutine);
            _rollRoutine = null;
        }

        if (faces == null || faces.Count == 0)
            return;

        int targetIndex = Mathf.Clamp(value, 1, faces.Count) - 1;
        rounds = Mathf.Max(0, rounds);

        if (rounds == 0)
        {
            SetActiveFace(targetIndex);
            return;
        }

        _rollRoutine = StartCoroutine(RollRoutine(targetIndex, rounds));
    }

    IEnumerator RollRoutine(int targetIndex, int rounds)
    {
        float stepDuration = rollSpeed > 0f ? 1f / rollSpeed : 0.05f;
        int lastIndex = GetActiveFaceIndex();

        for (int i = 0; i < rounds; i++)
        {
            int next;
            if (i == rounds - 1)
                next = targetIndex;
            else
            {
                if (faces.Count <= 1)
                    next = 0;
                else
                {
                    do
                        next = Random.Range(0, faces.Count);
                    while (next == lastIndex);
                }
            }

            SetActiveFace(next);
            lastIndex = next;
            yield return new WaitForSeconds(stepDuration);
        }

        _rollRoutine = null;
    }

    int GetActiveFaceIndex()
    {
        for (int i = 0; i < faces.Count; i++)
        {
            if (faces[i] != null && faces[i].gameObject.activeSelf)
                return i;
        }
        return 0;
    }

    void SetActiveFace(int index)
    {
        index = Mathf.Clamp(index, 0, faces.Count - 1);
        for (int i = 0; i < faces.Count; i++)
        {
            if (faces[i] == null)
                continue;
            faces[i].gameObject.SetActive(i == index);
        }
    }
}
