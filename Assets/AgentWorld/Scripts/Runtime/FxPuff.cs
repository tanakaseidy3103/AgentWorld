using UnityEngine;

namespace AgentWorld
{
    /// Construction dust: drifts up, grows and fades, then removes itself.
    public class FxPuff : MonoBehaviour
    {
        public float life = 0.7f;
        float age;
        SpriteRenderer sr;
        Vector3 drift;

        public static void Spawn(Vector2 at)
        {
            var go = new GameObject("Dust");
            go.transform.position = at;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = RuntimeArt.Dust();
            sr.sortingOrder = 40;
            go.AddComponent<FxPuff>();
        }

        void Start()
        {
            sr = GetComponent<SpriteRenderer>();
            drift = new Vector3(Random.Range(-0.4f, 0.4f), Random.Range(0.4f, 0.9f), 0);
        }

        void Update()
        {
            age += Time.deltaTime;
            var k = age / life;
            transform.position += drift * Time.deltaTime;
            transform.localScale = Vector3.one * (1f + k);
            sr.color = new Color(1, 1, 1, 1f - k);
            if (k >= 1f) Destroy(gameObject);
        }
    }
}
