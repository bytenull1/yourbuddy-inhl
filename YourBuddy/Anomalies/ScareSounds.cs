using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using NPC.Core;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// What an anomaly sound is meant to suggest. docs/anomalies.md#5-sounds
    /// </summary>
    internal enum ScareSound
    {
        /// <summary>The robot's talk blips, the buddy's "voice" for a spoken line.</summary>
        Voice,
        /// <summary>Clicking, from blips and a door's failed close.</summary>
        Click,
        /// <summary>Something at your back, from the scrubbing brush and the mod's knocks, cracks and rips.</summary>
        Odd,
        /// <summary>Feeding, from the mod's tearing, popping and chewing.</summary>
        Gore,
        /// <summary>The Breathless moving, the game's own scare stings.</summary>
        Creature,
        /// <summary>The Breathless screech, a scream.</summary>
        Shriek
    }

    /// <summary>
    /// The game's FMOD events, borrowed from whatever instance of their owner the scene holds and kept until
    /// the world resets, and the mod's own clips (ModSounds). A category with nothing found stays silent.
    /// </summary>
    internal static class ScareSounds
    {
        /// <summary>A game event, or the name of one of the mod's clips.</summary>
        private readonly struct Source(EventReference sound, string? clip)
        {
            internal readonly EventReference Event = sound;
            internal readonly string? Clip = clip;
        }

        private static readonly Dictionary<ScareSound, List<Source>> Found = [];
        /// <summary>
        /// Each category's last picks, by index, not picked again yet. docs/anomalies.md#5-sounds
        /// </summary>
        private static readonly Dictionary<ScareSound, List<int>> Recent = [];
        private const int RecentCount = 2;
        private static bool _gathered;
        private static float _gatheredAt;
        /// <summary>
        /// The monster's and the game's scare sounds can run for many seconds, so they are faded out
        /// after SoundCapSeconds. docs/anomalies.md#5-sounds
        /// </summary>
        internal const float SoundCapSeconds = 2.5f;
        private static readonly List<(EventInstance sound, float stopAt)> Playing = [];
        /// <summary>
        /// Voice blips still to come, with who speaks and when. docs/anomalies.md#3-what-it-says
        /// </summary>
        private static readonly List<(Transform speaker, float at)> Blips = [];
        private const float BlipGapMin = 0.07f;
        private const float BlipGapMax = 0.12f;
        private const float BlipHeight = 1.5f;
        /// <summary>
        /// A category found empty is looked for again after this, since its owners may load later.
        /// </summary>
        private const float RegatherSeconds = 60f;

        static ScareSounds()
        {
            NpcEvents.WorldReset += () =>
            {
                Found.Clear();
                _gathered = false;
                foreach ((EventInstance sound, float _) in Playing) Stop(sound, FMOD.Studio.STOP_MODE.IMMEDIATE);

                Playing.Clear();
                Blips.Clear();
            };
        }

        /// <summary>
        /// Plays one sound of the category at `at`, in 3D. False when none was found.
        /// </summary>
        internal static bool Play(ScareSound kind, Vector3 at) => Play(kind, at, null, out _);

        /// <summary>
        /// Plays from `source` (its colliders never muffle a clip) and returns how long the sound lasts.
        /// </summary>
        internal static bool Play(ScareSound kind, Vector3 at, Transform? source, out float seconds)
        {
            seconds = 0f;
            Gather();
            if (!Found.TryGetValue(kind, out List<Source>? list) || list.Count == 0) return false;

            Source picked = list[Pick(kind, list.Count)];
            if (picked.Clip != null)
            {
                bool played = ModSounds.Play(picked.Clip, at, source, out seconds);
                if (played) Trace(kind, picked.Clip, seconds);

                return played;
            }
            seconds = Length(picked.Event, kind >= ScareSound.Creature);
            if (kind >= ScareSound.Odd) Trace(kind, Path(picked.Event), seconds);

            if (kind < ScareSound.Creature)
            {
                RuntimeManager.PlayOneShot(picked.Event, at);
                return true;
            }
            EventInstance sound = RuntimeManager.CreateInstance(picked.Event);
            sound.set3DAttributes(at.To3DAttributes());
            sound.start();
            Playing.Add((sound, Time.time + SoundCapSeconds));
            return true;
        }

        /// <summary>
        /// An event's length in seconds, at most SoundCapSeconds for a capped one. Unknown (looping) gives 1 s, or the cap.
        /// </summary>
        private static float Length(EventReference sound, bool capped)
        {
            float seconds = RuntimeManager.GetEventDescription(sound).getLength(out int ms) == FMOD.RESULT.OK && ms > 0 ? ms / 1000f : 0f;
            if (capped) return seconds > 0f ? Mathf.Min(seconds, SoundCapSeconds) : SoundCapSeconds;

            return seconds > 0f ? seconds : 1f;
        }

        private static string Path(EventReference sound) =>
            RuntimeManager.GetEventDescription(sound).getPath(out string path) == FMOD.RESULT.OK ? path : sound.Guid.ToString();

        private static void Trace(ScareSound kind, string what, float seconds)
        {
            if (NpcLog.Level >= 2) YourBuddyPlugin.Log.LogInfo($"[anomaly] Sound {kind}: {what} ({seconds:0.0}s)");
        }

        /// <summary>
        /// A random index under `count`, not one of the category's last RecentCount (fewer when it has fewer).
        /// </summary>
        private static int Pick(ScareSound kind, int count)
        {
            if (!Recent.TryGetValue(kind, out List<int>? recent)) Recent[kind] = recent = [];

            int keep = Mathf.Min(RecentCount, count - 1);
            while (recent.Count > keep) recent.RemoveAt(0);

            int left = Random.Range(0, count - recent.Count);
            int picked = 0;
            for (int i = 0; i < count; i++)
            {
                if (recent.Contains(i)) continue;
                if (left-- == 0)
                {
                    picked = i;
                    break;
                }
            }
            if (keep > 0) recent.Add(picked);

            return picked;
        }

        /// <summary>
        /// A line's worth of the robot's talk blips at the speaker, one every BlipGapMin..Max, as the
        /// station robot's own typewriter plays them.
        /// </summary>
        internal static void Babble(Transform speaker, int count)
        {
            float at = Time.time;
            for (int i = 0; i < count; i++)
            {
                Blips.Add((speaker, at));
                at += Random.Range(BlipGapMin, BlipGapMax);
            }
        }

        /// <summary>
        /// From AnomalyDirector.Tick. Plays the blips that are due, and fades out the long sounds whose
        /// time is up.
        /// </summary>
        internal static void Tick()
        {
            for (int i = Blips.Count - 1; i >= 0; i--)
            {
                if (Time.time < Blips[i].at) continue;

                if (Blips[i].speaker != null) Play(ScareSound.Voice, Blips[i].speaker.position + Vector3.up * BlipHeight);
                Blips.RemoveAt(i);
            }

            for (int i = Playing.Count - 1; i >= 0; i--)
            {
                if (Time.time < Playing[i].stopAt) continue;

                Stop(Playing[i].sound, FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
                Playing.RemoveAt(i);
            }
            ModSounds.Tick();
        }

        private static void Stop(EventInstance sound, FMOD.Studio.STOP_MODE how)
        {
            if (!sound.isValid()) return;

            sound.stop(how);
            sound.release();
        }

        private static void Gather()
        {
            bool complete = Found.Count == System.Enum.GetValues(typeof(ScareSound)).Length;
            if (_gathered && (complete || Time.time - _gatheredAt < RegatherSeconds)) return;

            _gathered = true;
            _gatheredAt = Time.time;
            Found.Clear();
            Recent.Clear();

            AssistanceBot? bot = Object.FindObjectOfType<AssistanceBot>(true);
            Breathless? breathless = GameManager.Instance != null ? GameManager.Instance.Breathless : null;
            Gate? gate = Object.FindObjectOfType<Gate>(true);

            Add(ScareSound.Voice, GameInternals.ScareSoundAccess.GetTalk(bot));
            Add(ScareSound.Click, GameInternals.ScareSoundAccess.GetTalk(bot));
            Add(ScareSound.Click, GameInternals.ScareSoundAccess.GetCloseFail(gate));
            Add(ScareSound.Odd, GameInternals.ScareSoundAccess.GetClean(Object.FindObjectOfType<Cleanable>(true)));
            foreach (string clip in ModSounds.Named("odd_")) Add(ScareSound.Odd, new Source(default, clip));
            foreach (string clip in ModSounds.Named("gore_")) Add(ScareSound.Gore, new Source(default, clip));
            Add(ScareSound.Creature, GameInternals.ScareSoundAccess.GetMoving(breathless));
            Add(ScareSound.Creature, GameInternals.ScareSoundAccess.GetActivity(Object.FindObjectOfType<BreathlessActivity>(true)));
            Add(ScareSound.Creature, GameInternals.ScareSoundAccess.GetRandom(Object.FindObjectOfType<RandomSound>(true)));
            Add(ScareSound.Creature, GameInternals.ScareSoundAccess.GetBackground(Object.FindObjectOfType<BackgroundSound>(true)));
            Add(ScareSound.Shriek, GameInternals.ScareSoundAccess.GetScreech(breathless));
            Add(ScareSound.Shriek, GameInternals.ScareSoundAccess.GetScream(Object.FindObjectOfType<UnsealScream>(true)));

            if (NpcLog.Level < 2) return;

            YourBuddyPlugin.Log.LogInfo("[anomaly] Sounds found: " + Describe());
        }

        private static void Add(ScareSound kind, EventReference? sound)
        {
            if (sound is not { IsNull: false } found) return;

            Add(kind, new Source(found, null));
        }

        private static void Add(ScareSound kind, Source source)
        {
            if (!Found.TryGetValue(kind, out List<Source>? list)) Found[kind] = list = [];

            list.Add(source);
        }

        /// <summary>
        /// "Voice 1, Click 2, ...", for the log and buddy_anomaly.
        /// </summary>
        internal static string Describe()
        {
            Gather();
            string text = "";
            foreach (KeyValuePair<ScareSound, List<Source>> entry in Found)
            {
                text += (text.Length > 0 ? ", " : "") + entry.Key + " " + entry.Value.Count;
            }
            return text.Length > 0 ? text : "none";
        }
    }
}
