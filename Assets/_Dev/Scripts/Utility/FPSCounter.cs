using TMPro;
using UnityEngine;
public class FPSCounter : MonoBehaviour
{
    [SerializeField] private TMP_Text fpsText;

    private float timer;
    private int frames;

    void Update()
    {
        frames++;
        timer += Time.unscaledDeltaTime;

        if (timer >= 0.5f) // Update twice per second
        {
            float fps = frames / timer;
            fpsText.text = $"FPS: {fps:F0}";

            frames = 0;
            timer = 0f;
        }
    }
}