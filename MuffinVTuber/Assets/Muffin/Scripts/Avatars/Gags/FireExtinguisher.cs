using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// The fire extinguisher: open your mouth and it blasts a roaring white cloud of powder out of its nozzle, aimed
    /// the way you're looking, kicking back as it goes; the room fills with white and the desk gets dusted. When a
    /// viewer sets the desk on fire (!fire) it goes for the fire by itself and puts it out. !shake is a long blast;
    /// the face slam bursts the valve and it sprays all round.
    /// </summary>
    public sealed class FireExtinguisher : GagAvatar
    {
        [Tooltip("The nozzle's tip, and a point a little back along it (rest, avatar space): tip - back is its aim.")]
        public Vector3 nozzle, nozzleBack;
        [Tooltip("The valve on top (rest, avatar space): the face slam bursts it.")]
        public Vector3 valve;

        static readonly Color Powder = new Color(0.97f, 0.97f, 0.99f, 0.75f), PowderHaze = new Color(0.96f, 0.96f, 0.98f, 0.55f), Dusting = new Color(0.97f, 0.97f, 0.98f, 0.6f);

        Anchor tip, back, top;
        RoomHaze haze;
        SoundLoop roar;
        float blastFor, burstFor, autoAt = -1f, emitOwed, dustOwed;
        bool autoBlast;

        public bool Blasting { get; private set; }
        public float Haze => haze != null ? haze.Amount : 0f;
        public override void TestMoment() => blastFor = 2f;
        public override string TestReport => $"haze {Haze:0.00}{(Blasting ? ", blasting" : "")}";
        public override string MessName => "a cloud of extinguisher powder";
        protected override float MouthThreshold => 0.35f;

        protected override void Begin()
        {
            if (valve == Vector3.zero) valve = new Vector3(0f, body.max.y - 0.03f, 0f);
            if (nozzle == Vector3.zero) nozzle = new Vector3(body.max.x, body.size.y * 0.45f, body.min.z * 0.5f);
            if (nozzleBack == Vector3.zero) nozzleBack = nozzle + new Vector3(-0.05f, 0.08f, 0f);
            tip = Pin(nozzle);
            back = Pin(nozzleBack);
            top = Pin(valve);
            if (Chaos && Chaos.smoke) haze = new RoomHaze(Chaos.smoke, PowderHaze, 100) { VeilFrom = 0.6f, ClearFlat = 0.035f, ClearShare = 0.1f };
        }

        protected override void Tick(float dt)
        {
            // A fire on the desk: a moment's alarm, then it goes for it by itself.
            bool fire = Chaos && Chaos.FireBurning;
            if (fire && autoAt < 0f) autoAt = Time.time + 1.2f;
            if (!fire) autoAt = -1f;
            autoBlast = fire && Time.time >= autoAt;

            blastFor = Mathf.Max(0f, blastFor - dt);
            burstFor = Mathf.Max(0f, burstFor - dt);
            bool blasting = mouth > MouthThreshold || blastFor > 0f || burstFor > 0f || autoBlast;
            if (blasting != Blasting)
            {
                Blasting = blasting;
                if (blasting)
                {
                    Sfx.Play("steam_hiss", 0.9f, 0.08f);
                    roar = Sfx.Loop("steam_hiss", 0.8f);
                }
                else
                {
                    roar?.Stop(0.3f);
                    roar = null;
                    Sfx.Play("spore_puff", 0.5f, 0.1f); // the last cough of powder
                }
            }
            if (Blasting) Spray(dt);
            if (haze != null) haze.Update(Camera.main, dt);
        }

        /// <summary>Which way the powder goes: toward the fire, all round (a burst valve), or the way you're looking.</summary>
        Vector3 Aim()
        {
            if (autoBlast)
            {
                Vector3 to = Chaos.FireSpot + Vector3.up * 0.1f - Pos(tip);
                return to.normalized;
            }
            float side = TurnedSide;
            // The horn hangs mouth-down at its side: the blast goes out the way it's looking (a little up), not at the desk.
            Vector3 look = new Vector3(side * 0.9f + Facing.x, 0.18f, -(1f - Mathf.Abs(side) * 0.45f));
            return look.normalized;
        }

        void Spray(float dt)
        {
            Vector3 from = burstFor > 0f ? Pos(top) : Pos(tip);
            emitOwed += (burstFor > 0f ? 70f : 45f) * dt;
            for (int g = 0; emitOwed >= 1f && g < 6; g++)
            {
                emitOwed -= 1f;
                Vector3 aim = burstFor > 0f ? (Random.onUnitSphere + Vector3.up * 0.6f).normalized : (Aim() + Random.insideUnitSphere * 0.18f).normalized;
                puffs?.Emit(from + aim * 0.03f, aim * Random.Range(3f, 4.4f), Powder, Random.Range(0.09f, 0.15f), Random.Range(1.1f, 1.8f), 6f, 2.2f, 0.08f);
            }
            if (haze != null) haze.Amount = Mathf.Min(1f, haze.Amount + (burstFor > 0f ? 0.3f : 0.11f) * dt);
            // Recoil: it rocks back away from where it's spraying.
            Vector3 a = burstFor > 0f ? Vector3.zero : Aim();
            rig.KickTip(new Vector3(-a.x, 0f, -a.z) * 3.5f * dt);
            if (Random.value < 6f * dt) rig.Bonk(0.03f);
            // Powder settles on the desk where it's aimed.
            dustOwed += 2.5f * dt;
            if (dustOwed >= 1f && Mess.Instance)
            {
                dustOwed -= 1f;
                Vector3 land = autoBlast ? Chaos.FireSpot : from + a * Random.Range(0.3f, 0.8f);
                land += new Vector3(Random.Range(-0.1f, 0.1f), 0f, Random.Range(-0.1f, 0.1f));
                Mess.Instance.DeskDust(land, Random.Range(0.06f, 0.11f), Dusting, 120f, 0.8f);
            }
            if (autoBlast && Chaos.FireBurning)
            {
                // Once the powder has had a moment to reach it, the fire's out.
                if (Time.time > autoAt + 1.4f) Chaos.PutOutFire();
            }
        }

        public override void Burst() => blastFor = 2.2f;

        public override void OnSlam()
        {
            Sfx.Play("pop", 1f, 0.05f);
            burstFor = 1.2f;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            roar?.Stop(0.1f);
            roar = null;
            Blasting = false;
            haze?.Clear();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            haze?.Dispose();
        }
    }
}
