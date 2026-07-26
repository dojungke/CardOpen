using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CardOpen.Prototype
{
    public sealed class PackTearVisual : MonoBehaviour
    {
        public static readonly Quaternion DefaultRotation = Quaternion.identity;
        private MeshFilter[] sourceFilters;
        private MeshRenderer[] sourceRenderers;
        private Transform peelPivot;
        private Transform tornRoot;
        private Transform remainderRoot;
        private Coroutine tiltReturn;
        private bool applyDefaultTilt;

        public void Initialize(Material unusedTearMaterial)
        {
            sourceFilters = GetComponentsInChildren<MeshFilter>(true);
            sourceRenderers = new MeshRenderer[sourceFilters.Length];
            for (int i = 0; i < sourceFilters.Length; i++) sourceRenderers[i] = sourceFilters[i].GetComponent<MeshRenderer>();
        }

        private void LateUpdate()
        {
            if (!applyDefaultTilt) return;
            applyDefaultTilt = false;
            transform.rotation = DefaultRotation;
        }

        public void BeginGesture() { applyDefaultTilt = false; StopTiltReturn(); }

        public void PreviewTilt(Vector2 screenDrag)
        {
            float strength = Mathf.Clamp01(screenDrag.magnitude / 145f);
            Vector2 direction = screenDrag.sqrMagnitude > 0.01f ? screenDrag.normalized : Vector2.zero;
            transform.rotation = Quaternion.Euler(direction.y * 7f * strength, direction.x * 8f * strength, direction.x * -4f * strength) * DefaultRotation;
        }

        public void CancelGesture()
        {
            StopTiltReturn();
            tiltReturn = StartCoroutine(ReturnTilt());
        }

        public IEnumerator PeelInDirection(Vector2 screenDirection, Transform synchronizedCards, Vector3 cardPivot, Vector3 cardOffset)
        {
            StopTiltReturn();
            Vector2 localDirection = new Vector2(screenDirection.x, -screenDirection.y).normalized;
            if (localDirection.sqrMagnitude < 0.1f) localDirection = Vector2.right;
            Vector2 cutDirection = localDirection;
            bool horizontalCut = Mathf.Abs(localDirection.x) >= Mathf.Abs(localDirection.y);
            Vector2 cutOrigin = horizontalCut ? new Vector2(0f, 1.08f) : Vector2.zero;
            BuildSplitObjects(cutDirection, localDirection, cutOrigin);
            SetSourceVisible(false);

            Vector3 peelDirection = new Vector3(localDirection.x, localDirection.y, 0f);
            Quaternion endRotation = Quaternion.Euler(peelDirection.y * 94f, -peelDirection.x * 104f, -peelDirection.x * 36f);
            Quaternion remainderKick = Quaternion.Euler(peelDirection.y * -3f, peelDirection.x * 4f, peelDirection.x * 2f);
            const float duration = 0.52f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float u = Mathf.SmoothStep(0f, 1f, t / duration);
                peelPivot.localPosition = Vector3.Lerp(Vector3.zero, peelDirection * 4.25f + Vector3.back * 0.62f, u);
                peelPivot.localRotation = Quaternion.Slerp(Quaternion.identity, endRotation, u);
                tornRoot.localScale = Vector3.Lerp(Vector3.one, Vector3.one * 0.88f, u);
                remainderRoot.localRotation = Quaternion.Slerp(Quaternion.identity, remainderKick, Mathf.Sin(u * Mathf.PI));
                if (synchronizedCards != null)
                {
                    Quaternion synchronizedRotation = transform.rotation * remainderRoot.localRotation;
                    synchronizedCards.rotation = synchronizedRotation;
                    synchronizedCards.position = cardPivot + cardOffset - synchronizedRotation * cardPivot;
                }
                yield return null;
            }
        }

        public void ResetTear()
        {
            StopTiltReturn();
            SetSourceVisible(true);
            DestroyGeneratedObject(peelPivot);
            DestroyGeneratedObject(remainderRoot);
            peelPivot = null;
            tornRoot = null;
            remainderRoot = null;
            applyDefaultTilt = true;
        }

        private IEnumerator ReturnTilt()
        {
            Quaternion start = transform.rotation;
            const float duration = 0.38f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                transform.rotation = Quaternion.Slerp(start, DefaultRotation, Mathf.SmoothStep(0f, 1f, t / duration));
                yield return null;
            }
            transform.rotation = DefaultRotation;
            tiltReturn = null;
        }

        private void StopTiltReturn()
        {
            if (tiltReturn == null) return;
            StopCoroutine(tiltReturn);
            tiltReturn = null;
        }

        private void BuildSplitObjects(Vector2 cutDirection, Vector2 peelDirection, Vector2 cutOrigin)
        {
            DestroyGeneratedObject(peelPivot);
            DestroyGeneratedObject(remainderRoot);
            Vector2 movingProbe = cutOrigin + new Vector2(-peelDirection.y, peelDirection.x) * 0.5f;
            bool positiveMoves = SignedDistance(movingProbe, cutOrigin, cutDirection) >= 0f;
            peelPivot = new GameObject("Automatic Tear Pivot").transform;
            peelPivot.SetParent(transform, false);
            tornRoot = new GameObject("Detached Foil Piece").transform;
            tornRoot.SetParent(peelPivot, false);
            remainderRoot = new GameObject("Remaining Foil Piece").transform;
            remainderRoot.SetParent(transform, false);

            for (int i = 0; i < sourceFilters.Length; i++)
            {
                MeshFilter filter = sourceFilters[i];
                MeshRenderer renderer = sourceRenderers[i];
                SplitMesh(filter, cutDirection, cutOrigin, out MeshBuilder positive, out MeshBuilder negative);
                MeshBuilder moving = positiveMoves ? positive : negative;
                MeshBuilder remaining = positiveMoves ? negative : positive;
                if (moving.HasTriangles) CreatePiece(filter.name + " Torn", moving.ToMesh(filter.name + " Torn Mesh"), renderer.sharedMaterials, tornRoot);
                if (remaining.HasTriangles) CreatePiece(filter.name + " Remaining", remaining.ToMesh(filter.name + " Remaining Mesh"), renderer.sharedMaterials, remainderRoot);
            }
        }

        private void SplitMesh(MeshFilter filter, Vector2 cutDirection, Vector2 cutOrigin, out MeshBuilder positive, out MeshBuilder negative)
        {
            positive = new MeshBuilder();
            negative = new MeshBuilder();
            Mesh mesh = filter.sharedMesh;
            Vector3[] positions = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector2[] uvs = mesh.uv;
            int[] triangles = mesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                List<CutVertex> polygon = new List<CutVertex>(3);
                for (int corner = 0; corner < 3; corner++)
                {
                    int index = triangles[i + corner];
                    Vector3 position = transform.InverseTransformPoint(filter.transform.TransformPoint(positions[index]));
                    Vector3 normal = transform.InverseTransformDirection(filter.transform.TransformDirection(normals[index])).normalized;
                    polygon.Add(new CutVertex(position, normal, uvs.Length > index ? uvs[index] : Vector2.zero));
                }
                positive.AddPolygon(ClipPolygon(polygon, true, cutDirection, cutOrigin));
                negative.AddPolygon(ClipPolygon(polygon, false, cutDirection, cutOrigin));
            }
        }

        private static List<CutVertex> ClipPolygon(List<CutVertex> input, bool keepPositive, Vector2 cutDirection, Vector2 cutOrigin)
        {
            List<CutVertex> output = new List<CutVertex>(5);
            for (int i = 0; i < input.Count; i++)
            {
                CutVertex current = input[i];
                CutVertex next = input[(i + 1) % input.Count];
                float currentDistance = SignedDistance(new Vector2(current.Position.x, current.Position.y), cutOrigin, cutDirection);
                float nextDistance = SignedDistance(new Vector2(next.Position.x, next.Position.y), cutOrigin, cutDirection);
                bool currentInside = keepPositive ? currentDistance >= -0.0001f : currentDistance <= 0.0001f;
                bool nextInside = keepPositive ? nextDistance >= -0.0001f : nextDistance <= 0.0001f;
                if (currentInside) output.Add(current);
                if (currentInside == nextInside) continue;
                output.Add(CutVertex.Lerp(current, next, currentDistance / (currentDistance - nextDistance)));
            }
            return output;
        }

        private static float SignedDistance(Vector2 point, Vector2 origin, Vector2 direction)
        {
            Vector2 relative = point - origin;
            return direction.x * relative.y - direction.y * relative.x;
        }

        private static void CreatePiece(string objectName, Mesh mesh, Material[] pieceMaterials, Transform parent)
        {
            GameObject piece = new GameObject(objectName);
            piece.transform.SetParent(parent, false);
            piece.AddComponent<MeshFilter>().sharedMesh = mesh;
            piece.AddComponent<MeshRenderer>().sharedMaterials = pieceMaterials;
        }

        private void SetSourceVisible(bool visible)
        {
            for (int i = 0; i < sourceRenderers.Length; i++) if (sourceRenderers[i] != null) sourceRenderers[i].enabled = visible;
        }

        private static void DestroyGeneratedObject(Transform target)
        {
            if (target == null) return;
            target.gameObject.SetActive(false);
            Destroy(target.gameObject);
        }

        private readonly struct CutVertex
        {
            public readonly Vector3 Position;
            public readonly Vector3 Normal;
            public readonly Vector2 UV;
            public CutVertex(Vector3 position, Vector3 normal, Vector2 uv) { Position = position; Normal = normal; UV = uv; }
            public static CutVertex Lerp(CutVertex a, CutVertex b, float amount)
            {
                return new CutVertex(Vector3.Lerp(a.Position, b.Position, amount), Vector3.Lerp(a.Normal, b.Normal, amount).normalized, Vector2.Lerp(a.UV, b.UV, amount));
            }
        }

        private sealed class MeshBuilder
        {
            private readonly List<Vector3> positions = new List<Vector3>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<Vector2> uvs = new List<Vector2>();
            private readonly List<int> triangles = new List<int>();
            public bool HasTriangles => triangles.Count >= 3;
            public void AddPolygon(List<CutVertex> polygon)
            {
                if (polygon.Count < 3) return;
                int start = positions.Count;
                foreach (CutVertex vertex in polygon) { positions.Add(vertex.Position); normals.Add(vertex.Normal); uvs.Add(vertex.UV); }
                for (int i = 1; i < polygon.Count - 1; i++) { triangles.Add(start); triangles.Add(start + i); triangles.Add(start + i + 1); }
            }
            public Mesh ToMesh(string meshName)
            {
                Mesh mesh = new Mesh { name = meshName };
                mesh.SetVertices(positions); mesh.SetNormals(normals); mesh.SetUVs(0, uvs); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
