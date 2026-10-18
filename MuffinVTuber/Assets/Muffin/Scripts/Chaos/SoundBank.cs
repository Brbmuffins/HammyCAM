using System.Collections.Generic;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// The app's sound effects (splats, sizzles, bonks…: Assets/Muffin/Sounds, made with props-service
    /// batches/sfx-01.json). Sfx.Play("splat") plays one, picking among numbered variants (splat_1, splat_2) with a
    /// touch of random pitch so repeats don't sound canned; Sfx.Loop("fire_crackle") holds a looping one until it's
    /// stopped. Everything follows the Sounds switch and the volume (control panel → Desk & mess).
    /// </summary>
    public sealed class SoundBank : MonoBehaviour
    {
        public static SoundBank Instance { get; private set; }

        public AudioClip[] clips;
        [Tooltip("The same sound won't start again sooner than this (seconds): a burst of splats stays a sound, not a buzz.")]
        public float minGap = 0.06f;

        readonly Dictionary<string, List<AudioClip>> byName = new Dictionary<string, List<AudioClip>>();
        readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();
        readonly List<SoundLoop> loops = new List<SoundLoop>();
        AudioSource[] voices;
        int nextVoice;

        public int Count => clips != null ? clips.Length : 0;

        /// <summary>Overall loudness 0..1, remembered.</summary>
        public static float Volume
        {
            get => PlayerPrefs.GetFloat("muffin.volume", 0.8f);
            set => PlayerPrefs.SetFloat("muffin.volume", Mathf.Clamp01(value));
        }

        void Awake()
        {
            Instance = this;
            foreach (AudioClip clip in clips ?? new AudioClip[0])
            {
                if (!clip) continue;
                // "splat_2" is a variant of "splat".
                string name = clip.name;
                int underscore = name.LastIndexOf('_');
                if (underscore > 0 && int.TryParse(name.Substring(underscore + 1), out _)) name = name.Substring(0, underscore);
                if (!byName.TryGetValue(name, out var list)) byName[name] = list = new List<AudioClip>();
                list.Add(clip);
            }
            voices = new AudioSource[10];
            for (int i = 0; i < voices.Length; i++)
            {
                voices[i] = gameObject.AddComponent<AudioSource>();
                voices[i].playOnAwake = false;
                voices[i].spatialBlend = 0f;
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public bool Has(string name) => byName.ContainsKey(name);

        public AudioClip Pick(string name) => byName.TryGetValue(name, out var list) && list.Count > 0 ? list[Random.Range(0, list.Count)] : null;

        public void PlayNow(string name, float volume, float pitchJitter)
        {
            if (!Sound.On) return;
            AudioClip clip = Pick(name);
            if (!clip) return;
            float now = Time.unscaledTime;
            if (lastPlayed.TryGetValue(name, out float last) && now - last < minGap) return;
            lastPlayed[name] = now;
            AudioSource voice = voices[nextVoice];
            nextVoice = (nextVoice + 1) % voices.Length;
            voice.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            voice.PlayOneShot(clip, Mathf.Clamp01(volume) * Volume);
        }

        public SoundLoop StartLoop(string name, float volume)
        {
            AudioClip clip = Pick(name);
            if (!clip) return null;
            var source = gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.time = Random.Range(0f, clip.length * 0.5f);
            var loop = new SoundLoop(source, volume);
            loops.Add(loop);
            source.volume = 0f;
            source.Play();
            return loop;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = loops.Count - 1; i >= 0; i--)
            {
                SoundLoop loop = loops[i];
                if (!loop.Step(dt, Sound.On ? Volume : 0f))
                {
                    Destroy(loop.Source);
                    loops.RemoveAt(i);
                }
            }
        }
    }

    /// <summary>A looping sound playing (a fire crackling): set how loud it is, and stop it (it fades out).</summary>
    public sealed class SoundLoop
    {
        public readonly AudioSource Source;
        float target, current, fadeOut = -1f;

        public SoundLoop(AudioSource source, float volume)
        {
            Source = source;
            target = volume;
        }

        public void SetVolume(float volume) => target = Mathf.Clamp01(volume);

        public void Stop(float fade = 0.4f) => fadeOut = Mathf.Max(0.01f, fade);

        /// <summary>False once it has faded out and can go.</summary>
        internal bool Step(float dt, float master)
        {
            if (!Source) return false;
            if (fadeOut > 0f)
            {
                current = Mathf.MoveTowards(current, 0f, dt / fadeOut);
                if (current <= 0f) return false;
            }
            else current = Mathf.MoveTowards(current, target, dt * 4f); // fades in quickly
            Source.volume = current * master;
            return true;
        }
    }

    /// <summary>Short calls for the sound effects (nothing happens if there's no sound bank or sounds are off).</summary>
    public static class Sfx
    {
        public static void Play(string name, float volume = 1f, float pitchJitter = 0.08f) => SoundBank.Instance?.PlayNow(name, volume, pitchJitter);
        public static SoundLoop Loop(string name, float volume = 1f) => SoundBank.Instance ? SoundBank.Instance.StartLoop(name, volume) : null;
    }
}
