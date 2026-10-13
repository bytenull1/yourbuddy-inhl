using UnityEngine;
namespace YourBuddy
{
    // Conservative bounds about the same collider centre used by NpcHands. docs/storing.md
    internal static class StorageShape
    {
        internal const int Orientations = 6;
        internal static Vector3 Measure(Grabbable item, Bounds measured)
        {
            Vector3 half = Vector3.zero;
            Quaternion inverse = Quaternion.Inverse(item.transform.rotation);
            foreach (Collider collider in item.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || collider.isTrigger) continue;
                bool local = collider is BoxCollider || collider is MeshCollider { sharedMesh: not null };
                Bounds bounds = collider is BoxCollider box ? new Bounds(box.center, box.size) :
                    collider is MeshCollider mesh && mesh.sharedMesh != null ? mesh.sharedMesh.bounds : collider.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 offset = inverse * ((local ? collider.transform.TransformPoint(corner) : corner) - measured.center);
                    half = Vector3.Max(half, new Vector3(Mathf.Abs(offset.x), Mathf.Abs(offset.y), Mathf.Abs(offset.z)));
                }
            }
            return half;
        }
        internal static Quaternion Rotation(Transform root, int index) => root.rotation * (index switch
        {
            1 => Quaternion.Euler(0, 90, 0), 2 => Quaternion.Euler(90, 0, 0),
            3 => Quaternion.Euler(90, 90, 0), 4 => Quaternion.Euler(0, 0, 90),
            5 => Quaternion.Euler(0, 90, 90), _ => Quaternion.identity
        });
        internal static Vector3 Half(Vector3 half, Quaternion rotation)
        {
            Vector3 x = rotation * new Vector3(half.x, 0, 0);
            Vector3 y = rotation * new Vector3(0, half.y, 0);
            Vector3 z = rotation * new Vector3(0, 0, half.z);
            return new Vector3(Mathf.Abs(x.x)+Mathf.Abs(y.x)+Mathf.Abs(z.x),
                Mathf.Abs(x.y)+Mathf.Abs(y.y)+Mathf.Abs(z.y), Mathf.Abs(x.z)+Mathf.Abs(y.z)+Mathf.Abs(z.z)) + Vector3.one * .01f;
        }
    }
}
