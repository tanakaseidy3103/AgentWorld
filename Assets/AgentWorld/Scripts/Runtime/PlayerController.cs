using UnityEngine;

namespace AgentWorld
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerController : MonoBehaviour
    {
        public float speed = 4f;
        public bool useKeyboard = true;
        public Vector2 MoveInput { get; set; }

        Rigidbody2D rb;

        void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
        }

        void Update()
        {
            if (!useKeyboard) return;
            if (TerminalUI.IsOpen) { MoveInput = Vector2.zero; return; }
            var x = (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0);
            var y = (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0);
            MoveInput = new Vector2(x, y).normalized;
        }

        void FixedUpdate() => rb.linearVelocity = MoveInput * speed;

        public void Teleport(Vector2 position)
        {
            rb.position = position;
            transform.position = position;
        }
    }
}
