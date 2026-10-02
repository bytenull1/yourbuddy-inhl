using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using FMOD;
using FMODUnity;
using NPC.Core.Navigation;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// The mod's own sound clips, mp3s embedded under Resources/Sounds, played in 3D through FMOD's core API
    /// on the game's sfx bus. docs/anomalies.md#5-sounds
    /// </summary>
    internal static class ModSounds
    {
        private const string Prefix = "YourBuddy.Resources.Sounds.";
        private const string SfxBus = "bus:/master/sfx";
        /// <summary>Full volume within MinDistance, silent past MaxDistance.</summary>
        private const float MinDistance = 1.5f;
        private const float MaxDistance = 20f;
        private const float Volume = 0.85f;
        /// <summary>Through a wall, only what is under MuffledCutoff (Hz) gets through, at MuffledVolume.</summary>
        private const float MuffledCutoff = 600f;
        private const float MuffledVolume = 0.6f;
        /// <summary>The longest clip, rounded up. The core listener follows the game's while one may play.</summary>
        private const float ListenSeconds = 3f;

        private static readonly Dictionary<string, Sound> Loaded = [];
        private static readonly HashSet<string> Failed = [];
        private static ChannelGroup _sfx;
        private static bool _sfxFound;
        private static float _listenUntil;
        private static readonly List<(Channel channel, DSP filter)> Filters = [];

        /// <summary>
        /// The clips whose names start with `prefix`, as Play takes them ("odd_crack", "gore_pop1", ...).
        /// </summary>
        internal static List<string> Named(string prefix) =>
            typeof(ModSounds).Assembly.GetManifestResourceNames()
                .Where(n => n.StartsWith(Prefix + prefix) && n.EndsWith(".mp3"))
                .Select(n => n.Substring(Prefix.Length, n.Length - Prefix.Length - ".mp3".Length))
                .ToList();

        /// <summary>
        /// Plays `clip` at `at`, muffled when a wall or a shut door stands between it and you (`source`'s
        /// own colliders never do), and returns how long it lasts.
        /// </summary>
        internal static bool Play(string clip, Vector3 at, Transform? source, out float seconds)
        {
            seconds = 0f;
            if (!Load(clip, out Sound sound)) return false;

            FMOD.System core = RuntimeManager.CoreSystem;
            if (core.playSound(sound, Sfx(core), true, out Channel channel) != RESULT.OK) return false;

            if (sound.getLength(out uint ms, TIMEUNIT.MS) == RESULT.OK) seconds = ms / 1000f;

            VECTOR pos = at.ToFMODVector();
            VECTOR vel = default;
            channel.set3DAttributes(ref pos, ref vel);
            bool muffled = Muffle(core, channel, at, source);
            channel.setVolume(muffled ? Volume * MuffledVolume : Volume);
            SyncListener(core);
            channel.setPaused(false);
            _listenUntil = Time.time + ListenSeconds;
            return true;
        }

        /// <summary>
        /// A low-pass on the channel when you cannot see `at`, by the same test as being seen.
        /// </summary>
        private static bool Muffle(FMOD.System core, Channel channel, Vector3 at, Transform? source)
        {
            Transform? cam = PlayerView.Camera();
            if (cam == null || NavProbe.CanSee(cam.position, at, source, out _)) return false;

            if (core.createDSPByType(DSP_TYPE.LOWPASS_SIMPLE, out DSP filter) != RESULT.OK) return false;

            filter.setParameterFloat((int)DSP_LOWPASS_SIMPLE.CUTOFF, MuffledCutoff);
            if (channel.addDSP(CHANNELCONTROL_DSP_INDEX.HEAD, filter) != RESULT.OK)
            {
                filter.release();
                return false;
            }
            Filters.Add((channel, filter));
            return true;
        }

        /// <summary>
        /// From ScareSounds.Tick. Studio places only its own events; the core listener that places these
        /// clips is moved to the game's while one may still play. A finished clip's filter is freed.
        /// </summary>
        internal static void Tick()
        {
            if (Time.time < _listenUntil) SyncListener(RuntimeManager.CoreSystem);

            for (int i = Filters.Count - 1; i >= 0; i--)
            {
                if (Filters[i].channel.isPlaying(out bool playing) == RESULT.OK && playing) continue;

                Filters[i].filter.release();
                Filters.RemoveAt(i);
            }
        }

        private static void SyncListener(FMOD.System core)
        {
            if (RuntimeManager.StudioSystem.getListenerAttributes(0, out ATTRIBUTES_3D ears) != RESULT.OK) return;

            core.set3DListenerAttributes(0, ref ears.position, ref ears.velocity, ref ears.forward, ref ears.up);
        }

        /// <summary>
        /// The sfx bus's channel group, so the game's sfx volume applies; the master group until it exists.
        /// </summary>
        private static ChannelGroup Sfx(FMOD.System core)
        {
            if (_sfxFound) return _sfx;

            FMOD.Studio.Bus bus = RuntimeManager.GetBus(SfxBus);
            bus.lockChannelGroup();
            RuntimeManager.StudioSystem.flushCommands();
            if (bus.getChannelGroup(out _sfx) == RESULT.OK) _sfxFound = true;
            else core.getMasterChannelGroup(out _sfx);

            return _sfx;
        }

        private static bool Load(string clip, out Sound sound)
        {
            if (Loaded.TryGetValue(clip, out sound)) return true;
            if (Failed.Contains(clip)) return false;

            byte[]? data = Read(Prefix + clip + ".mp3");
            var info = new CREATESOUNDEXINFO { cbsize = Marshal.SizeOf(typeof(CREATESOUNDEXINFO)), length = (uint)(data?.Length ?? 0) };
            RESULT result = data == null ? RESULT.ERR_FILE_NOTFOUND :
                RuntimeManager.CoreSystem.createSound(data, MODE.OPENMEMORY | MODE.CREATESAMPLE | MODE._3D | MODE._3D_LINEARSQUAREROLLOFF,
                                                      ref info, out sound);
            if (result != RESULT.OK)
            {
                Failed.Add(clip);
                YourBuddyPlugin.Log.LogWarning($"[anomaly] Sound '{clip}' could not be loaded ({result}); it stays silent");
                return false;
            }
            sound.set3DMinMaxDistance(MinDistance, MaxDistance);
            Loaded[clip] = sound;
            return true;
        }

        private static byte[]? Read(string resource)
        {
            using Stream? stream = typeof(ModSounds).Assembly.GetManifestResourceStream(resource);
            if (stream == null) return null;

            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }
    }
}
