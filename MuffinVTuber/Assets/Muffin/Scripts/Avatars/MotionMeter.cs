using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// How much part of an avatar is moving, for the mess it sheds: the speed of a point on its rim, so turning
    /// on the spot counts as much as moving about (with head-turn control the avatar mostly turns, and the
    /// middle of its top hardly moves), and its shake (acceleration). Both are smoothed a little.
    /// </summary>
    public sealed class MotionMeter
    {
        Vector3 lastPosition, lastVelocity;
        Quaternion lastRotation;
        bool primed;

        /// <summary>How fast the part itself is moving (m/s), unsmoothed: things that come off carry it.</summary>
        public Vector3 Velocity { get; private set; }
        /// <summary>Speed at the rim, moving plus turning (m/s).</summary>
        public float Speed { get; private set; }
        /// <summary>Acceleration (m/s²).</summary>
        public float Shake { get; private set; }

        /// <summary>Start again (switching back to the avatar): no jump from where it was left.</summary>
        public void Reset()
        {
            primed = false;
            Speed = Shake = 0f;
            Velocity = Vector3.zero;
        }

        public void Update(Transform part, float rimRadius, float dt)
        {
            Vector3 position = part.position;
            Quaternion rotation = part.rotation;
            if (!primed)
            {
                lastPosition = position;
                lastRotation = rotation;
                lastVelocity = Vector3.zero;
                primed = true;
                return;
            }
            Vector3 velocity = (position - lastPosition) / dt;
            float spin = Quaternion.Angle(lastRotation, rotation) * Mathf.Deg2Rad / dt;
            Vector3 acceleration = (velocity - lastVelocity) / dt;
            lastPosition = position;
            lastRotation = rotation;
            lastVelocity = velocity;
            Velocity = velocity;
            Speed = Mathf.Lerp(Speed, Mathf.Min(velocity.magnitude + spin * rimRadius, 10f), 1f - Mathf.Exp(-dt * 8f));
            Shake = Mathf.Lerp(Shake, Mathf.Min(acceleration.magnitude, 200f), 1f - Mathf.Exp(-dt * 10f));
        }
    }
}
