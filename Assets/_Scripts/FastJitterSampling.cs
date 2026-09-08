using System.Collections.Generic;
using UnityEngine;

using Random = UnityEngine.Random;

public static class FastJitterSampler
{
    public static List<Vector2> Sample(Vector2 area, float spacing, float jitterStrength = 0.8f)
    {
        int cols = Mathf.CeilToInt(area.x / spacing);
        int rows = Mathf.CeilToInt(area.y / spacing);
        
        List<Vector2> points = new List<Vector2>(cols * rows);

        for (int x = 0; x < cols; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                // Center point of current grid cell
                float cellCenterX = (x + 0.5f) * spacing;
                float cellCenterY = (y + 0.5f) * spacing;

                // Random displacement scaled by spacing and jitter strength
                float offsetX = (Random.value - 0.5f) * spacing * jitterStrength;
                float offsetY = (Random.value - 0.5f) * spacing * jitterStrength;

                float px = cellCenterX + offsetX;
                float py = cellCenterY + offsetY;

                // Ensure point stays within bounds
                if (px >= 0f && px < area.x && py >= 0f && py < area.y)
                {
                    points.Add(new Vector2(px, py));
                }
            }
        }

        return points;
    }
}