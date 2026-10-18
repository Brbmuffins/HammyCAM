using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// One Euro filter (Casiez, Roussel and Vogel, 2012). It smooths hard while the input moves slowly,
    /// which removes tracker jitter when you hold still, and barely at all while it moves fast, so
    /// there is no lag when you actually move. minCutoff trades jitter at rest; beta trades lag in motion.
    /// </summary>
    public static class OneEuro
    {
        public static float Alpha(float cutoff, float dt)
        {
            float tau = 1f / (2f * Mathf.PI * cutoff);
            return 1f / (1f + tau / dt);
        }
    }

    public sealed class OneEuroVector3
    {
        public float MinCutoff, Beta, DerivativeCutoff;
        Vector3 value, speed;
        bool primed;

        public OneEuroVector3(float minCutoff, float beta, float derivativeCutoff = 1f)
        {
            MinCutoff = minCutoff;
            Beta = beta;
            DerivativeCutoff = derivativeCutoff;
        }

        public Vector3 Filter(Vector3 x, float dt)
        {
            if (!primed)
            {
                value = x;
                speed = Vector3.zero;
                primed = true;
                return x;
            }
            speed = Vector3.Lerp(speed, (x - value) / dt, OneEuro.Alpha(DerivativeCutoff, dt));
            value = Vector3.Lerp(value, x, OneEuro.Alpha(MinCutoff + Beta * speed.magnitude, dt));
            return value;
        }

        public void Reset() => primed = false;
    }

    /// <summary>The same filter on rotations; speed is angular speed in radians per second.</summary>
    public sealed class OneEuroRotation
    {
        public float MinCutoff, Beta, DerivativeCutoff;
        Quaternion value;
        float speed;
        bool primed;

        public OneEuroRotation(float minCutoff, float beta, float derivativeCutoff = 1f)
        {
            MinCutoff = minCutoff;
            Beta = beta;
            DerivativeCutoff = derivativeCutoff;
        }

        public Quaternion Filter(Quaternion x, float dt)
        {
            if (!primed)
            {
                value = x;
                speed = 0f;
                primed = true;
                return x;
            }
            float rawSpeed = Quaternion.Angle(value, x) * Mathf.Deg2Rad / dt;
            speed = Mathf.Lerp(speed, rawSpeed, OneEuro.Alpha(DerivativeCutoff, dt));
            value = Quaternion.Slerp(value, x, OneEuro.Alpha(MinCutoff + Beta * speed, dt));
            return value;
        }

        public void Reset() => primed = false;
    }
}
