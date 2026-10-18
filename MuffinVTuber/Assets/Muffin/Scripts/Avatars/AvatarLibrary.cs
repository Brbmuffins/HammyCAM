using System.Collections.Generic;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// The avatars you can switch between (Down / Up arrows). Exactly one is active; switching keeps the
    /// current input and emotion. Each avatar builds itself the first time it's shown.
    /// </summary>
    public sealed class AvatarLibrary : MonoBehaviour
    {
        public List<BendRig> avatars = new List<BendRig>();
        public HeadPoseSource source;

        public int ActiveIndex { get; private set; } = -1;
        public BendRig ActiveRig => ActiveIndex >= 0 ? avatars[ActiveIndex] : null;
        public Emotion Emotion { get; private set; }

        public string ActiveName
        {
            get
            {
                if (!ActiveRig) return "none";
                if (ActiveRig.TryGetComponent(out MeshAvatar m)) return m.displayName;
                return ActiveRig.name;
            }
        }

        void Awake()
        {
            DigitalFace.Style = (FaceStyle)PlayerPrefs.GetInt("muffin.facestyle", (int)FaceStyle.Cartoon);
            // Saved sizes (older saves kept a preset number instead).
            DigitalFace.EyeLevel = PlayerPrefs.GetInt("muffin.eyezoom", 0);
            DigitalFace.MouthLevel = PlayerPrefs.GetInt("muffin.mouthzoom", 0);
            DigitalFace.EyeSize = Mathf.Clamp(PlayerPrefs.GetFloat("muffin.eyesize", DigitalFace.EyeSize), DigitalFace.MinSize, DigitalFace.MaxSize);
            DigitalFace.MouthSize = Mathf.Clamp(PlayerPrefs.GetFloat("muffin.mouthsize", DigitalFace.MouthSize), DigitalFace.MinSize, DigitalFace.MaxSize);
            DigitalFace.EdgeLevel = Mathf.Clamp(PlayerPrefs.GetInt("muffin.edges", 0), 0, DigitalFace.EdgeNames.Length - 1);
            DigitalFace.CutBrightness = Mathf.Clamp(PlayerPrefs.GetFloat("muffin.cut.brightness", 0f), -0.5f, 0.5f);
            DigitalFace.CutContrast = Mathf.Clamp(PlayerPrefs.GetFloat("muffin.cut.contrast", 1f), 0.5f, 2f);
            DigitalFace.CutSaturation = Mathf.Clamp(PlayerPrefs.GetFloat("muffin.cut.saturation", 1f), 0f, 2f);
            DigitalFace.Crop = (DigitalFace.RealCrop)Mathf.Clamp(PlayerPrefs.GetInt("muffin.realcrop", 0), 0, DigitalFace.CropNames.Length - 1);
            int start = Mathf.Clamp(PlayerPrefs.GetInt("muffin.avatar", 0), 0, Mathf.Max(0, avatars.Count - 1));
            for (int i = 0; i < avatars.Count; i++) avatars[i].gameObject.SetActive(false);
            Activate(start);
        }

        void Update()
        {
            // Down arrow: next avatar, Up arrow: previous; both wrap around.
            if (avatars.Count > 1 && Keys.Pressed(MuffinKey.Down)) Activate((ActiveIndex + 1) % avatars.Count);
            if (avatars.Count > 1 && Keys.Pressed(MuffinKey.Up)) Activate((ActiveIndex - 1 + avatars.Count) % avatars.Count);
            if (Keys.Pressed(MuffinKey.F1)) ToggleEmotion(Emotion.Happy);
            if (Keys.Pressed(MuffinKey.F2)) ToggleEmotion(Emotion.Sad);
            if (Keys.Pressed(MuffinKey.F3)) ToggleEmotion(Emotion.Angry);
            if (Keys.Pressed(MuffinKey.F4)) ToggleEmotion(Emotion.Surprised);
            if (Keys.Pressed(MuffinKey.V))
            {
                DigitalFace.Style = DigitalFace.Style == FaceStyle.Real ? FaceStyle.Cartoon : FaceStyle.Real;
                PlayerPrefs.SetInt("muffin.facestyle", (int)DigitalFace.Style);
            }
            // Z: eye zoom, O: mouth zoom, each normal → big → huge → giant. S: real-face edges soft → sharp →
            // outlined. E: how much of your face shows (just eyeballs and lips, eyes and mouth, eyes with brows).
            if (Keys.Pressed(MuffinKey.Z))
            {
                SetEyeZoom(NextPreset(DigitalFace.EyeSize));
            }
            if (Keys.Pressed(MuffinKey.O))
            {
                SetMouthZoom(NextPreset(DigitalFace.MouthSize));
            }
            if (Keys.Pressed(MuffinKey.S))
            {
                DigitalFace.EdgeLevel = (DigitalFace.EdgeLevel + 1) % DigitalFace.EdgeNames.Length;
                PlayerPrefs.SetInt("muffin.edges", DigitalFace.EdgeLevel);
            }
            if (Keys.Pressed(MuffinKey.E))
            {
                DigitalFace.Crop = (DigitalFace.RealCrop)(((int)DigitalFace.Crop + 1) % DigitalFace.CropNames.Length);
                PlayerPrefs.SetInt("muffin.realcrop", (int)DigitalFace.Crop);
            }
        }

        public void Activate(int index)
        {
            if (index < 0 || index >= avatars.Count || index == ActiveIndex) return;
            bool filter = ActiveRig ? ActiveRig.filterInput : true;
            if (ActiveRig) ActiveRig.gameObject.SetActive(false);
            ActiveIndex = index;
            BendRig rig = avatars[index];
            rig.source = source;
            rig.filterInput = filter;
            rig.gameObject.SetActive(true);
            if (rig.TryGetComponent(out DigitalFace face)) face.SetEmotion(Emotion);
            PlayerPrefs.SetInt("muffin.avatar", index);
        }

        /// <summary>The display name of avatar i (for the control panel).</summary>
        public string NameOf(int i)
        {
            if (i < 0 || i >= avatars.Count || !avatars[i]) return "-";
            return avatars[i].TryGetComponent(out MeshAvatar m) ? m.displayName : avatars[i].name;
        }

        // The face settings, as the keys set them, for the control panel (each remembered).
        public void SetFaceStyle(FaceStyle style)
        {
            DigitalFace.Style = style;
            PlayerPrefs.SetInt("muffin.facestyle", (int)style);
        }

        public void SetCrop(DigitalFace.RealCrop crop)
        {
            DigitalFace.Crop = crop;
            PlayerPrefs.SetInt("muffin.realcrop", (int)crop);
        }

        /// <summary>The next preset size up from this one (round to normal after giant).</summary>
        static int NextPreset(float size)
        {
            for (int i = 0; i < DigitalFace.Sizes.Length; i++)
                if (DigitalFace.Sizes[i] > size + 0.01f) return i;
            return 0;
        }

        public void SetEyeZoom(int level) => SetEyeSize(DigitalFace.Sizes[Mathf.Clamp(level, 0, DigitalFace.Sizes.Length - 1)]);
        public void SetMouthZoom(int level) => SetMouthSize(DigitalFace.Sizes[Mathf.Clamp(level, 0, DigitalFace.Sizes.Length - 1)]);

        /// <summary>Any eye size (× normal), remembered.</summary>
        public void SetEyeSize(float size)
        {
            DigitalFace.EyeSize = Mathf.Clamp(size, DigitalFace.MinSize, DigitalFace.MaxSize);
            PlayerPrefs.SetFloat("muffin.eyesize", DigitalFace.EyeSize);
        }

        public void SetMouthSize(float size)
        {
            DigitalFace.MouthSize = Mathf.Clamp(size, DigitalFace.MinSize, DigitalFace.MaxSize);
            PlayerPrefs.SetFloat("muffin.mouthsize", DigitalFace.MouthSize);
        }

        /// <summary>How your real eyes and mouth look (the cut-outs only), remembered.</summary>
        public void SetCutoutLook(float brightness, float contrast, float saturation)
        {
            DigitalFace.CutBrightness = Mathf.Clamp(brightness, -0.5f, 0.5f);
            DigitalFace.CutContrast = Mathf.Clamp(contrast, 0.5f, 2f);
            DigitalFace.CutSaturation = Mathf.Clamp(saturation, 0f, 2f);
            PlayerPrefs.SetFloat("muffin.cut.brightness", DigitalFace.CutBrightness);
            PlayerPrefs.SetFloat("muffin.cut.contrast", DigitalFace.CutContrast);
            PlayerPrefs.SetFloat("muffin.cut.saturation", DigitalFace.CutSaturation);
        }

        public void SetEdges(int level)
        {
            DigitalFace.EdgeLevel = Mathf.Clamp(level, 0, DigitalFace.EdgeNames.Length - 1);
            PlayerPrefs.SetInt("muffin.edges", DigitalFace.EdgeLevel);
        }

        public void SetSource(HeadPoseSource s)
        {
            source = s;
            foreach (BendRig rig in avatars) rig.source = s;
        }

        /// <summary>Press an emotion key again to go back to neutral.</summary>
        public void ToggleEmotion(Emotion e) => SetEmotion(Emotion == e ? Emotion.Neutral : e);

        public void SetEmotion(Emotion e)
        {
            Emotion = e;
            foreach (BendRig rig in avatars)
                if (rig.TryGetComponent(out DigitalFace face)) face.SetEmotion(e);
        }
    }
}
