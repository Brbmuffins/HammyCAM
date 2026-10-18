using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// One tracking sample, relative to the streamer's rest pose. Offsets are normalized: about ±1 is a
    /// comfortable full lean, so the same numbers drive a 30 cm sunflower or a 2 m steak.
    /// </summary>
    public struct HeadPose
    {
        /// <summary>x = screen right, y = up (stretch) / down (scrunch), z = toward the camera.</summary>
        public Vector3 Offset;
        /// <summary>Head rotation relative to looking straight at the camera.</summary>
        public Quaternion Rotation;
        /// <summary>0 closed .. 1 wide open.</summary>
        public float MouthOpen;
        /// <summary>0 open .. 1 closed.</summary>
        public float BlinkLeft, BlinkRight;
        /// <summary>-1 frown .. +1 smile (mouth corners), relative to your neutral face.</summary>
        public float Smile;
        /// <summary>-1 brows lowered .. +1 brows raised.</summary>
        public float BrowRaise;
        /// <summary>-1 puckered ("ooh") .. +1 stretched wide ("ee").</summary>
        public float MouthWide;
        /// <summary>One-sided smile: -1 the screen-left corner up .. +1 the screen-right corner up.</summary>
        public float Smirk;

        public static HeadPose Rest => new HeadPose { Rotation = Quaternion.identity };
    }

    /// <summary>Anything that supplies head poses: the mouse (development), a face tracker, a VMC stream.</summary>
    public abstract class HeadPoseSource : MonoBehaviour
    {
        public abstract string Label { get; }

        /// <summary>True when the source already smooths its own noise (the avatar then only evens out its steps).</summary>
        public virtual bool PreFiltered => false;

        /// <summary>False when nothing is tracked (face lost): the avatar eases back to rest.</summary>
        public abstract bool TryGetPose(out HeadPose pose);
    }
}
