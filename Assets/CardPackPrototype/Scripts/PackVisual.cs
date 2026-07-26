using System.Collections.Generic;
using UnityEngine;

namespace CardOpen.Prototype
{
    public sealed class PackVisual : MonoBehaviour
    {
        private const float BodyWidth = 1.9f;
        private const float BodyAspect = 1.8f;
        private const float BodyHalfWidth = BodyWidth * 0.5f;
        private const float BodyHalfHeight = BodyWidth * BodyAspect * 0.5f;
        private const float SealHalfWidth = BodyHalfWidth + 0.01f;
        private const float SealInner = BodyHalfHeight - 0.08f;
        private const float SealOuterTip = BodyHalfHeight + 0.22f;
        private const float SealOuterValley = BodyHalfHeight + 0.155f;
        private Vector3 restPosition;
        private static Material hologramMaterial;

        public void Build(Material bodyMaterial, Material artworkMaterial) { Build(bodyMaterial, artworkMaterial, artworkMaterial); }

        public void Build(Material bodyMaterial, Material frontArtworkMaterial, Material backArtworkMaterial)
        {
            restPosition = transform.position;
            MakeGlossy(bodyMaterial);
            MakeGlossy(frontArtworkMaterial);
            MakeGlossy(backArtworkMaterial);
            CreateMeshObject("Tapered Foil Body", BuildFoilBody(), bodyMaterial);
            CreateMeshObject("Pack Front Artwork", BuildFoilArtwork(true), frontArtworkMaterial);
            CreateMeshObject("Pack Back Artwork", BuildFoilArtwork(false), backArtworkMaterial);
            CreateMeshObject("Pack Front Side Artwork", BuildBodySideArtwork(true), frontArtworkMaterial);
            CreateMeshObject("Pack Back Side Artwork", BuildBodySideArtwork(false), backArtworkMaterial);
            CreateMeshObject("Crimped Top Seal", BuildSeal(true), bodyMaterial);
            CreateMeshObject("Crimped Bottom Seal", BuildSeal(false), bodyMaterial);
            CreateMeshObject("Front Top Seal Artwork", BuildSealArtwork(true, true), frontArtworkMaterial);
            CreateMeshObject("Back Top Seal Artwork", BuildSealArtwork(true, false), backArtworkMaterial);
            CreateMeshObject("Front Bottom Seal Artwork", BuildSealArtwork(false, true), frontArtworkMaterial);
            CreateMeshObject("Back Bottom Seal Artwork", BuildSealArtwork(false, false), backArtworkMaterial);
            CreateMeshObject("Front Top Seal Perimeter", BuildSealPerimeterArtwork(true, true), frontArtworkMaterial);
            CreateMeshObject("Back Top Seal Perimeter", BuildSealPerimeterArtwork(true, false), backArtworkMaterial);
            CreateMeshObject("Front Bottom Seal Perimeter", BuildSealPerimeterArtwork(false, true), frontArtworkMaterial);
            CreateMeshObject("Back Bottom Seal Perimeter", BuildSealPerimeterArtwork(false, false), backArtworkMaterial);
        }

        public void SetHolographic(bool enabled)
        {
            Material hologram = GetHologramMaterial();
            if (hologram == null) return;
            MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer renderer = renderers[i];
                if (renderer == null) continue;
                List<Material> rendererMaterials = new List<Material>(renderer.sharedMaterials);
                rendererMaterials.RemoveAll(material => material == hologram);
                if (enabled) rendererMaterials.Add(hologram);
                renderer.sharedMaterials = rendererMaterials.ToArray();
            }
        }

        private static Material GetHologramMaterial()
        {
            if (hologramMaterial != null) return hologramMaterial;
            Shader shader = Shader.Find("CardOpen/Hologram");
            if (shader == null || !shader.isSupported) return null;
            hologramMaterial = new Material(shader) { name = "Card Pack Hologram" };
            hologramMaterial.SetFloat("_Intensity", 0.72f);
            hologramMaterial.renderQueue = 3050;
            return hologramMaterial;
        }
        public void ResetVisual()
        {
            transform.position = restPosition;
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            gameObject.SetActive(true);
        }

        private void CreateMeshObject(string objectName, Mesh mesh, Material material)
        {
            GameObject meshObject = new GameObject(objectName);
            meshObject.transform.SetParent(transform, false);
            meshObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshObject.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static Mesh BuildFoilBody()
        {
            const int xSections = 14;
            const int ySections = 12;
            int columns = xSections + 1;
            int rows = ySections + 1;
            int surfaceCount = columns * rows;
            List<Vector3> vertices = new List<Vector3>(surfaceCount * 2);
            List<Vector2> uvs = new List<Vector2>(surfaceCount * 2);

            for (int side = 0; side < 2; side++)
            {
                float zSign = side == 0 ? -1f : 1f;
                for (int y = 0; y < rows; y++)
                {
                    float v = y / (float)ySections;
                    float vertical = Mathf.Lerp(-BodyHalfHeight, BodyHalfHeight, v);
                    float normalizedY = v * 2f - 1f;
                    float topBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 1f, v));
                    float bottomBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.28f, 0f, v));
                    float endBlend = Mathf.Max(topBlend, bottomBlend);
                    float rowHalfWidth = Mathf.Lerp(BodyHalfWidth, SealHalfWidth, endBlend);
                    for (int x = 0; x < columns; x++)
                    {
                        float u = x / (float)xSections;
                        float normalizedX = u * 2f - 1f;
                        float towardCenter = Mathf.Pow(1f - Mathf.Abs(normalizedX), 0.62f);
                        float verticalRound = 0.82f + 0.18f * (1f - normalizedY * normalizedY);
                        float halfDepth = (0.012f + 0.112f * towardCenter) * verticalRound;
                        halfDepth = Mathf.Lerp(halfDepth, 0.029f, endBlend);
                        vertices.Add(new Vector3(normalizedX * rowHalfWidth, vertical, halfDepth * zSign));
                        uvs.Add(new Vector2(u, v));
                    }
                }
            }

            List<int> triangles = new List<int>();
            AddClosedGridTriangles(triangles, columns, rows, 0, surfaceCount);
            return FinishMesh("Tapered Foil Body Mesh", vertices, uvs, triangles);
        }

        private static Mesh BuildFoilArtwork(bool isFront)
        {
            const int xSections = 14;
            const int ySections = 12;
            int columns = xSections + 1;
            int rows = ySections + 1;
            float zSign = isFront ? -1f : 1f;
            List<Vector3> vertices = new List<Vector3>(columns * rows);
            List<Vector2> uvs = new List<Vector2>(columns * rows);
            List<int> triangles = new List<int>(xSections * ySections * 6);

            for (int y = 0; y < rows; y++)
            {
                float v = y / (float)ySections;
                float vertical = Mathf.Lerp(-BodyHalfHeight, BodyHalfHeight, v);
                float normalizedY = v * 2f - 1f;
                float topBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 1f, v));
                float bottomBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.28f, 0f, v));
                float endBlend = Mathf.Max(topBlend, bottomBlend);
                float rowHalfWidth = Mathf.Lerp(BodyHalfWidth, SealHalfWidth, endBlend);
                for (int x = 0; x < columns; x++)
                {
                    float u = x / (float)xSections;
                    float normalizedX = u * 2f - 1f;
                    float towardCenter = Mathf.Pow(1f - Mathf.Abs(normalizedX), 0.62f);
                    float verticalRound = 0.82f + 0.18f * (1f - normalizedY * normalizedY);
                    float halfDepth = (0.012f + 0.112f * towardCenter) * verticalRound;
                    halfDepth = Mathf.Lerp(halfDepth, 0.029f, endBlend) + 0.0015f;
                    vertices.Add(new Vector3(normalizedX * rowHalfWidth, vertical, halfDepth * zSign));
                    uvs.Add(new Vector2(u, v));
                }
            }

            for (int y = 0; y < ySections; y++)
            {
                for (int x = 0; x < xSections; x++)
                {
                    int a = y * columns + x;
                    int b = a + 1;
                    int c = a + columns;
                    int d = c + 1;
                    if (isFront)
                    {
                        AddTriangle(triangles, a, c, b);
                        AddTriangle(triangles, b, c, d);
                    }
                    else
                    {
                        AddTriangle(triangles, a, b, c);
                        AddTriangle(triangles, b, d, c);
                    }
                }
            }
            return FinishMesh(isFront ? "Pack Front Artwork Mesh" : "Pack Back Artwork Mesh", vertices, uvs, triangles);
        }

        private static Mesh BuildBodySideArtwork(bool isFront)
        {
            const int ySections = 12;
            const int columns = 2;
            int rows = ySections + 1;
            List<Vector3> vertices = new List<Vector3>(rows * columns * 2);
            List<Vector2> uvs = new List<Vector2>(rows * columns * 2);
            List<int> triangles = new List<int>(ySections * 12);

            for (int side = 0; side < 2; side++)
            {
                bool isLeft = side == 0;
                int start = vertices.Count;
                float edgeU = isLeft ? 0f : 1f;
                float xSign = isLeft ? -1f : 1f;
                for (int y = 0; y < rows; y++)
                {
                    float v = y / (float)ySections;
                    float vertical = Mathf.Lerp(-BodyHalfHeight, BodyHalfHeight, v);
                    float normalizedY = v * 2f - 1f;
                    float topBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 1f, v));
                    float bottomBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.28f, 0f, v));
                    float endBlend = Mathf.Max(topBlend, bottomBlend);
                    float rowHalfWidth = Mathf.Lerp(BodyHalfWidth, SealHalfWidth, endBlend) + 0.0015f;
                    float verticalRound = 0.82f + 0.18f * (1f - normalizedY * normalizedY);
                    float halfDepth = 0.012f * verticalRound;
                    halfDepth = Mathf.Lerp(halfDepth, 0.029f, endBlend);
                    float firstZ = isFront ? -halfDepth : 0f;
                    float secondZ = isFront ? 0f : halfDepth;
                    vertices.Add(new Vector3(xSign * rowHalfWidth, vertical, firstZ));
                    vertices.Add(new Vector3(xSign * rowHalfWidth, vertical, secondZ));
                    uvs.Add(new Vector2(edgeU, v));
                    uvs.Add(new Vector2(edgeU, v));
                }

                for (int y = 0; y < ySections; y++)
                {
                    int a = start + y * columns;
                    int b = a + 1;
                    int c = a + columns;
                    int d = c + 1;
                    if (isLeft)
                    {
                        AddTriangle(triangles, a, b, c);
                        AddTriangle(triangles, b, d, c);
                    }
                    else
                    {
                        AddTriangle(triangles, a, c, b);
                        AddTriangle(triangles, b, c, d);
                    }
                }
            }

            return FinishMesh(isFront ? "Pack Front Side Artwork Mesh" : "Pack Back Side Artwork Mesh", vertices, uvs, triangles);
        }

        private static Mesh BuildSeal(bool isTop)
        {
            const int xSections = 24;
            const int ySections = 4;
            int columns = xSections + 1;
            int rows = ySections + 1;
            int surfaceCount = columns * rows;
            float[] ridgeDepths = { 0.029f, 0.054f, 0.031f, 0.049f, 0.023f };
            List<Vector3> vertices = new List<Vector3>(surfaceCount * 2);
            List<Vector2> uvs = new List<Vector2>(surfaceCount * 2);

            for (int side = 0; side < 2; side++)
            {
                float zSign = side == 0 ? -1f : 1f;
                for (int y = 0; y < rows; y++)
                {
                    float v = y / (float)ySections;
                    int ridgeIndex = isTop ? y : ySections - y;
                    for (int x = 0; x < columns; x++)
                    {
                        float u = x / (float)xSections;
                        float normalizedX = u * 2f - 1f;
                        float outer = x % 2 == 0 ? SealOuterTip : SealOuterValley;
                        float vertical = isTop ? Mathf.Lerp(SealInner, outer, v) : Mathf.Lerp(-outer, -SealInner, v);
                        float edgeThin = Mathf.Lerp(0.56f, 1f, Mathf.Pow(1f - Mathf.Abs(normalizedX), 0.45f));
                        vertices.Add(new Vector3(normalizedX * SealHalfWidth, vertical, ridgeDepths[ridgeIndex] * edgeThin * zSign));
                        uvs.Add(new Vector2(u, v));
                    }
                }
            }

            List<int> triangles = new List<int>();
            AddClosedGridTriangles(triangles, columns, rows, 0, surfaceCount);
            return FinishMesh(isTop ? "Crimped Top Seal Mesh" : "Crimped Bottom Seal Mesh", vertices, uvs, triangles);
        }

        private static Mesh BuildSealArtwork(bool isTop, bool isFront)
        {
            const int xSections = 24;
            const int ySections = 4;
            int columns = xSections + 1;
            int rows = ySections + 1;
            float zSign = isFront ? -1f : 1f;
            float[] ridgeDepths = { 0.029f, 0.054f, 0.031f, 0.049f, 0.023f };
            List<Vector3> vertices = new List<Vector3>(columns * rows);
            List<Vector2> uvs = new List<Vector2>(columns * rows);
            List<int> triangles = new List<int>(xSections * ySections * 6);

            for (int y = 0; y < rows; y++)
            {
                float v = y / (float)ySections;
                int ridgeIndex = isTop ? y : ySections - y;
                float textureV = isTop ? Mathf.Lerp(0.90f, 1f, v) : Mathf.Lerp(0f, 0.10f, v);
                for (int x = 0; x < columns; x++)
                {
                    float u = x / (float)xSections;
                    float normalizedX = u * 2f - 1f;
                    float outer = x % 2 == 0 ? SealOuterTip : SealOuterValley;
                    float vertical = isTop ? Mathf.Lerp(SealInner, outer, v) : Mathf.Lerp(-outer, -SealInner, v);
                    float edgeThin = Mathf.Lerp(0.56f, 1f, Mathf.Pow(1f - Mathf.Abs(normalizedX), 0.45f));
                    float depth = ridgeDepths[ridgeIndex] * edgeThin + 0.0015f;
                    vertices.Add(new Vector3(normalizedX * SealHalfWidth, vertical, depth * zSign));
                    uvs.Add(new Vector2(u, textureV));
                }
            }

            for (int y = 0; y < ySections; y++)
            {
                for (int x = 0; x < xSections; x++)
                {
                    int a = y * columns + x;
                    int b = a + 1;
                    int c = a + columns;
                    int d = c + 1;
                    if (isFront)
                    {
                        AddTriangle(triangles, a, c, b);
                        AddTriangle(triangles, b, c, d);
                    }
                    else
                    {
                        AddTriangle(triangles, a, b, c);
                        AddTriangle(triangles, b, d, c);
                    }
                }
            }

            string sideName = isFront ? "Front" : "Back";
            string endName = isTop ? "Top" : "Bottom";
            return FinishMesh(sideName + " " + endName + " Seal Artwork Mesh", vertices, uvs, triangles);
        }

        private static Mesh BuildSealPerimeterArtwork(bool isTop, bool isFront)
        {
            const int xSections = 24;
            const int ySections = 4;
            int rows = ySections + 1;
            float[] ridgeDepths = { 0.029f, 0.054f, 0.031f, 0.049f, 0.023f };
            List<Vector3> vertices = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> triangles = new List<int>();

            for (int side = 0; side < 2; side++)
            {
                bool isLeft = side == 0;
                int start = vertices.Count;
                float edgeU = isLeft ? 0f : 1f;
                float xSign = isLeft ? -1f : 1f;
                for (int y = 0; y < rows; y++)
                {
                    float v = y / (float)ySections;
                    int ridgeIndex = isTop ? y : ySections - y;
                    float vertical = isTop ? Mathf.Lerp(SealInner, SealOuterTip, v) : Mathf.Lerp(-SealOuterTip, -SealInner, v);
                    float textureV = isTop ? Mathf.Lerp(0.90f, 1f, v) : Mathf.Lerp(0f, 0.10f, v);
                    float depth = ridgeDepths[ridgeIndex] * 0.56f;
                    float firstZ = isFront ? -depth : 0f;
                    float secondZ = isFront ? 0f : depth;
                    vertices.Add(new Vector3(xSign * (SealHalfWidth + 0.0015f), vertical, firstZ));
                    vertices.Add(new Vector3(xSign * (SealHalfWidth + 0.0015f), vertical, secondZ));
                    uvs.Add(new Vector2(edgeU, textureV));
                    uvs.Add(new Vector2(edgeU, textureV));
                }

                for (int y = 0; y < ySections; y++)
                {
                    int a = start + y * 2;
                    int b = a + 1;
                    int c = a + 2;
                    int d = c + 1;
                    if (isLeft)
                    {
                        AddTriangle(triangles, a, b, c);
                        AddTriangle(triangles, b, d, c);
                    }
                    else
                    {
                        AddTriangle(triangles, a, c, b);
                        AddTriangle(triangles, b, c, d);
                    }
                }
            }

            int outerStart = vertices.Count;
            for (int x = 0; x <= xSections; x++)
            {
                float u = x / (float)xSections;
                float normalizedX = u * 2f - 1f;
                float outer = x % 2 == 0 ? SealOuterTip : SealOuterValley;
                float vertical = (isTop ? outer : -outer) + (isTop ? 0.0015f : -0.0015f);
                float edgeThin = Mathf.Lerp(0.56f, 1f, Mathf.Pow(1f - Mathf.Abs(normalizedX), 0.45f));
                float depth = ridgeDepths[ySections] * edgeThin;
                float firstZ = isFront ? -depth : 0f;
                float secondZ = isFront ? 0f : depth;
                vertices.Add(new Vector3(normalizedX * SealHalfWidth, vertical, firstZ));
                vertices.Add(new Vector3(normalizedX * SealHalfWidth, vertical, secondZ));
                uvs.Add(new Vector2(u, isTop ? 1f : 0f));
                uvs.Add(new Vector2(u, isTop ? 1f : 0f));
            }

            for (int x = 0; x < xSections; x++)
            {
                int a = outerStart + x * 2;
                int b = a + 1;
                int c = a + 2;
                int d = c + 1;
                if (isTop)
                {
                    AddTriangle(triangles, a, b, c);
                    AddTriangle(triangles, b, d, c);
                }
                else
                {
                    AddTriangle(triangles, a, c, b);
                    AddTriangle(triangles, b, c, d);
                }
            }

            string sideName = isFront ? "Front" : "Back";
            string endName = isTop ? "Top" : "Bottom";
            return FinishMesh(sideName + " " + endName + " Seal Perimeter Mesh", vertices, uvs, triangles);
        }

        private static void AddClosedGridTriangles(List<int> triangles, int columns, int rows, int front, int back)
        {
            for (int y = 0; y < rows - 1; y++)
            {
                for (int x = 0; x < columns - 1; x++)
                {
                    int a = y * columns + x;
                    int b = a + 1;
                    int c = a + columns;
                    int d = c + 1;
                    AddTriangle(triangles, front + a, front + c, front + b);
                    AddTriangle(triangles, front + b, front + c, front + d);
                    AddTriangle(triangles, back + a, back + b, back + c);
                    AddTriangle(triangles, back + b, back + d, back + c);
                }
            }

            for (int y = 0; y < rows - 1; y++)
            {
                int ll = y * columns;
                int ul = (y + 1) * columns;
                AddTriangle(triangles, front + ll, back + ll, front + ul);
                AddTriangle(triangles, back + ll, back + ul, front + ul);
                int lr = y * columns + columns - 1;
                int ur = (y + 1) * columns + columns - 1;
                AddTriangle(triangles, front + lr, front + ur, back + lr);
                AddTriangle(triangles, back + lr, front + ur, back + ur);
            }

            for (int x = 0; x < columns - 1; x++)
            {
                AddTriangle(triangles, front + x, front + x + 1, back + x);
                AddTriangle(triangles, front + x + 1, back + x + 1, back + x);
                int tl = (rows - 1) * columns + x;
                int tr = tl + 1;
                AddTriangle(triangles, front + tl, back + tl, front + tr);
                AddTriangle(triangles, front + tr, back + tl, back + tr);
            }
        }

        private static void AddTriangle(List<int> triangles, int a, int b, int c)
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }

        private static Mesh FinishMesh(string meshName, List<Vector3> vertices, List<Vector2> uvs, List<int> triangles)
        {
            Mesh mesh = new Mesh { name = meshName };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            // Keep CPU data readable because the tear system splits these meshes at runtime.
            mesh.UploadMeshData(false);
            return mesh;
        }

        private static void MakeGlossy(Material material)
        {
            if (material == null) return;
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.91f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.16f);
            if (material.HasProperty("_SpecularHighlights")) material.SetFloat("_SpecularHighlights", 1f);
            if (material.HasProperty("_EnvironmentReflections")) material.SetFloat("_EnvironmentReflections", 1f);
        }
    }
}
