using UnityEngine;

namespace AgentWorld
{
    public class CameraFollow : MonoBehaviour
    {
        public Transform target;
        public float smoothTime = 0.12f;
        Vector3 velocity;

        void LateUpdate()
        {
            if (target == null) return;
            // Center the target in the part of the screen not covered by Mission Control (docked right).
            var cam = GetComponent<Camera>();
            var shift = cam && cam.orthographic && Screen.height > 0 ? MissionPanel.ReservedWidth / 2f * (2f * cam.orthographicSize / Screen.height) : 0f;
            var goal = new Vector3(target.position.x + shift, target.position.y, transform.position.z);
            transform.position = Vector3.SmoothDamp(transform.position, goal, ref velocity, smoothTime);
        }
    }
}
