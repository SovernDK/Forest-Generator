using System.Collections.Generic;
using UnityEngine;

using Random = UnityEngine.Random;

public static class BridsonsFastPDS
{
    public static List<Vector2> Sample(Vector2 sampleArea, float minDist = 1.0f, int maxAttempts = 30)
    {
        if (sampleArea.x <= 0 || sampleArea.y <= 0)
            return new List<Vector2>();

        if (minDist <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(minDist));

        if (maxAttempts <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(maxAttempts));

        // Grid cell size.
        float cellSize = minDist / Mathf.Sqrt(2f);

        int gridW = Mathf.CeilToInt(sampleArea.x / cellSize);
        int gridH = Mathf.CeilToInt(sampleArea.y / cellSize);

        int[,] grid = new int[gridW, gridH];

        for (int x = 0; x < gridW; x++)
            for (int y = 0; y < gridH; y++)
            {
                grid[x, y] = -1;
            }

        List<Vector2> samples = new List<Vector2>();
        List<Vector2> activeList = new List<Vector2>();

        Vector2 firstPoint = new Vector2(
            sampleArea.x / 2.0f,
            sampleArea.y / 2.0f
        );

        SetSample(
            firstPoint,
            activeList,
            samples,
            grid,
            cellSize
        );

        while (activeList.Count > 0)
        {
            int randIndex = Random.Range(0, activeList.Count);
            Vector2 center = activeList[randIndex];

            bool found = false;

            for (int i = 0; i < maxAttempts; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float distance = Random.Range(minDist, 2f * minDist);

                Vector2 offset = new Vector2(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle)
                ) * distance;

                Vector2 candidate = center + offset;

                if (candidate.x < 0f || candidate.x >= sampleArea.x ||
                    candidate.y < 0f || candidate.y >= sampleArea.y)
                {
                    continue;
                }

                if (!IsValid(
                    candidate,
                    gridW,
                    gridH,
                    cellSize,
                    minDist,
                    grid,
                    samples))
                {
                    continue;
                }

                SetSample(
                    candidate,
                    activeList,
                    samples,
                    grid,
                    cellSize
                );

                found = true;
                break;
            }

            // Couldn't find another point around this active point.
            if (!found)
                activeList.RemoveAt(randIndex);

        }

        return samples;
    }

    private static void SetSample(
        Vector2 p,
        List<Vector2> activeList,
        List<Vector2> samples,
        int[,] grid,
        float cellSize)
    {
        int index = samples.Count;

        samples.Add(p);
        activeList.Add(p);

        int cellX = Mathf.FloorToInt(p.x / cellSize);
        int cellY = Mathf.FloorToInt(p.y / cellSize);

        grid[cellX, cellY] = index;
    }

    private static bool IsValid(
        Vector2 p,
        int gridW,
        int gridH,
        float cellSize,
        float minDist,
        int[,] grid,
        List<Vector2> samples)
    {
        int cellX = Mathf.FloorToInt(p.x / cellSize);
        int cellY = Mathf.FloorToInt(p.y / cellSize);

        for (int x = -2; x <= 2; x++)
        {
            for (int y = -2; y <= 2; y++)
            {
                int currX = cellX + x;
                int currY = cellY + y;

                // Ignore cells outside the grid.
                if (currX < 0 || currX >= gridW ||
                    currY < 0 || currY >= gridH)
                {
                    continue;
                }

                int pointIndex = grid[currX, currY];

                if (pointIndex == -1)
                    continue;

                float sqrDist =
                    (p - samples[pointIndex]).sqrMagnitude;

                // Candidate is invalid if it's too close.
                if (sqrDist < minDist * minDist)
                    return false;
            }
        }

        return true;
    }
}