namespace Muffin
{
    /// <summary>
    /// Which face points are which, for each tracker: the eye and lip outlines the real-face cut-outs follow,
    /// the eye corners (tilt, size, centre), the brows, the mouth's corners and middle, the nose. "Left" is
    /// the eye on the left of the camera picture (not mirrored): your right eye.
    /// </summary>
    public sealed class FaceLayout
    {
        public string Name;
        public int[] LeftEye, RightEye, Mouth;
        public int OuterLeft, InnerLeft, InnerRight, OuterRight;
        public int[] LeftBrow, RightBrow;
        public int MouthLeft, MouthRight, LipTop, LipBottom;
        /// <summary>Averaged for the nose-based turn and nod reading (OpenSeeFace); a point may repeat to weigh more.</summary>
        public int[] Nose;
        /// <summary>
        /// How much room the eyeball and lip cut-outs get around the outline: sideways and up/down scale, extra
        /// lid room (in eye widths) so a shut eye keeps a lash line, and the lips' scale. Coarse outlines (6
        /// points an eye) need more room than fine ones (16).
        /// </summary>
        public float EyeExpandX, EyeExpandY, UpperLidPad, LowerLidPad, MouthExpand;

        /// <summary>OpenSeeFace's 66 face landmarks plus two pupils; its mouth differs from iBUG-68.</summary>
        public static readonly FaceLayout OpenSeeFace = new FaceLayout
        {
            Name = "OpenSeeFace",
            LeftEye = new[] { 36, 37, 38, 39, 40, 41 },
            RightEye = new[] { 42, 43, 44, 45, 46, 47 },
            // Outer lip corners are 58/62. Points 59..61 and 63..65 trace the INNER lips.
            // Walking 48..59 directly cuts across the mouth and produces a missing triangular wedge.
            Mouth = new[] { 58, 48, 49, 50, 51, 52, 62, 53, 54, 55, 56, 57 },
            OuterLeft = 36, InnerLeft = 39, InnerRight = 42, OuterRight = 45,
            LeftBrow = new[] { 17, 18, 19, 20, 21 },
            RightBrow = new[] { 22, 23, 24, 25, 26 },
            MouthLeft = 58, MouthRight = 62, LipTop = 50, LipBottom = 55,
            Nose = new[] { 30, 30, 31, 32, 33, 34, 35 },
            // Tight feature cut-outs: avoid bringing a ring of skin and a line above the lips along.
            EyeExpandX = 1.03f, EyeExpandY = 1.06f, UpperLidPad = 0.025f, LowerLidPad = 0.015f, MouthExpand = 1.01f,
        };

        /// <summary>
        /// MediaPipe Face Landmarker's 478 points. The outlines are the ones proven in Face Object Stream
        /// (16 points an eye, 20 for the lips), with its 1.08 / 1.06 expansion.
        /// </summary>
        public static readonly FaceLayout MediaPipe = new FaceLayout
        {
            Name = "MediaPipe",
            LeftEye = new[] { 33, 7, 163, 144, 145, 153, 154, 155, 133, 173, 157, 158, 159, 160, 161, 246 },
            RightEye = new[] { 362, 382, 381, 380, 374, 373, 390, 249, 263, 466, 388, 387, 386, 385, 384, 398 },
            Mouth = new[] { 61, 146, 91, 181, 84, 17, 314, 405, 321, 375, 291, 409, 270, 269, 267, 0, 37, 39, 40, 185 },
            OuterLeft = 33, InnerLeft = 133, InnerRight = 362, OuterRight = 263,
            LeftBrow = new[] { 70, 63, 105, 66, 107 },
            RightBrow = new[] { 336, 296, 334, 293, 300 },
            MouthLeft = 61, MouthRight = 291, LipTop = 0, LipBottom = 17,
            Nose = new[] { 1 },
            EyeExpandX = 1.08f, EyeExpandY = 1.08f, UpperLidPad = 0.03f, LowerLidPad = 0.02f, MouthExpand = 1.06f,
        };
    }
}
