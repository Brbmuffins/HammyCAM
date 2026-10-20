using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// The rubber chicken: its long neck is a floppy tip that whips about as it moves. Open your mouth wide and it
    /// SCREAMS (a squeezed rubber chicken's long wheezing honk, made here, no sound file needed): it squashes, its
    /// neck flings back and a puff of air blasts out of its beak. A hard shake makes it lay an egg that cracks on the
    /// desk. Its face sits on its head (just eyes; the beak is its mouth) or, on its page, on its belly.
    /// </summary>
    public sealed class RubberChicken : GagAvatar
    {
        [Tooltip("The beak's tip (rest, avatar space): the scream's puff of air comes out here.")]
        public Vector3 beak;

        static readonly Color Yolk = new Color(1f, 0.72f, 0.08f, 0.95f), White = new Color(1f, 1f, 0.94f, 0.55f), Shell = new Color(0.98f, 0.95f, 0.88f);

        Anchor beakAnchor;
        AudioSource voice;
        AudioClip[] screams;
        float nextEgg, screaming;

        public override string MessName => "a scream and an egg";

        protected override void DescribeMore(List<AvatarFeature> into)
        {
            into.Add(new AvatarFeature { Key = "scream", Label = "Screams when you open your mouth wide" });
            into.Add(new AvatarFeature { Key = "eggs", Label = "A hard shake makes it lay an egg" });
        }

        public override void TestMoment() => Scream(1f);
        public override string TestReport => screams != null ? "screams ready" : "no voice";

        protected override void Begin()
        {
            if (beak == Vector3.zero) beak = new Vector3(0f, body.size.y * 0.9f, body.min.z);
            beakAnchor = Pin(beak);
            voice = gameObject.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.spatialBlend = 0f;
            screams = new[] { MakeScream(1, 1.2f), MakeScream(2, 0.8f), MakeScream(3, 1.6f) };
        }

        protected override void Tick(float dt)
        {
            screaming = Mathf.MoveTowards(screaming, 0f, dt);
            if (On("scream") && mouthJustOpened && mouth > 0.55f) Scream(Mathf.Lerp(0.8f, 1.2f, mouth));
            if (On("eggs") && meter.Shake > 55f && Time.time >= nextEgg) LayEgg();
        }

        void Scream(float strength)
        {
            if (Sound.On && voice && screams != null)
            {
                voice.pitch = Random.Range(0.9f, 1.15f);
                voice.PlayOneShot(screams[Random.Range(0, screams.Length)], SoundBank.Volume * Mathf.Clamp01(strength));
            }
            screaming = 1f;
            rig.Bonk(0.4f * strength);
            // The neck flings back and up, the way a squeezed rubber chicken's does.
            rig.KickTip(new Vector3(Random.Range(-0.6f, 0.6f), 1.2f, 2.2f) * strength);
            if (puffs != null)
                for (int i = 0; i < 10; i++)
                    puffs.Emit(Pos(beakAnchor), new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(-0.1f, 0.2f), -Random.Range(1f, 1.8f)),
                        new Color(1f, 1f, 1f, 0.3f), 0.05f, 0.6f, 4f, 2.5f);
        }

        /// <summary>Plop: an egg drops out from under it and cracks on the desk.</summary>
        void LayEgg()
        {
            nextEgg = Time.time + 6f;
            Run(Egg());
        }

        IEnumerator Egg()
        {
            Sfx.Play("plop", 0.6f, 0.1f);
            Transform egg = Item(PrimitiveType.Sphere, Shell, new Vector3(0.07f, 0.09f, 0.07f));
            Vector3 at = transform.position + new Vector3(Random.Range(-0.05f, 0.05f), body.size.y * 0.32f, 0f);
            egg.position = at;
            Vector3 v = new Vector3(Random.Range(-0.3f, 0.3f), 0.2f, -0.8f);
            for (float t = 0f; t < 0.6f && egg; t += Time.deltaTime)
            {
                v.y -= 9f * Time.deltaTime;
                egg.position += v * Time.deltaTime;
                float ground = DeskScene.SurfaceHeight(egg.position.x, egg.position.z);
                if (!float.IsNegativeInfinity(ground) && egg.position.y <= ground + 0.04f) break;
                yield return null;
            }
            if (!egg) yield break;
            Vector3 landed = egg.position;
            Destroy(egg.gameObject);
            Sfx.Play("egg_crack", 0.9f, 0.08f);
            if (Mess.Instance && Keep())
            {
                Mess.Instance.DeskStain(landed, 0.12f, White, 150f, 0.5f, true);
                Mess.Instance.DeskStain(landed + new Vector3(0.01f, 0f, 0.01f), 0.05f, Yolk, 150f, 0.3f, true);
                Mess.Instance.Blob(null, landed + new Vector3(0.01f, 0.01f, 0.01f), new Vector3(0.055f, 0.022f, 0.055f), new Color(1f, 0.68f, 0.05f), 150f, 0.3f);
            }
            for (int i = 0; i < 6; i++)
            {
                Transform shell = Item(PrimitiveType.Sphere, Shell, new Vector3(Random.Range(0.025f, 0.045f), 0.007f, Random.Range(0.02f, 0.04f)));
                shell.position = landed + Vector3.up * 0.05f;
                shell.rotation = Random.rotation;
                ToDesk(shell, new Vector3(Random.Range(-0.8f, 0.8f), Random.Range(0.5f, 1.2f), Random.Range(-0.8f, 0.3f)), 0.02f, 0.004f);
            }
        }

        public override void Burst()
        {
            Scream(1f);
            LayEgg();
        }

        public override void OnSlam()
        {
            Scream(1.3f);
            Run(ThreeEggs());
        }

        IEnumerator ThreeEggs()
        {
            for (int i = 0; i < 3; i++)
            {
                LayEgg();
                yield return new WaitForSeconds(0.35f);
            }
        }

        public override void Whack(float strength)
        {
            base.Whack(strength);
            Scream(0.7f);
        }

        /// <summary>
        /// A rubber chicken's scream, synthesized: a reedy buzz through a narrow rubber throat, a fast rise then a long
        /// sag in pitch as the air runs out, a nasal formant, a rattle of flutter and a breathy hiss on top.
        /// </summary>
        static AudioClip MakeScream(int seed, float length)
        {
            const int rate = 44100;
            var random = new System.Random(seed);
            int n = (int)(rate * length);
            var data = new float[n];
            float f0 = 520f + (float)random.NextDouble() * 180f, phase = 0f, low = 0f, band = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate, k = t / length;
                float rise = Mathf.Clamp01(t / 0.06f);
                float pitch = f0 * (0.75f + 0.45f * rise) * (1f - 0.45f * k * k) * (1f + 0.04f * Mathf.Sin(2f * Mathf.PI * 23f * t));
                phase += 2f * Mathf.PI * pitch / rate;
                // A narrow-pulse buzz (reedy), plus a little noise.
                float pulse = Mathf.Sin(phase) > 0.7f ? 1f : -0.25f;
                float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                float x = pulse * 0.8f + noise * 0.2f;
                // A rough resonant band-pass round 1.8 kHz for the nasal honk.
                low += (x - low) * 0.35f;
                band += (low - band) * 0.12f;
                float voiced = low - band;
                float envelope = Mathf.Clamp01(t / 0.015f) * Mathf.Clamp01((length - t) / 0.25f) * (0.8f + 0.2f * Mathf.Sin(2f * Mathf.PI * 7f * t));
                data[i] = Mathf.Clamp(voiced * 2.2f * envelope, -1f, 1f) * 0.55f;
            }
            AudioClip clip = AudioClip.Create($"Chicken scream {seed}", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
