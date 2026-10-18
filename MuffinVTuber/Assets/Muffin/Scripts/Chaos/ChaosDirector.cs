using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Muffin
{
    /// <summary>Everything that can be done to the set, by viewers (chat commands) or the streamer (panel, keys).</summary>
    public enum ChaosEvent { Sprinklers, Fire, Slime, Pie, Fruit, WhippedCream, WaterSpill, Shake, Slam }

    /// <summary>Something an avatar can spill on cue (!shake): its pepperoni, sprinkles, garbage, juice…</summary>
    public interface IMessy
    {
        /// <summary>A short name for what it spills ("pepperoni", "sprinkles").</summary>
        string MessName { get; }
        /// <summary>Spill a good amount, now.</summary>
        void Burst();
    }

    /// <summary>
    /// The chaos set: viewers type commands in Twitch chat (read anonymously, no login) to make the desk
    /// messier, within cooldowns (per event, per viewer, and a gap between any two), under a master switch.
    /// The streamer fires the same events from the control panel. A caption says who did it.
    /// </summary>
    public sealed partial class ChaosDirector : MonoBehaviour
    {
        public static ChaosDirector Instance { get; private set; }

        [Header("Scene")]
        public AvatarLibrary library;
        public DeskScene desk;
        public DeskDebris debris;
        public Mess mess;
        public PerfHud hud;
        public Camera cam;

        [Header("Materials")]
        public Material flame, flameYellow, smoke, bubble, rain, glass, water, pieTin, cream, tomato, leaf, slime, canBody, scorch;

        /// <summary>One event's settings and chat commands.</summary>
        public sealed class EventInfo
        {
            public ChaosEvent Event;
            public string Label, Caption;
            public string[] Commands;
            public float Cooldown;
            public bool Enabled = true;
            public float LastFired = -9999f;
        }

        public readonly EventInfo[] Events =
        {
            new EventInfo { Event = ChaosEvent.Sprinklers, Label = "Sprinklers", Caption = "set off the sprinklers!", Commands = new[] { "!sprinkler", "!sprinklers", "!rain" }, Cooldown = 90f },
            new EventInfo { Event = ChaosEvent.Fire, Label = "Fire", Caption = "started a fire!", Commands = new[] { "!fire" }, Cooldown = 90f },
            new EventInfo { Event = ChaosEvent.Slime, Label = "Sneeze", Caption = "sneezed on the camera!", Commands = new[] { "!sneeze", "!achoo", "!slime" }, Cooldown = 60f },
            new EventInfo { Event = ChaosEvent.Pie, Label = "Pie to the face", Caption = "threw a pie!", Commands = new[] { "!pie" }, Cooldown = 30f },
            new EventInfo { Event = ChaosEvent.Fruit, Label = "Tomatoes", Caption = "threw tomatoes!", Commands = new[] { "!fruit", "!tomato", "!tomatoes" }, Cooldown = 20f },
            new EventInfo { Event = ChaosEvent.WhippedCream, Label = "Whipped cream", Caption = "sprayed whipped cream!", Commands = new[] { "!cream", "!whip", "!whippedcream" }, Cooldown = 30f },
            new EventInfo { Event = ChaosEvent.WaterSpill, Label = "Water spill", Caption = "knocked over the water!", Commands = new[] { "!water", "!spill" }, Cooldown = 60f },
            new EventInfo { Event = ChaosEvent.Shake, Label = "Shake (avatar's own mess)", Caption = "shook things up!", Commands = new[] { "!shake" }, Cooldown = 20f },
            new EventInfo { Event = ChaosEvent.Slam, Label = "Face slam", Caption = "made it faceplant!", Commands = new[] { "!slam", "!faceplant" }, Cooldown = 30f },
        };

        [Header("Rules")]
        public bool chaosOn = true;
        [Tooltip("Seconds between any two viewer events.")]
        public float gap = 5f;
        [Tooltip("Seconds before the same viewer can trigger again.")]
        public float perViewer = 30f;
        public bool showCaptions = true;
        public int maxAtOnce = 3;
        public string channel = "";

        public TwitchChat Chat { get; } = new TwitchChat();
        public int Running { get; private set; }
        public string LastViewerEvent { get; private set; } = "";

        readonly Dictionary<string, float> viewerLast = new Dictionary<string, float>();
        float lastAny = -9999f;
        string caption;
        float captionUntil;
        GUIStyle captionStyle, captionShadow;
        Mesh capsule, sphere, cylinder;

        void Awake()
        {
            Instance = this;
            capsule = Mess.PrimitiveMesh(PrimitiveType.Capsule);
            sphere = Mess.PrimitiveMesh(PrimitiveType.Sphere);
            cylinder = Mess.PrimitiveMesh(PrimitiveType.Cylinder);
            Load();
        }

        void Start()
        {
            if (channel.Length > 0 && !HasArg("-muffin-selftest")) Chat.Connect(channel);
        }

        void OnDestroy()
        {
            Chat.Dispose();
            if (Instance == this) Instance = null;
        }

        static bool HasArg(string a) => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), a) >= 0;

        void Load()
        {
            chaosOn = PlayerPrefs.GetInt("muffin.chaos.on", 1) == 1;
            gap = PlayerPrefs.GetFloat("muffin.chaos.gap", gap);
            perViewer = PlayerPrefs.GetFloat("muffin.chaos.viewer", perViewer);
            showCaptions = PlayerPrefs.GetInt("muffin.chaos.captions", 1) == 1;
            channel = PlayerPrefs.GetString("muffin.chaos.channel", "");
            foreach (EventInfo e in Events)
            {
                e.Enabled = PlayerPrefs.GetInt($"muffin.chaos.{e.Event}.on", 1) == 1;
                e.Cooldown = PlayerPrefs.GetFloat($"muffin.chaos.{e.Event}.cooldown", e.Cooldown);
            }
        }

        /// <summary>Remember the settings (the panel calls this when anything changes).</summary>
        public void Save()
        {
            PlayerPrefs.SetInt("muffin.chaos.on", chaosOn ? 1 : 0);
            PlayerPrefs.SetFloat("muffin.chaos.gap", gap);
            PlayerPrefs.SetFloat("muffin.chaos.viewer", perViewer);
            PlayerPrefs.SetInt("muffin.chaos.captions", showCaptions ? 1 : 0);
            PlayerPrefs.SetString("muffin.chaos.channel", channel);
            foreach (EventInfo e in Events)
            {
                PlayerPrefs.SetInt($"muffin.chaos.{e.Event}.on", e.Enabled ? 1 : 0);
                PlayerPrefs.SetFloat($"muffin.chaos.{e.Event}.cooldown", e.Cooldown);
            }
        }

        public void ConnectChat(string name)
        {
            channel = (name ?? "").Trim().TrimStart('#');
            Save();
            Chat.Connect(channel);
        }

        public EventInfo Info(ChaosEvent e) => Events[(int)e];

        /// <summary>Seconds until this event can be triggered by a viewer again (0 = ready).</summary>
        public float CooldownLeft(ChaosEvent e) => Mathf.Max(0f, Info(e).LastFired + Info(e).Cooldown - Time.time);

        void Update()
        {
            if (Keys.Pressed(MuffinKey.F7))
            {
                chaosOn = !chaosOn;
                Save();
            }
            while (Chat.TryNext(out string user, out string text)) HandleChat(user, text);
        }

        /// <summary>
        /// A chat message: if it starts with an event's command and the rules allow, the event fires.
        /// Returns the event fired, or null.
        /// </summary>
        public ChaosEvent? HandleChat(string user, string text)
        {
            if (!chaosOn || string.IsNullOrEmpty(text) || text[0] != '!') return null;
            string word = text.Split(' ')[0].ToLowerInvariant();
            EventInfo info = null;
            foreach (EventInfo e in Events)
                if (System.Array.IndexOf(e.Commands, word) >= 0) info = e;
            if (info == null || !info.Enabled) return null;
            float now = Time.time;
            string key = (user ?? "").ToLowerInvariant();
            if (now - lastAny < gap) return null;
            if (now < info.LastFired + info.Cooldown) return null;
            if (viewerLast.TryGetValue(key, out float last) && now - last < perViewer) return null;
            if (Running >= maxAtOnce) return null;
            viewerLast[key] = now;
            lastAny = now;
            LastViewerEvent = $"{user}: {info.Label}";
            Fire(info.Event, user);
            return info.Event;
        }

        /// <summary>Fire an event now (the streamer's panel and keys skip the cooldowns; viewers go through HandleChat).</summary>
        public void Fire(ChaosEvent e, string who = null)
        {
            EventInfo info = Info(e);
            info.LastFired = Time.time;
            if (showCaptions && !string.IsNullOrEmpty(who))
            {
                caption = $"{who} {info.Caption}";
                captionUntil = Time.time + 4f;
            }
            StartCoroutine(Run(e));
        }

        IEnumerator Run(ChaosEvent e)
        {
            Sfx.Play("pop", 0.7f, 0.05f); // a viewer did something: a wet suction pop
            Running++;
            IEnumerator routine = e switch
            {
                ChaosEvent.Sprinklers => Sprinklers(),
                ChaosEvent.Fire => FireEvent(),
                ChaosEvent.Slime => Slime(),
                ChaosEvent.Pie => Pie(),
                ChaosEvent.Fruit => Fruit(),
                ChaosEvent.WhippedCream => WhippedCream(),
                ChaosEvent.WaterSpill => WaterSpill(),
                ChaosEvent.Shake => Shake(),
                _ => SlamEvent(),
            };
            yield return routine;
            Running--;
        }

        IEnumerator Shake()
        {
            BendRig rig = library ? library.ActiveRig : null;
            if (rig)
                foreach (IMessy messy in rig.GetComponents<IMessy>()) messy.Burst();
            yield return null;
        }

        IEnumerator SlamEvent()
        {
            if (debris) debris.Slam();
            yield return new WaitForSeconds(1f);
        }

        /// <summary>What !shake spills on the current avatar (for the panel).</summary>
        public string ShakeName
        {
            get
            {
                BendRig rig = library ? library.ActiveRig : null;
                if (!rig) return "nothing";
                var names = new List<string>();
                foreach (IMessy messy in rig.GetComponents<IMessy>()) names.Add(messy.MessName);
                return names.Count > 0 ? string.Join(", ", names) : "nothing";
            }
        }

        void OnGUI()
        {
            // The caption is part of the show (OBS captures it), so it stays when the readout is hidden.
            if (caption == null || Time.time > captionUntil) return;
            captionStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true, normal = { textColor = new Color(1f, 0.92f, 0.3f) } };
            captionShadow ??= new GUIStyle(captionStyle) { normal = { textColor = new Color(0f, 0f, 0f, 0.85f) } };
            // Sized to the window; in a tall (TikTok) window it sits high up, clear of the phone app's comments
            // along the bottom and its top bar.
            bool tall = (float)Screen.width / Mathf.Max(1, Screen.height) < DeskScene.TallAspect;
            captionStyle.fontSize = captionShadow.fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.width / (tall ? 16f : 30f)), 18, 40);
            float fade = Mathf.Clamp01((captionUntil - Time.time) / 0.5f);
            float height = captionStyle.fontSize * 2.6f;
            var rect = tall
                ? new Rect(Screen.width * 0.06f, Screen.height * 0.16f, Screen.width * 0.88f, height)
                : new Rect(0f, Screen.height - 110f, Screen.width, 60f);
            Color old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, fade);
            for (int dx = -2; dx <= 2; dx += 2)
                for (int dy = -2; dy <= 2; dy += 2)
                    if (dx != 0 || dy != 0) GUI.Label(new Rect(rect.x + dx, rect.y + dy, rect.width, rect.height), caption, captionShadow);
            GUI.Label(rect, caption, captionStyle);
            GUI.color = old;
        }
    }
}
