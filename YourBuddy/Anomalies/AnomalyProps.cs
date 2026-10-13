using System.Collections.Generic;
using NPC.Core;
using Space.Data;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// What an anomaly leaves lying about. Raw meat and a bloody pipe you can pick up and bin, and blood
    /// you can scrub off. They clone the game's own item and blood decal with save data the save never
    /// holds, so the save never learns of them. docs/invariants.md#an-anomaly-prop-never-enters-the-save
    /// </summary>
    internal static class AnomalyProps
    {
        private const string MeatResource = "YourBuddy.Resources.Meat.bbmodel";
        /// <summary>
        /// The item the meat is cloned from, with one mesh, a convex collider and nothing else saved.
        /// </summary>
        private const string MeatBase = "Skull";
        /// <summary>
        /// The meat's size over the model's centimetres. The Skull's own root is scaled down.
        /// </summary>
        private const float MeatScale = 1.5f;
        /// <summary>
        /// The game's blood decal is a one-sided plane, so it is never mirrored.
        /// </summary>
        private const string BloodPrefab = "prefabs/objects/decals/DirtBlood";
        /// <summary>
        /// Dark red over the decal's greyish-pink texture (about 112, 87, 93). Alpha is the materials' own.
        /// </summary>
        private static readonly Color BloodTint = new(1.12f, 0.2f, 0.21f, 0.5019608f);
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        internal const string MeatSignature = "YB_MEAT";
        private const string PipeBase = "Metal_Pipe";
        internal const string PipeSignature = "YB_PIPE";
        /// <summary>
        /// Oldest first, the props beyond this are removed.
        /// </summary>
        private const int MaxProps = 16;
        /// <summary>
        /// Save ids the game's counter never reaches. The props' data is never registered, so these only
        /// keep SaveObject from making real data. Above GrabbableData.Preplaced, so a bin destroys the meat.
        /// </summary>
        private const uint FirstUnsavedId = 0xFFF00000u;

        private static uint _nextId = FirstUnsavedId;
        private static readonly List<GameObject> Spawned = [];
        private static GameObject? _holder;
        private static Cleanable? _bloodTemplate;
        private static bool _warned;

        /// <summary>One Meat set piece's meat and blood, and whether you have been there.</summary>
        private sealed class Mess
        {
            internal readonly List<GameObject> Props = [];
            internal bool Visited;
        }

        private static readonly List<Mess> Messes = [];
        /// <summary>Props that go the moment you cannot see them (the dropped pipe). docs/anomalies.md#pipe</summary>
        private static readonly List<GameObject> Fleeting = [];
        private static float _messCheckAt;
        private const float MessCheckSeconds = 0.5f;
        private const float MessSeenDist = 10f;
        private const float MessNearDist = 4f;
        private const float MessGoneDist = 15f;

        static AnomalyProps()
        {
            NpcEvents.WorldReset += () =>
            {
                Spawned.Clear();
                Messes.Clear();
                Fleeting.Clear();
                _holder = null;
                _bloodTemplate = null;
            };
        }

        /// <summary>
        /// A switched-off parent, so a clone is set up under it before its Awake runs.
        /// </summary>
        private static Transform Holder()
        {
            if (_holder == null)
            {
                _holder = new GameObject("YourBuddy props (setup)");
                _holder.SetActive(false);
            }
            return _holder.transform;
        }

        /// <summary>
        /// Raw meat on the floor at `floor`, under `parent` (a room's content). Null when it cannot be made.
        /// </summary>
        internal static Grabbable? SpawnMeat(Transform parent, Vector3 floor, float yaw)
        {
            Grabbable? prefab = ResourceLoader.GetItemPrefab(MeatBase);
            BbModel.Model? model = BbModel.Load(MeatResource);
            if (!GameInternals.PropAccess.Ready || prefab == null || model == null)
            {
                WarnOnce("no meat: " + (prefab == null ? $"no '{MeatBase}' item to copy" : model == null ? "no model" : "game internals changed"));
                return null;
            }
            Grabbable meat = Object.Instantiate(prefab, Holder());
            meat.name = "YB_Meat";
            meat.transform.localScale = Vector3.one * MeatScale;
            foreach (MeshFilter filter in meat.GetComponentsInChildren<MeshFilter>(true)) filter.sharedMesh = model.Mesh;
            foreach (MeshCollider collider in meat.GetComponentsInChildren<MeshCollider>(true)) collider.sharedMesh = model.Hull;
            if (meat.TryGetComponent(out MeshRenderer renderer))
            {
                Material material = renderer.material;
                material.SetTexture("_BaseMap", model.Texture);
                material.mainTexture = model.Texture;
                Matte(material);
            }
            if (meat.TryGetComponent(out Rigidbody body)) body.mass = 1f;

            GameInternals.PropAccess.SetItem(meat, MeatSignature, canStore: false, canSell: false, canTrash: true, price: 0);
            GameInternals.PropAccess.MuteImpacts(meat);
            meat.gameObject.AddComponent<MeatSounds>();
            Place(meat, parent, floor + Vector3.up * 0.03f, Quaternion.Euler(0f, yaw, 0f));
            AddToMess(meat.gameObject);
            return meat;
        }

        /// <summary>
        /// The game's metal pipe, one end bloody, at `at` under `parent`. Null when it cannot be made.
        /// </summary>
        internal static Grabbable? SpawnPipe(Transform parent, Vector3 at, Quaternion rotation)
        {
            Grabbable? prefab = ResourceLoader.GetItemPrefab(PipeBase);
            if (!GameInternals.PropAccess.Ready || prefab == null)
            {
                YourBuddyPlugin.Log.LogWarning("[anomaly] No bloody pipe - " + (prefab == null ? $"no '{PipeBase}' item to copy" : "game internals changed"));
                return null;
            }
            Grabbable pipe = Object.Instantiate(prefab, Holder());
            pipe.name = "YB_Pipe";
            if (pipe.TryGetComponent(out MeshRenderer renderer) && renderer.sharedMaterial != null && renderer.sharedMaterial.mainTexture is { } clean &&
                BuddyGore.BloodyEnd(clean) is { } bloody)
            {
                Material material = renderer.material;
                material.SetTexture("_BaseMap", bloody);
                material.mainTexture = bloody;
            }
            GameInternals.PropAccess.SetItem(pipe, PipeSignature, canStore: false, canSell: false, canTrash: true, price: 0);
            Place(pipe, parent, at, rotation);
            return pipe;
        }

        /// <summary>
        /// Gives a dressed clone save data the save never holds, then wakes it under `parent`.
        /// docs/invariants.md#an-anomaly-prop-never-enters-the-save
        /// </summary>
        private static void Place(Grabbable item, Transform parent, Vector3 at, Quaternion rotation)
        {
            AddNames();
            GameInternals.PropAccess.ClearId(item);
            GrabbableData data = new(_nextId++, item.name) { parentName = parent.name };
            data.GameObjectData.Position = parent.InverseTransformPoint(at);
            data.GameObjectData.Rotation = Quaternion.Inverse(parent.rotation) * rotation;
            item.Data = data;
            // Awake runs here, with data that is not the save's.
            item.transform.SetParent(parent, false);
            item.transform.SetPositionAndRotation(at, rotation);
            Track(item.gameObject);
        }

        /// <summary>
        /// A blood stain on the floor at `floor`, scrubbed off like the game's own. Null when none can be made.
        /// </summary>
        internal static Cleanable? SpawnBlood(Transform parent, Vector3 floor, float yaw, float scale)
        {
            Cleanable? template = BloodTemplate();
            if (!GameInternals.PropAccess.Ready || template == null)
            {
                WarnOnce("no blood: " + (template == null ? "no blood decal in the scene to copy" : "game internals changed"));
                return null;
            }
            Cleanable blood = Object.Instantiate(template, Holder());
            blood.name = "YB_Blood";
            GameInternals.PropAccess.ClearId(blood);
            blood.Data = new ByteData(_nextId++) { value = (byte)Random.Range(70, 101) };
            blood.OnStateChanged.AddListener(clean =>
            {
                if (clean && blood != null) Object.Destroy(blood.gameObject);
            });
            blood.transform.SetParent(parent, false);
            blood.transform.SetPositionAndRotation(floor + Vector3.up * 0.01f, Quaternion.Euler(0f, yaw, 0f));
            blood.transform.localScale = new Vector3(scale, 1f, scale);
            // A block, not a material, since Cleanable swaps the material as it is scrubbed.
            if (blood.TryGetComponent(out MeshRenderer renderer))
            {
                MaterialPropertyBlock block = new();
                block.SetColor(BaseColorId, BloodTint);
                block.SetColor(ColorId, BloodTint);
                renderer.SetPropertyBlock(block);
            }
            Track(blood.gameObject);
            AddToMess(blood.gameObject);
            return blood;
        }

        /// <summary>
        /// Starts a new mess: the meat and blood spawned from now on go away together, once you have been there
        /// and left. docs/anomalies.md#the-mess-and-the-meat-model
        /// </summary>
        internal static void BeginMess() => Messes.Add(new Mess());

        /// <summary>
        /// `prop` goes as soon as you cannot see it and it is not in your hands.
        /// </summary>
        internal static void GoneWhenUnseen(GameObject prop)
        {
            if (!Fleeting.Contains(prop)) Fleeting.Add(prop);
        }

        private static void AddToMess(GameObject prop)
        {
            if (Messes.Count == 0) BeginMess();

            Messes[^1].Props.Add(prop);
        }

        /// <summary>
        /// From AnomalyDirector.Tick, every MessCheckSeconds. A mess you have seen within MessSeenDist, or
        /// come within MessNearDist of, loses each piece you are MessGoneDist from and cannot see. The meat
        /// in your hands stays.
        /// </summary>
        internal static void Tick()
        {
            if ((Messes.Count == 0 && Fleeting.Count == 0) || Time.time < _messCheckAt) return;

            _messCheckAt = Time.time + MessCheckSeconds;
            Transform? eyes = PlayerView.Camera();
            if (eyes != null) CheckMesses(eyes);
        }

        // Apart from Tick: the lambdas capture `eyes`, and a captured local allocates on every entry.
        private static void CheckMesses(Transform eyes)
        {
            Fleeting.RemoveAll(p =>
            {
                if (p == null) return true;
                if (Seen(p) || (p.TryGetComponent(out Grabbable held) && held.IsGrabbed)) return false;

                YourBuddyPlugin.Log.LogInfo($"[anomaly] {p.name} is gone - you looked away");
                Object.Destroy(p);
                return true;
            });

            for (int m = Messes.Count - 1; m >= 0; m--)
            {
                Mess mess = Messes[m];
                mess.Props.RemoveAll(p => p == null);
                if (mess.Props.Count == 0)
                {
                    Messes.RemoveAt(m);
                    continue;
                }
                if (!mess.Visited)
                {
                    mess.Visited = mess.Props.Exists(p => Near(p, eyes, MessNearDist) || (Near(p, eyes, MessSeenDist) && Seen(p)));
                    continue;
                }
                int gone = mess.Props.RemoveAll(p =>
                {
                    if (Near(p, eyes, MessGoneDist) || Seen(p) || (p.TryGetComponent(out Grabbable held) && held.IsGrabbed)) return false;

                    Object.Destroy(p);
                    return true;
                });
                if (gone > 0) YourBuddyPlugin.Log.LogInfo($"[anomaly] {gone} piece(s) of the mess gone behind you, {mess.Props.Count} left");
            }
        }

        private static bool Near(GameObject prop, Transform eyes, float dist) =>
            (prop.transform.position - eyes.position).sqrMagnitude < dist * dist;

        private static bool Seen(GameObject prop) => PlayerView.Sees(prop.transform.position + Vector3.up * 0.1f, prop.transform);

        /// <summary>
        /// The game's blood decal prefab with mesh, materials, collider and sound. Else a ship's stain of that
        /// name, never DirtBloodSmudge, a faint smear.
        /// </summary>
        private static Cleanable? BloodTemplate()
        {
            if (_bloodTemplate != null) return _bloodTemplate;

            GameObject? prefab = Resources.Load<GameObject>(BloodPrefab);
            if (prefab != null && prefab.TryGetComponent(out Cleanable fromPrefab)) return _bloodTemplate = fromPrefab;

            foreach (Cleanable dirt in Object.FindObjectsOfType<Cleanable>(true))
            {
                if (dirt == null || dirt is SolarPanel || !dirt.name.StartsWith("DirtBlood") || dirt.name.StartsWith("DirtBloodSmudge")) continue;

                _bloodTemplate = dirt;
                break;
            }
            return _bloodTemplate;
        }

        private static void Track(GameObject prop)
        {
            Spawned.RemoveAll(p => p == null);
            Spawned.Add(prop);
            while (Spawned.Count > MaxProps)
            {
                if (Spawned[0] != null) Object.Destroy(Spawned[0]);
                Spawned.RemoveAt(0);
            }
        }

        /// <summary>
        /// No highlight or reflection, since flesh is not glossy. The Skull's URP Lit has both on.
        /// </summary>
        private static void Matte(Material material)
        {
            material.SetFloat("_Smoothness", 0f);
            material.SetFloat("_SpecularHighlights", 0f);
            material.SetFloat("_EnvironmentReflections", 0f);
            material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        }

        /// <summary>
        /// The meat's name and description, which the game looks up by its signature.
        /// </summary>
        private static void AddNames()
        {
            Dictionary<string, string>? texts = SceneLoader.Instance != null ? SceneLoader.Instance.TranslationData : null;
            if (texts == null) return;

            texts.TryAdd("ITEM_" + MeatSignature, "Raw meat");
            texts.TryAdd("DESC_" + MeatSignature, "Still warm. You did not bring this aboard.");
            texts.TryAdd("ITEM_" + PipeSignature, "Bloody pipe");
            texts.TryAdd("DESC_" + PipeSignature, "One end is wet. It was clean when you last saw it.");
        }

        private static void WarnOnce(string what)
        {
            if (_warned) return;

            _warned = true;
            YourBuddyPlugin.Log.LogWarning("[anomaly] The cryo room is left clean - " + what);
        }
    }
}
