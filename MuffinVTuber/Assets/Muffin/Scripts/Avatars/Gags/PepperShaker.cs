using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// The pepper shaker: shake it and pepper puffs out of the holes; the room slowly fills with a peppery haze, and
    /// the more pepper in the air the more it tickles, until... ah... ah... ACHOO: a huge sneeze that rocks it back,
    /// blasts a cloud of pepper at the camera and blows everything on the desk away. Pull a sneeze face yourself
    /// (mouth open, eyes squeezed shut) and it sneezes on the spot. !shake shakes out a cloud (a sneeze follows);
    /// the face slam is an instant sneeze.
    /// </summary>
    public sealed class PepperShaker : GagAvatar
    {
        [Tooltip("The middle of the holes on the cap (rest, avatar space); zero = the model's top.")]
        public Vector3 holes;

        static readonly Color Pepper = new Color(0.32f, 0.27f, 0.22f, 0.55f), PepperHaze = new Color(0.55f, 0.5f, 0.44f, 0.5f),
            Specks = new Color(0.2f, 0.17f, 0.14f, 0.55f);

        Anchor cap, nose;
        Material speck;
        RoomHaze haze;
        float tickle, shakeOwed;
        bool sneezing;

        public float Tickle => tickle;
        public float Haze => haze != null ? haze.Amount : 0f;
        public override void TestMoment()
        {
            StopAllCoroutines(); // whatever sneeze was coming, this one goes now
            sneezing = false;
            Run(Sneeze(0.15f));
        }
        public override string TestReport => $"haze {Haze:0.00}, tickle {tickle:0.00}";
        protected override void DescribeMore(List<AvatarFeature> into)
        {
            into.Add(new AvatarFeature { Key = "haze", Label = "The room fills with pepper" });
            into.Add(new AvatarFeature { Key = "selfsneeze", Label = "Sneezes by itself when the pepper tickles" });
        }

        public override string MessName => "a cloud of pepper and a sneeze";

        protected override void Begin()
        {
            if (holes == Vector3.zero) holes = new Vector3(0f, body.max.y - 0.02f, 0f);
            cap = Pin(holes);
            // The sneeze comes out of the face, toward the camera.
            float face = TryGetComponent(out MeshAvatar avatar) ? avatar.faceFraction : 0.45f;
            nose = Pin(new Vector3(0f, body.size.y * (face - 0.03f), body.min.z - 0.02f));
            speck = Tint(new Color(0.12f, 0.1f, 0.08f));
            if (Chaos && Chaos.smoke) haze = new RoomHaze(Chaos.smoke, PepperHaze, 90) { VeilFrom = 0.75f, ClearFlat = 0.03f, ClearShare = 0.12f };
        }

        protected override void Tick(float dt)
        {
            float over = Mathf.Max(0f, meter.Shake - 6f);
            // Shaking puts pepper out of the holes (and into the air).
            shakeOwed += (over * 0.35f + meter.Speed * 1.2f) * dt;
            for (int g = 0; shakeOwed >= 1f && g < 6; g++)
            {
                shakeOwed -= 1f;
                Shake(1);
            }
            if (haze != null)
            {
                if (On("haze")) haze.Amount = Mathf.Clamp01(haze.Amount + over * 0.0018f * Amount * dt);
                else haze.Amount = 0f;
                haze.Update(Camera.main, dt);
            }
            if (sneezing) return;
            tickle = Mathf.Clamp01(tickle + ((haze != null ? haze.Amount : 0f) * 0.45f + over * 0.0015f - 0.03f) * dt);
            bool sneezeFace = mouth > 0.45f && eyesShutFor > 0.2f;
            if ((tickle >= 1f && On("selfsneeze")) || sneezeFace) Run(Sneeze(sneezeFace ? 0.2f : 1f));
        }

        void Shake(int puffCount)
        {
            if (puffs == null) return;
            Vector3 at = Pos(cap);
            for (int i = 0; i < puffCount; i++)
                puffs.Emit(at + Random.insideUnitSphere * 0.03f, (Vector3.up * Random.Range(0.1f, 0.4f) + Outward() * Random.Range(0.1f, 0.4f)) + meter.Velocity * 0.5f,
                    Pepper, Random.Range(0.07f, 0.12f), Random.Range(1.2f, 2f), 3f, 1.5f, -0.01f);
            if (Mess.Instance && Random.value < 0.15f * puffCount)
                Mess.Instance.DeskDust(InFront(Random.Range(-0.35f, 0.35f), Random.Range(0.02f, 0.3f)), Random.Range(0.03f, 0.06f), Specks, 90f, 1f);
            BitSound("spore_puff", 0.15f);
        }

        public override void Burst()
        {
            for (int i = 0; i < 6; i++) Shake(4);
            Sfx.Play("spore_puff", 0.7f, 0.1f);
            if (haze != null) haze.Amount = Mathf.Min(1f, haze.Amount + 0.35f);
            tickle = Mathf.Max(tickle, 0.85f);
        }

        public override void OnSlam()
        {
            if (!sneezing) Run(Sneeze(0.15f));
        }

        /// <summary>Ah... ah... (it rears back, twitching) ...CHOO.</summary>
        IEnumerator Sneeze(float windUp)
        {
            sneezing = true;
            for (float t = 0f; t < windUp * 1.1f; t += 0.18f)
            {
                rig.Bonk(0.04f + t * 0.05f);
                rig.KickTip(new Vector3(0f, 0.2f, 0.4f)); // rearing back, away from the camera
                yield return new WaitForSeconds(0.18f);
            }
            Sfx.Play("sneeze", 1f, 0.06f);
            yield return new WaitForSeconds(0.25f);
            rig.Bonk(0.5f);
            rig.KickTip(new Vector3(Random.Range(-0.4f, 0.4f), 0f, -3f)); // lunges at the camera
            // The blast: a dark cloud of pepper out of its face, straight at the camera, and specks on the lens.
            if (puffs != null)
            {
                Vector3 at = Pos(nose);
                Color blast = new Color(0.22f, 0.18f, 0.14f, 0.75f);
                for (int i = 0; i < 55; i++)
                    puffs.Emit(at + Random.insideUnitSphere * 0.06f, new Vector3(Random.Range(-1.4f, 1.4f), Random.Range(-0.5f, 0.25f), -Random.Range(2.2f, 3.4f)),
                        blast, Random.Range(0.18f, 0.32f), Random.Range(1.2f, 2.2f), 4f, 1.4f);
            }
            if (Mess.Instance && speck)
                for (int i = 0; i < 2; i++)
                    GlassItem.Stick(Mess.Instance.Lumps[i % Mess.Instance.Lumps.Length], speck, GlassItem.EdgeSpot(i == 0 ? -1f : 1f),
                        Random.Range(0.006f, 0.01f), 0f, Random.Range(3f, 5f), Random.Range(0f, 180f));
            if (haze != null) haze.Amount = Mathf.Min(1f, haze.Amount + 0.3f);
            // Everything on the desk is blown away from it: off to the sides and toward the front edge.
            if (DeskDebris.Instance)
                for (int k = 0; k < 10; k++)
                {
                    Vector3 c = transform.position;
                    DeskDebris.Instance.PushArea(c + new Vector3(-0.6f, 0f, -0.3f), 0.7f, new Vector3(-2.2f, 0.8f, -1.2f));
                    DeskDebris.Instance.PushArea(c + new Vector3(0.6f, 0f, -0.3f), 0.7f, new Vector3(2.2f, 0.8f, -1.2f));
                    DeskDebris.Instance.PushArea(c + new Vector3(0f, 0f, -0.5f), 0.5f, new Vector3(Random.Range(-0.8f, 0.8f), 0.9f, -2.6f));
                    yield return null;
                }
            if (Mess.Instance)
                for (int i = 0; i < 4; i++)
                    Mess.Instance.DeskDust(InFront(Random.Range(-0.5f, 0.5f), Random.Range(0.05f, 0.45f)), Random.Range(0.05f, 0.1f), Specks, 90f, 0.6f);
            tickle = 0f;
            yield return new WaitForSeconds(1.5f);
            sneezing = false;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            haze?.Clear();
            sneezing = false;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            haze?.Dispose();
        }
    }
}
