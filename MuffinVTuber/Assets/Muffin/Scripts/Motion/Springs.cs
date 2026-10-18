using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// Second-order spring follower: what makes motion feel fluid instead of rigid. It chases its
    /// target with natural frequency f (Hz, speed), damping z (below 1 overshoots and wobbles, 1 settles
    /// without overshoot) and response r (1 reacts immediately, above 1 overshoots at the start, below 0
    /// anticipates). Stable at any frame time.
    /// </summary>
    public sealed class SpringVector3
    {
        float k1, k2, k3;
        Vector3 previousTarget, y, velocity;

        public SpringVector3(float frequency, float damping, float response, Vector3 start)
        {
            SetParameters(frequency, damping, response);
            previousTarget = y = start;
            velocity = Vector3.zero;
        }

        public Vector3 Value => y;

        public void SetParameters(float frequency, float damping, float response)
        {
            float w = 2f * Mathf.PI * Mathf.Max(0.01f, frequency);
            k1 = damping / (Mathf.PI * Mathf.Max(0.01f, frequency));
            k2 = 1f / (w * w);
            k3 = response * damping / w;
        }

        public Vector3 Update(Vector3 target, float dt)
        {
            Vector3 targetVelocity = (target - previousTarget) / dt;
            previousTarget = target;
            float k2Stable = Mathf.Max(k2, dt * dt / 2f + dt * k1 / 2f, dt * k1);
            y += dt * velocity;
            velocity += dt * (target + k3 * targetVelocity - y - k1 * velocity) / k2Stable;
            return y;
        }
    }

    public sealed class SpringFloat
    {
        readonly SpringVector3 inner;

        public SpringFloat(float frequency, float damping, float response, float start) =>
            inner = new SpringVector3(frequency, damping, response, new Vector3(start, 0f, 0f));

        public float Value => inner.Value.x;

        public void SetParameters(float frequency, float damping, float response) =>
            inner.SetParameters(frequency, damping, response);

        public float Update(float target, float dt) => inner.Update(new Vector3(target, 0f, 0f), dt).x;
    }

    public static class RotationMath
    {
        /// <summary>Rotation as axis × angle (radians): lets springs act on rotations below 180°.</summary>
        public static Vector3 ToVector(Quaternion q)
        {
            q.ToAngleAxis(out float degrees, out Vector3 axis);
            if (degrees > 180f) degrees -= 360f;
            if (Mathf.Abs(degrees) < 1e-4f || float.IsNaN(axis.x)) return Vector3.zero;
            return axis.normalized * (degrees * Mathf.Deg2Rad);
        }

        public static Quaternion FromVector(Vector3 v)
        {
            float radians = v.magnitude;
            return radians < 1e-6f ? Quaternion.identity : Quaternion.AngleAxis(radians * Mathf.Rad2Deg, v / radians);
        }
    }
}
