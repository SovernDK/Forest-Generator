using UnityEngine;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using System;
using Random = UnityEngine.Random;
using Debug = UnityEngine.Debug;
using System.Diagnostics;

public class ChunkedForestGenerator : MonoBehaviour
{
    [Serializable]
    private struct ForestObj
    {
        public Mesh mesh;
        public Material[] material;
        public Vector3 scale;
    }

    private struct Chunk
    {
        public Vector3 center;
        public Matrix4x4[][] batches;
    }

    public Vector2Int area = new Vector2Int(100, 100);
    public int chunk = 20;

    [Title("Ground")]
    public Material groundMaterial;
    public float cellSize = 5;

    [Title("Trees")]
    [SerializeField] private ForestObj[] trees;
    public float treeDensity = 1.0f;
    public float treesCullDist = 60.0f;

    [Title("Grass")]
    [SerializeField] private ForestObj[] grasses;
    public float grassDensity = 2;
    public float grassCullDist = 60.0f;
    public float swayAngle = 25.0f;

    [Title("Debug")]
    [SerializeField] bool _frustumCulling = true;

    Chunk[] _chunks;
    Chunk[] _grass;
    Vector3 _cameraPos;
    Mesh _groundMesh; 
    Matrix4x4 _groundMatrix;

    void Start()
    {
        BuildBatch();
    }

    void Update()
    {
        if (Camera.main == null) return;
        _cameraPos = Camera.main.transform.position;

        Graphics.DrawMesh(
            mesh:     _groundMesh,
            matrix:   _groundMatrix,
            material: groundMaterial,
            layer:    gameObject.layer
        );

        Sway(_grass, grassCullDist);

        RenderBatches(_chunks, trees, treesCullDist);
        RenderBatches(_grass, grasses, grassCullDist);
    }

    [Button("Rebuild")]
    void BuildBatch()
    {
        _groundMesh = CreateGroundMesh(cellSize);
        _groundMatrix = transform.localToWorldMatrix;

        int chunkX = Mathf.Max(1, area.x / chunk);
        int chunkY = Mathf.Max(1, area.y / chunk);
        
        _chunks = new Chunk[chunkX * chunkY];
        _grass  = new Chunk[chunkX * chunkY];

        var sw = Stopwatch.StartNew();
        List<Vector2> treeSamples = BridsonsFastPDS.Sample(new Vector2(area.x, area.y), treeDensity);
        List<Vector2> grassSamples = FastJitterSampler.Sample(new Vector2(area.x, area.y), grassDensity);

        List<Vector2>[] treeBuckets = BucketSamplesToChunks(treeSamples, chunkX, chunkY);
        List<Vector2>[] grassBuckets = BucketSamplesToChunks(grassSamples, chunkX, chunkY);

        int chunkIndex = 0;
        for (int cx = 0; cx < chunkX; cx++)
            for (int cy = 0; cy < chunkY; cy++)
            {
                BuildBatch(_chunks, 
                    treeBuckets, 
                    chunkIndex, 
                    cx, 
                    cy, 
                    trees, 
                    treeDensity
                );

                BuildBatch(_grass, 
                    grassBuckets, 
                    chunkIndex, 
                    cx, 
                    cy, 
                    grasses, 
                    grassDensity
                );

                chunkIndex++;
            }

        sw.Stop();
        Debug.Log($"Batch Building time: {sw.Elapsed.TotalMilliseconds} ms");
    }

    List<Vector2>[] BucketSamplesToChunks(List<Vector2> samples, int chunkX, int chunkY)
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
            
            // Map position to grid coordinate
            int cx = Mathf.Clamp(Mathf.FloorToInt(p.x / chunk), 0, chunkX - 1);
            int cy = Mathf.Clamp(Mathf.FloorToInt(p.y / chunk), 0, chunkY - 1);
            
            int chunkIndex = cx * chunkY + cy;
            buckets[chunkIndex].Add(p);
        }

        return buckets;
    }

    void BuildBatch(Chunk[] chunks, List<Vector2>[] buckets, int chunkIndex, int chunkX, int chunkY, ForestObj[] objects, float density = 1.0f)
    {
        Vector3 chunkCenter = transform.position + new Vector3((chunkX + 0.5f) * chunk, 0, (chunkY + 0.5f) * chunk);
        List<Vector2> samples = buckets[chunkIndex];

        chunks[chunkIndex].batches = new Matrix4x4[objects.Length][];
        chunks[chunkIndex].center  = chunkCenter;

        List<List<Matrix4x4>> batchBuilder = new();
        for(int i = 0; i < objects.Length; i++) 
            batchBuilder.Add(new List<Matrix4x4>());

        for (int i = 0; i < samples.Count; i++)
        {
            Vector3 pos = transform.position + new Vector3(samples[i].x, 0, samples[i].y);
            int treeIndex = (int)(Random.value * objects.Length);
            batchBuilder[treeIndex].Add(Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one));
        }

        for (int t = 0; t < objects.Length; t++)
            chunks[chunkIndex].batches[t] = batchBuilder[t].ToArray();
        batchBuilder.Clear();
    }

    void RenderBatches(Chunk[] chunks, ForestObj[] forestObjs, float cullDist)
    {
        if (chunks == null) return;

        float sqrCullDist = cullDist * cullDist;
        for (int i = 0; i < chunks.Length; i++)
        {
            if(_frustumCulling && 
            (_cameraPos - chunks[i].center).sqrMagnitude > chunk * chunk && 
            IsChunkOutsideCamera(Camera.main, chunks[i].center, chunk))
                continue;

            if ((_cameraPos - chunks[i].center).sqrMagnitude > sqrCullDist) 
                continue;

            Matrix4x4[][] batches = chunks[i].batches;

            for(int batchIndex = 0; batchIndex < trees.Length; batchIndex++)
            {
                Matrix4x4[] matrices = batches[batchIndex];
                int total = matrices.Length;
                
                // Split into 1023 draw calls - Limit from unity doc
                for (int offset = 0; offset < total; offset += 1023)
                {
                    int count = Mathf.Min(1023, total - offset);
                    Matrix4x4[] slice = new Matrix4x4[count];
                    Array.Copy(matrices, offset, slice, 0, count);

                    Mesh currMesh = forestObjs[batchIndex].mesh;

                    for(int subMeshInx = 0; subMeshInx < forestObjs[batchIndex].material.Length; subMeshInx++)
                        Graphics.DrawMeshInstanced(currMesh, subMeshInx, forestObjs[batchIndex].material[subMeshInx], slice, count);
                }
            }
        }
    }

    void Sway(Chunk[] chunks, float cullDist)
    {
        float sqrCullDist = cullDist * cullDist;

        // Update only visible chunks
        for (int i = 0; i < chunks.Length; i++)
        {
           if(_frustumCulling && 
            (_cameraPos - chunks[i].center).sqrMagnitude > chunk * chunk && 
            IsChunkOutsideCamera(Camera.main, chunks[i].center, chunk))
                continue;

            if ((_cameraPos - chunks[i].center).sqrMagnitude > sqrCullDist) 
                continue;

            float sway = Mathf.Sin(Time.time);
            for(int ti = 0; ti < chunks[i].batches.Length; ti++)
            {
                for(int bi = 0; bi < chunks[i].batches[ti].Length; bi++)
                {
                    var matrix = chunks[i].batches[ti][bi];

                    Vector3 pos = matrix.GetColumn(3);
                    Quaternion tilt = Quaternion.Euler(sway * swayAngle, 0f, sway * swayAngle);
                    Vector3 scale = matrix.lossyScale;

                    chunks[i].batches[ti][bi] = Matrix4x4.TRS(pos, tilt, scale);
                }
            }
        }
    }

    Mesh CreateGroundMesh(float cellSize = 5f)
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

                float pX = (float) x / vertsPerRow;
                float pY = (float) z / vertsPerCol;
                // float worldY = Noise(pX, pY, octaves, amplitude, frequency);

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
            vert++; // skip the extra vertex at the end of each row
        }

        // If the grid is coarse enough, verts stay under 65535 and we can use 16-bit indices
        // (default). If it's still large, bump to 32-bit indices to avoid an overflow/wrap.
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
        Bounds bounds = new Bounds(
            chunkCenter,
            new Vector3(chunkSize, 4f, chunkSize)
        );

        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(cam);
        return !GeometryUtility.TestPlanesAABB(planes, bounds);
    }

    public bool IsBehindCamera(Camera cam, Vector3 chunkCenter)
    {
        return cam.transform.position.z - chunkCenter.z < 0.0f;
    }
}