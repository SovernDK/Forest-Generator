using UnityEngine;
using UnityEngine.UI;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using Random = UnityEngine.Random;
using System;

public class ForestGenerator : MonoBehaviour
{
    [Serializable]
    private struct Tree
    {
        public Mesh mesh;
        public Material[] mats;
        public Vector3 scale;
    }

    [Serializable]
    private struct Grass
    {
        public Mesh mesh;
        public Material[] mats;
        public Vector3 scale;
    }

    private struct Instance
    {
        public Vector3 position;
        public float baseYaw;     
        public Vector3 scale;
        public float phase;       
        public float windScale;   
    }

    public Vector2Int area;
 
    [Title("Ground")]
    public Material groundMaterial;
    public float cellSize = 5;
    public int octaves = 4;
    public float frequency = .5f;
    public float amplitude = 1.0f;
 
    [Title("Trees")]
    [SerializeField] private Tree[] _trees;
    public float treesFrequency = 2.0f;

    [Title("Grass")]
    [SerializeField] private Grass[] _grass;
    public float grassFrequency = 0.2f;
    public float grassCullDistance = 60f;

    [Title("Wind / Sway")]
    public bool enableSway = true;
    [Tooltip("Direction the wind blows in, XZ plane. Doesn't need to be normalized.")]
    public Vector2 windDirection = new Vector2(1f, 0.3f);
    [Tooltip("Overall wind strength multiplier.")]
    public float windStrength = 1.0f;
    [Tooltip("How fast the base wind cycle oscillates.")]
    public float windSpeed = 0.6f;
    [Tooltip("Adds a slower, larger gust on top of the base sway.")]
    [Range(0f, 1f)] public float gustiness = 0.35f;
    public float gustSpeed = 0.15f;

    [Title("Tree Sway")]
    [Tooltip("Max tilt angle in degrees for trees.")]
    public float treeSwayAmount = 3f;
    public float treeSwaySpeedMultiplier = 1f;

    [Title("Grass Sway")]
    [Tooltip("Max tilt angle in degrees for grass.")]
    public float grassSwayAmount = 10f;
    public float grassSwaySpeedMultiplier = 2.2f;
 
    Mesh groundMesh; 
    Matrix4x4 groundMatrix;

    Matrix4x4[][] _treeBatches;
    Matrix4x4[][] _grassBatches;

    Instance[][] _treeInstances;
    Instance[][] _grassInstances;

    float[][] _heightMap;
    
    void Start()
    {
        BuildBatch();
    }
 
    void OnValidate()
    {
        
    }
 
    [Button("Rebuild")]
    void BuildBatch()
    {
        groundMesh = CreateGroundMesh(cellSize);
        groundMatrix = transform.localToWorldMatrix;

        List<Vector2> points = BridsonsFastPDS.Sample(area, treesFrequency);
        List<Vector2> grasspoints = BridsonsFastPDS.Sample(area, grassFrequency);
 
        Debug.Log("Trees count: " + points.Count);
        Debug.Log("Grass count: " + grasspoints.Count);
 
        var offset = new Vector3(area.x / 2, 0, area.y / 2);
        Vector3 pos = groundMatrix.GetColumn(3);
        pos -= offset;
        groundMatrix.SetColumn(3, new Vector4(pos.x, pos.y, pos.z, 1.0f));

        int cellsX = Mathf.CeilToInt(area.x / cellSize);
        int cellsZ = Mathf.CeilToInt(area.y / cellSize);
        int vertsPerRow = cellsX + 1;

        Vector3[] groundVerts = groundMesh.vertices; 

        _treeBatches = new Matrix4x4[_trees.Length][];
        _treeInstances = new Instance[_trees.Length][];
        var part = points.Count / _trees.Length;
        int pIndex = 0;
        for (int i = 0; i < _trees.Length; i++)
        {
            var batch = new Matrix4x4[part];
            var instances = new Instance[part];
            for(int ti = 0; ti < part; ti++)
            {
                var point = points[pIndex++];

                int gridX = Mathf.Clamp(Mathf.RoundToInt(point.x / cellSize), 0, cellsX);
                int gridZ = Mathf.Clamp(Mathf.RoundToInt(point.y / cellSize), 0, cellsZ);
                int vIndex = gridZ * vertsPerRow + gridX;

                float y = groundVerts[vIndex].y;

                var position = new Vector3(point.x, y, point.y) - offset;
                var baseYaw = Random.Range(0, 360f);
                var scale = _trees[i].scale * Random.Range(0.75f, 1.3f);

                instances[ti] = new Instance
                {
                    position = position,
                    baseYaw = baseYaw,
                    scale = scale,
                    phase = Random.Range(0f, Mathf.PI * 2f),
                    windScale = Random.Range(0.7f, 1.15f)
                };

                var rotation = Quaternion.Euler(0, baseYaw, 0);
                batch[ti] = Matrix4x4.TRS(position, rotation, scale);
            }

            _treeBatches[i] = batch;
            _treeInstances[i] = instances;
        }

        _grassBatches = new Matrix4x4[_grass.Length][];
        _grassInstances = new Instance[_grass.Length][];
        part = grasspoints.Count / _grass.Length;
        pIndex = 0;
        for (int i = 0; i < _grass.Length; i++)
        {
            var batch = new Matrix4x4[part];
            var instances = new Instance[part];
            for(int ti = 0; ti < part; ti++)
            {
                var point = grasspoints[pIndex++];

                int gridX = Mathf.Clamp(Mathf.RoundToInt(point.x / cellSize), 0, cellsX);
                int gridZ = Mathf.Clamp(Mathf.RoundToInt(point.y / cellSize), 0, cellsZ);
                int vIndex = gridZ * vertsPerRow + gridX;
                
                float y = groundVerts[vIndex].y;

                var position = new Vector3(point.x, y, point.y) - offset;
                var baseYaw = Random.Range(0, 360f);
                var scale = _grass[i].scale;

                instances[ti] = new Instance
                {
                    position = position,
                    baseYaw = baseYaw,
                    scale = scale,
                    phase = Random.Range(0f, Mathf.PI * 2f),
                    windScale = Random.Range(0.85f, 1.3f)
                };

                var rotation = Quaternion.Euler(0, baseYaw, 0);
                batch[ti] = Matrix4x4.TRS(position, rotation, scale);
            }

            _grassBatches[i] = batch;
            _grassInstances[i] = instances;
        }
    }

    void Update()
    {
        if (enableSway)
        {
            UpdateSway();
        }

        Graphics.DrawMesh(
            mesh:     groundMesh,
            matrix:   groundMatrix,
            material: groundMaterial,
            layer:    gameObject.layer
        );

        if(_treeBatches == null
            || _grassBatches == null) 
            return;

        for(int bi = 0; bi < _treeBatches.Length; bi++)
        {
            for(int mi = 0; mi < _trees[bi].mats.Length; mi++)
            {
                Graphics.DrawMeshInstanced
                (
                    mesh:         _trees[bi].mesh,
                    submeshIndex: mi,
                    material:     _trees[bi].mats[mi],
                    matrices:     _treeBatches[bi],
                    count:        _treeBatches[bi].Length,
                    properties: null,
                    castShadows:  UnityEngine.Rendering.ShadowCastingMode.On,
                    receiveShadows: true
                );    
            }
        }
 
        for(int bi = 0; bi < _grassBatches.Length; bi++)
        {
            for(int mi = 0; mi < _grass[bi].mats.Length; mi++)
            {
                Graphics.DrawMeshInstanced
                (
                    mesh: _grass[bi].mesh,
                    submeshIndex: mi,
                    material: _grass[bi].mats[mi],
                    matrices: _grassBatches[bi],
                    count: _grassBatches[bi].Length
                );
            }
        }
    }

    void UpdateSway()
    {
        float t = Time.time;
        Vector2 wind = windDirection.sqrMagnitude > 0.0001f ? windDirection.normalized : Vector2.right;

        if (_treeInstances != null)
        {
            for (int bi = 0; bi < _treeInstances.Length; bi++)
                ApplySwayToBatch(_treeInstances[bi], _treeBatches[bi], t, wind,
                    treeSwayAmount, windSpeed * treeSwaySpeedMultiplier);
        }

        if (_grassInstances != null)
        {
            for (int bi = 0; bi < _grassInstances.Length; bi++)
                ApplySwayToBatch(_grassInstances[bi], _grassBatches[bi], t, wind,
                    grassSwayAmount, windSpeed * grassSwaySpeedMultiplier);
        }
    }

    void ApplySwayToBatch(Instance[] instances, Matrix4x4[] matrices, float t, Vector2 wind, float amount, float speed)
    {
        for (int i = 0; i < instances.Length; i++)
        {
            var inst = instances[i];

            float baseWave = Mathf.Sin(t * speed + inst.phase);
            float gustWave = gustiness > 0f
                ? Mathf.Sin(t * speed * gustSpeed + inst.phase * 0.5f) * gustiness
                : 0f;

            float sway = (baseWave + gustWave) * amount * windStrength * inst.windScale;

            // Tilt around the horizontal axis perpendicular to the wind direction,
            // so the mesh leans "downwind" rather than twisting around Y.
            Quaternion windTilt = Quaternion.AngleAxis(sway, new Vector3(-wind.y, 0f, wind.x));
            Quaternion rotation = windTilt * Quaternion.Euler(0f, inst.baseYaw, 0f);

            matrices[i] = Matrix4x4.TRS(inst.position, rotation, inst.scale);
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
                float worldY = Noise(pX, pY, octaves, amplitude, frequency);

                verts[index] = new Vector3(worldX, worldY, worldZ);
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

    float Noise(float x, float y, int octaves, float amplitude, float frequency, float persistance = .5f, float lacunarity = 2.0f)
    {
        float value = 0.0f;

        for(int i = 0; i < octaves; i++)
        {
            value += amplitude * Mathf.PerlinNoise(x * frequency, y * frequency);
            amplitude *= persistance;
            frequency *= lacunarity;
        }

        return value;
    }
}