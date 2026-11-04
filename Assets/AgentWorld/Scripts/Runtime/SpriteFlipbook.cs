using UnityEngine;

namespace AgentWorld
{
    /// Cycles a SpriteRenderer through frames (lake shimmer).
    [RequireComponent(typeof(SpriteRenderer))]
    public class SpriteFlipbook : MonoBehaviour
    {
        public Sprite[] frames;
        public float fps = 2.5f;
        SpriteRenderer sr;

        void Awake() => sr = GetComponent<SpriteRenderer>();

        void Update()
        {
            if (frames == null || frames.Length == 0) return;
            sr.sprite = frames[(int)(Time.time * fps) % frames.Length];
        }
    }
}
