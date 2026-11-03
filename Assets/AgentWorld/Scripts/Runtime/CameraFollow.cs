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
            var goal = new Vector3(target.position.x, target.position.y, transform.position.z);
            transform.position = Vector3.SmoothDamp(transform.position, goal, ref velocity, smoothTime);
        }
    }
}
