using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class AnimateAvatarOnEnable : MonoBehaviour
{
    public List<Texture2D> frames = new();
    [Header("Events")]
    public UnityEvent<Texture2D> onFrameChange;

    private Coroutine _coroutine = null;

    private void OnEnable()
    {
        if (_coroutine != null)
            StopCoroutine(_coroutine);
        _coroutine = StartCoroutine(Animate());
    }

    private IEnumerator Animate()
    {
        for (int i = 0; i < frames.Count; i++)
        {
            onFrameChange.Invoke(frames[i]);
            yield return new WaitForSecondsRealtime(1f / 60f);
        }
    }

    private void OnDisable()
    {
        if (_coroutine != null)
            StopCoroutine(_coroutine);
        _coroutine = null;
    }
}
