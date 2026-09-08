using UnityEngine;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using System;
using Random = UnityEngine.Random;
using Debug = UnityEngine.Debug;
using System.Diagnostics;

public class GO_ForestGenerator : MonoBehaviour
{
    private class Chunk
    {
        public Vector3 center;
        public GameObject rootObject;
        public List<Transform> instanceTransforms = new List<Transform>();
    }

    public Vector2Int area = new Vector2Int(100, 100);
    public int chunk = 20;

    [Title("Ground")]
    public Material groundMaterial;
    public float cellSize = 5;

    [Title("Trees")]
    [SerializeField] private GameObject[] treePrefabs;
    public float treeDensity = 1.0f;
    public float treesCullDist = 60.0f;

    [Title("Grass")]
    [SerializeField] private GameObject[] grassPrefabs;
    public float grassDensity = 2;
    public float grassCullDist = 60.0f;
    public float swayAngle = 25.0f;

    [Title("Debug")]
    [SerializeField] private bool _frustumCulling = true;

    private Chunk[] _chunks;
    private Chunk[] _grassChunks;
    private Vector3 _cameraPos;

    private MeshFilter _groundFilter;
    private MeshRenderer _groundRenderer;

    private void Start()
    {
        BuildBatch();
    }

    private void Update()
    {
        if (Camera.main == null) return;
        _cameraPos = Camera.main.transform.position;

        // Manage chunk visibility based on distance and frustum
        UpdateChunkVisibility(_chunks, treesCullDist);
        UpdateChunkVisibility(_grassChunks, grassCullDist);

        SwayGrass();
    }

    [Button("Rebuild")]
    public void BuildBatch()
    {
        ClearExisting();
        SetupGround();

        int chunkX = Mathf.Max(1, area.x / chunk);
        int chunkY = Mathf.Max(1, area.y / chunk);

        _chunks = new Chunk[chunkX * chunkY];
        _grassChunks = new Chunk[chunkX * chunkY];

        var sw = Stopwatch.StartNew();

        List<Vector2> treeSamples = BridsonsFastPDS.Sample(new Vector2(area.x, area.y), treeDensity);
        List<Vector2> grassSamples = FastJitterSampler.Sample(new Vector2(area.x, area.y), grassDensity);

        List<Vector2>[] treeBuckets = BucketSamplesToChunks(treeSamples, chunkX, chunkY);
        List<Vector2>[] grassBuckets = BucketSamplesToChunks(grassSamples, chunkX, chunkY);

        Transform treesParent = new GameObject("Trees_Container").transform;
        treesParent.SetParent(transform, false);

        Transform grassParent = new GameObject("Grass_Container").transform;
        grassParent.SetParent(transform, false);

        int chunkIndex = 0;
        for (int cx = 0; cx < chunkX; cx++)
        {
            for (int cy = 0; cy < chunkY; cy++)
            {
                _chunks[chunkIndex] = BuildChunkBucket(
                    treeBuckets[chunkIndex], cx, cy, treePrefabs, treesParent, "TreeChunk"
                );

                _grassChunks[chunkIndex] = BuildChunkBucket(
                    grassBuckets[chunkIndex], cx, cy, grassPrefabs, grassParent, "GrassChunk"
                );

                chunkIndex++;
            }
        }

        sw.Stop();
        Debug.Log($"Prefab Forest generation time: {sw.Elapsed.TotalMilliseconds} ms");
    }

    private void ClearExisting()
    {
        // Destroy existing child containers in runtime or edit mode
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            if (Application.isPlaying)
                Destroy(child);
            else
                DestroyImmediate(child);
        }
    }

    private void SetupGround()
    {
        if (!_groundFilter) _groundFilter = gameObject.GetComponent<MeshFilter>() ?? gameObject.AddComponent<MeshFilter>();
        if (!_groundRenderer) _groundRenderer = gameObject.GetComponent<MeshRenderer>() ?? gameObject.AddComponent<MeshRenderer>();

        _groundFilter.sharedMesh = CreateGroundMesh(cellSize);
        _groundRenderer.sharedMaterial = groundMaterial;
    }

    private List<Vector2>[] BucketSamplesToChunks(List<Vector2> samples, int chunkX, int chunkY)
    {
        int totalChunks = chunkX * chunkY;
        List<Vector2>[] buckets = new List<Vector2>[totalChunks];
        for (int i = 0; i < totalChunks; i++)
        {
            buckets[i] = new List<Vector2>();
        }

        for (int i = 0; i < samples.Count; i++)
        {
            Vector2 p = samples[i];
            int cx = Mathf.Clamp(Mathf.FloorToInt(p.x / chunk), 0, chunkX - 1);
            int cy = Mathf.Clamp(Mathf.FloorToInt(p.y / chunk), 0, chunkY - 1);

            int chunkIndex = cx * chunkY + cy;
            buckets[chunkIndex].Add(p);
        }

        return buckets;
    }

    private Chunk BuildChunkBucket(List<Vector2> samples, int cx, int cy, GameObject[] prefabs, Transform parent, string prefix)
    {
        Vector3 chunkCenter = transform.position + new Vector3((cx + 0.5f) * chunk, 0, (cy + 0.5f) * chunk);
        GameObject chunkObj = new GameObject($"{prefix}_{cx}_{cy}");
        chunkObj.transform.SetParent(parent, false);

        Chunk newChunk = new Chunk
        {
            center = chunkCenter,
            rootObject = chunkObj
        };

        if (prefabs == null || prefabs.Length == 0) return newChunk;

        for (int i = 0; i < samples.Count; i++)
        {
            Vector3 pos = transform.position + new Vector3(samples[i].x, 0, samples[i].y);
            GameObject prefab = prefabs[Random.Range(0, prefabs.Length)];

            if (prefab == null) continue;

            GameObject instance = Instantiate(prefab, pos, Quaternion.identity, chunkObj.transform);
            newChunk.instanceTransforms.Add(instance.transform);
        }

        return newChunk;
    }

    private void UpdateChunkVisibility(Chunk[] chunks, float cullDist)
    {
        if (chunks == null) return;
        float sqrCullDist = cullDist * cullDist;

        for (int i = 0; i < chunks.Length; i++)
        {
            if (chunks[i] == null || chunks[i].rootObject == null) continue;

            float sqrDistToCam = (_cameraPos - chunks[i].center).sqrMagnitude;
            bool isFar = sqrDistToCam > sqrCullDist;
            bool isOutsideFrustum = _frustumCulling && 
                                    sqrDistToCam > chunk * chunk && 
                                    IsChunkOutsideCamera(Camera.main, chunks[i].center, chunk);

            bool shouldBeActive = !isFar && !isOutsideFrustum;

            if (chunks[i].rootObject.activeSelf != shouldBeActive)
            {
                chunks[i].rootObject.SetActive(shouldBeActive);
            }
        }
    }

    private void SwayGrass()
    {
        if (_grassChunks == null) return;

        float sway = Mathf.Sin(Time.time);
        Quaternion tilt = Quaternion.Euler(sway * swayAngle, 0f, sway * swayAngle);

        for (int i = 0; i < _grassChunks.Length; i++)
        {
            // Only update sway on visible grass chunks
            if (_grassChunks[i] == null || !_grassChunks[i].rootObject.activeInHierarchy) continue;

            for (int t = 0; t < _grassChunks[i].instanceTransforms.Count; t++)
            {
                Transform grassTrans = _grassChunks[i].instanceTransforms[t];
                if (grassTrans != null)
                {
                    grassTrans.localRotation = tilt;
                }
            }
        }
    }

    private Mesh CreateGroundMesh(float cellSize = 5f)
    {
        Mesh mesh = new Mesh();
        int cellsX = Mathf.CeilToInt(area.x / cellSize);
        int cellsZ = Mathf.CeilToInt(area.y / cellSize);

        int vertsPerRow = cellsX + 1;
        int vertsPerCol = cellsZ + 1;

        Vector3[] verts = new Vector3[vertsPerRow * vertsPerCol];
        Vector2[] uvs = new Vector2[verts.Length];

        int index = 0;
        for (int z = 0; z < vertsPerCol; z++)
        {
            for (int x = 0; x < vertsPerRow; x++)
            {
                float worldX = Mathf.Min(x * cellSize, area.x);
                float worldZ = Mathf.Min(z * cellSize, area.y);

                verts[index] = new Vector3(worldX, 0, worldZ);
                uvs[index] = new Vector2(worldX / area.x, worldZ / area.y);
                index++;
            }
        }

        int[] triangles = new int[cellsX * cellsZ * 6];
        int tris = 0, vert = 0;

        for (int z = 0; z < cellsZ; z++)
        {
            for (int x = 0; x < cellsX; x++)
            {
                triangles[tris + 0] = vert + 0;
                triangles[tris + 1] = vert + vertsPerRow;
                triangles[tris + 2] = vert + 1;
                triangles[tris + 3] = vert + 1;
                triangles[tris + 4] = vert + vertsPerRow;
                triangles[tris + 5] = vert + vertsPerRow + 1;

                vert++;
                tris += 6;
            }
            vert++;
        }

        if (verts.Length > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    public bool IsChunkOutsideCamera(Camera cam, Vector3 chunkCenter, float chunkSize)
    {
        Bounds bounds = new Bounds(chunkCenter, new Vector3(chunkSize, 4f, chunkSize));
        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(cam);
        return !GeometryUtility.TestPlanesAABB(planes, bounds);
    }
}