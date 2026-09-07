using UnityEngine;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using Random = UnityEngine.Random;

public class GO_ForestGenerator : MonoBehaviour
{
    [Serializable]
    private struct Tree
    {
        public GameObject prefab;
        public Vector3 scale;
    }

    [Serializable]
    private struct Grass
    {
        public GameObject prefab;
        public Vector3 scale;
    }

    // Tracks the data needed to re-derive an animated rotation every frame
    // for a spawned instance, without having to read the transform back
    // (float precision drift) or run a MonoBehaviour per object.
    private struct SwayInstance
    {
        public Transform transform;
        public float baseYaw;   // random yaw chosen at spawn time
        public float phase;     // per-instance time offset so sway is desynced
        public float windScale; // per-instance multiplier
    }

    public Vector2Int area;

    [Title("Ground")]
    public Material groundMaterial;

    [Title("Trees")]
    [SerializeField] private Tree[] _trees;
    public float treesFrequency = 2.0f;

    [Title("Grass")]
    [SerializeField] private Grass[] _grass;
    public float grassFrequency = 0.2f;

    [Title("Containers")]
    [Tooltip("Optional. If left empty, this transform is used as the parent for spawned objects.")]
    [SerializeField] private Transform _groundContainer;
    [SerializeField] private Transform _treeContainer;
    [SerializeField] private Transform _grassContainer;

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

    private GameObject _groundObject;
    private readonly List<GameObject> _spawnedTrees = new List<GameObject>();
    private readonly List<GameObject> _spawnedGrass = new List<GameObject>();

    private readonly List<SwayInstance> _treeSway = new List<SwayInstance>();
    private readonly List<SwayInstance> _grassSway = new List<SwayInstance>();

    void Start()
    {
        BuildForest();
    }

    [Button("Rebuild")]
    void BuildForest()
    {
        ClearSpawned();
        BuildGround();

        var offset = new Vector3(area.x / 2f, 0, area.y / 2f);

        List<Vector2> treePoints = BridsonsFastPDS.Sample(area, treesFrequency);
        List<Vector2> grassPoints = BridsonsFastPDS.Sample(area, grassFrequency);

        Debug.Log("Trees count: " + treePoints.Count);
        Debug.Log("Grass count: " + grassPoints.Count);

        SpawnTrees(treePoints, offset);
        SpawnGrass(grassPoints, offset);
    }

    void BuildGround()
    {
        if (_groundObject == null)
        {
            _groundObject = new GameObject("Ground");
            _groundObject.AddComponent<MeshFilter>();
            _groundObject.AddComponent<MeshRenderer>();
        }

        var groundTransform = _groundObject.transform;
        groundTransform.SetParent(_groundContainer != null ? _groundContainer : transform, false);

        var offset = new Vector3(area.x / 2f, 0, area.y / 2f);
        groundTransform.localPosition = -offset;
        groundTransform.localRotation = Quaternion.identity;
        groundTransform.localScale = Vector3.one;

        _groundObject.GetComponent<MeshFilter>().sharedMesh = CreateGroundMesh();
        _groundObject.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;
        _groundObject.layer = gameObject.layer;
    }

    void SpawnTrees(List<Vector2> points, Vector3 offset)
    {
        if (_trees == null || _trees.Length == 0 || points.Count == 0)
            return;

        var parent = _treeContainer != null ? _treeContainer : transform;
        int part = points.Count / _trees.Length;
        int pIndex = 0;

        for (int i = 0; i < _trees.Length; i++)
        {
            var tree = _trees[i];

            if (tree.prefab == null)
            {
                pIndex += part;
                Debug.LogWarning($"ForestGenerator: tree entry {i} has no prefab assigned, skipping {part} points.");
                continue;
            }

            for (int ti = 0; ti < part; ti++)
            {
                var point = points[pIndex++];
                var position = new Vector3(point.x, 0, point.y) - offset;
                var baseYaw = Random.Range(0, 360f);
                var rotation = Quaternion.Euler(0, baseYaw, 0);
                var scale = tree.scale * Random.Range(0.75f, 1.3f);

                var instance = Instantiate(tree.prefab, parent);
                var instanceTransform = instance.transform;
                instanceTransform.localPosition = position;
                instanceTransform.localRotation = rotation;
                instanceTransform.localScale = scale;
                instance.name = $"{tree.prefab.name}_{ti}";
                instance.layer = gameObject.layer;

                _spawnedTrees.Add(instance);
                _treeSway.Add(new SwayInstance
                {
                    transform = instanceTransform,
                    baseYaw = baseYaw,
                    phase = Random.Range(0f, Mathf.PI * 2f),
                    // slightly bigger/older trees resist wind a bit more
                    windScale = Random.Range(0.7f, 1.15f)
                });
            }
        }
    }

    void SpawnGrass(List<Vector2> points, Vector3 offset)
    {
        if (_grass == null || _grass.Length == 0 || points.Count == 0)
            return;

        var parent = _grassContainer != null ? _grassContainer : transform;
        int part = points.Count / _grass.Length;
        int pIndex = 0;

        for (int i = 0; i < _grass.Length; i++)
        {
            var grass = _grass[i];

            if (grass.prefab == null)
            {
                pIndex += part;
                Debug.LogWarning($"ForestGenerator: grass entry {i} has no prefab assigned, skipping {part} points.");
                continue;
            }

            for (int ti = 0; ti < part; ti++)
            {
                var point = points[pIndex++];
                var position = new Vector3(point.x, 0, point.y) - offset;
                var baseYaw = Random.Range(0, 360f);
                var rotation = Quaternion.Euler(0, baseYaw, 0);

                var instance = Instantiate(grass.prefab, parent);
                var instanceTransform = instance.transform;
                instanceTransform.localPosition = position;
                instanceTransform.localRotation = rotation;
                instanceTransform.localScale = grass.scale;
                instance.name = $"{grass.prefab.name}_{ti}";
                instance.layer = gameObject.layer;

                _spawnedGrass.Add(instance);
                _grassSway.Add(new SwayInstance
                {
                    transform = instanceTransform,
                    baseYaw = baseYaw,
                    phase = Random.Range(0f, Mathf.PI * 2f),
                    windScale = Random.Range(0.85f, 1.3f)
                });
            }
        }
    }

    void Update()
    {
        if (!enableSway)
            return;

        float t = Time.time;
        Vector2 wind = windDirection.sqrMagnitude > 0.0001f ? windDirection.normalized : Vector2.right;

        ApplySway(_treeSway, t, wind, treeSwayAmount, windSpeed * treeSwaySpeedMultiplier);
        ApplySway(_grassSway, t, wind, grassSwayAmount, windSpeed * grassSwaySpeedMultiplier);
    }

    void ApplySway(List<SwayInstance> instances, float t, Vector2 wind, float amount, float speed)
    {
        Vector3 tiltAxis = new Vector3(-wind.y, 0f, wind.x);

        for (int i = 0; i < instances.Count; i++)
        {
            var inst = instances[i];

            if (inst.transform == null)
                continue;

            float baseWave = Mathf.Sin(t * speed + inst.phase);
            float gustWave = gustiness > 0f
                ? Mathf.Sin(t * speed * gustSpeed + inst.phase * 0.5f) * gustiness
                : 0f;

            float sway = (baseWave + gustWave) * amount * windStrength * inst.windScale;

            Quaternion windTilt = Quaternion.AngleAxis(sway, tiltAxis);
            inst.transform.localRotation = windTilt * Quaternion.Euler(0f, inst.baseYaw, 0f);
        }
    }

    [Button("Clear")]
    void ClearSpawned()
    {
        DestroyList(_spawnedTrees);
        DestroyList(_spawnedGrass);
        _treeSway.Clear();
        _grassSway.Clear();
    }

    void DestroyList(List<GameObject> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == null)
                continue;

            if (Application.isPlaying)
                Destroy(list[i]);
            else
                DestroyImmediate(list[i]);
        }
        list.Clear();
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
}