using System.Collections.Generic;
using UnityEngine;

namespace Muffin
{
    /// <summary>One thing an avatar does that the streamer can switch off or turn down, listed on its page in the menu.</summary>
    public sealed class AvatarFeature
    {
        public string Key, Label;
        /// <summary>A switch (on by default unless Default is false), or with Slider a 0..1 amount (DefaultValue).</summary>
        public bool Default = true;
        public bool Slider;
        public float DefaultValue = 1f;
    }

    /// <summary>Something on an avatar whose features the menu lists (the gag avatars, the mess makers, the flour bag).</summary>
    public interface IAvatarFeatures
    {
        void Describe(List<AvatarFeature> into);
    }

    /// <summary>
    /// Every avatar is controllable: each has its own remembered settings (switch off what it does when the mouth
    /// opens, turn its mess down, switch off its lights or its lens hits...), kept under the avatar's id. Plus the
    /// master switch for "your face sets off mess" on every avatar, for when you're talking to chat a lot.
    /// </summary>
    public static class AvatarSettings
    {
        /// <summary>Bumped on every change, so avatars re-read their settings only when something changed.</summary>
        public static int Version { get; private set; }

        /// <summary>The feature every mess-making avatar with face triggers has: your mouth, pucker or blinks set it off.</summary>
        public const string Face = "face";
        /// <summary>How much of its own mess it makes, 0..1.</summary>
        public const string Amount = "amount";

        static string Key(string avatar, string key) => $"muffin.av.{avatar}.{key}";

        public static bool On(string avatar, string key, bool fallback = true) => PlayerPrefs.GetInt(Key(avatar, key), fallback ? 1 : 0) == 1;

        public static void Set(string avatar, string key, bool on)
        {
            PlayerPrefs.SetInt(Key(avatar, key), on ? 1 : 0);
            Version++;
        }

        public static float Value(string avatar, string key, float fallback = 1f) => PlayerPrefs.GetFloat(Key(avatar, key), fallback);

        public static void SetValue(string avatar, string key, float value)
        {
            PlayerPrefs.SetFloat(Key(avatar, key), value);
            Version++;
        }

        /// <summary>Back to how it came.</summary>
        public static void Reset(string avatar, IEnumerable<AvatarFeature> features)
        {
            foreach (AvatarFeature f in features) PlayerPrefs.DeleteKey(Key(avatar, f.Key));
            Version++;
        }

        /// <summary>The master switch: your face (mouth, pucker, blinks) sets off avatars' mess at all.</summary>
        public static bool FaceMess
        {
            get => PlayerPrefs.GetInt("muffin.facemess", 1) == 1;
            set
            {
                PlayerPrefs.SetInt("muffin.facemess", value ? 1 : 0);
                Version++;
            }
        }

        /// <summary>The id an avatar's settings are kept under (its model id; the sunflower's name).</summary>
        public static string IdOf(Component c)
        {
            if (c.TryGetComponent(out MeshAvatar m) && !string.IsNullOrEmpty(m.id)) return m.id;
            return c.gameObject.name.ToLowerInvariant().Replace(' ', '_');
        }

        /// <summary>Everything the avatar's parts say about themselves, for its page in the menu.</summary>
        public static List<AvatarFeature> FeaturesOf(Component avatar)
        {
            var list = new List<AvatarFeature>();
            foreach (IAvatarFeatures f in avatar.GetComponents<IAvatarFeatures>()) f.Describe(list);
            return list;
        }
    }

    /// <summary>
    /// The avatars come in packs, each its own little add-on: a pack can be switched off in the menu and its avatars
    /// drop out of ↓/↑ and the avatar list. (One day a pack is something a streamer buys.)
    /// </summary>
    public static class AvatarPacks
    {
        public sealed class Pack
        {
            public string Id, Name, About;
        }

        public static readonly Pack[] All =
        {
            new Pack { Id = "starter", Name = "Starter", About = "The first ones: sunflower, steak, cactus, soda can, pizza, egg" },
            new Pack { Id = "snacks", Name = "Snack attack", About = "Food that gets everywhere" },
            new Pack { Id = "house", Name = "Around the house", About = "The kitchen, the bathroom and the laundry" },
            new Pack { Id = "party", Name = "Party pack", About = "Piñata, cake, soda, gumballs, balloon, bubble tea" },
            new Pack { Id = "lab", Name = "Mad science", About = "Volcano, toxic waste, lava lamp, fire extinguisher" },
            new Pack { Id = "cozy", Name = "Cozy corner", About = "Pillow, candle, snow globe, fish bowl, snowman" },
            new Pack { Id = "critters", Name = "Critters", About = "Hammy the hamster and the rubber chicken" },
        };

        public static string PackOf(Component avatar) =>
            avatar.TryGetComponent(out MeshAvatar m) && !string.IsNullOrEmpty(m.pack) ? m.pack : "starter";

        public static bool Enabled(string pack) => PlayerPrefs.GetInt("muffin.pack." + pack, 1) == 1;

        public static void SetEnabled(string pack, bool on)
        {
            PlayerPrefs.SetInt("muffin.pack." + pack, on ? 1 : 0);
            AvatarSettingsChanged();
        }

        public static string NameOf(string pack)
        {
            foreach (Pack p in All) if (p.Id == pack) return p.Name;
            return pack;
        }

        static void AvatarSettingsChanged() => AvatarSettings.SetValue("_packs", "changed", Time.realtimeSinceStartup);
    }
}
