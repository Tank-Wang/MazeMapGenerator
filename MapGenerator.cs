/*
 * MapGenerator.cs - Unity Procedural Maze Generator
 * 
 * MIT License
 * 
 * Copyright (c) 2025 Xiaotian (Tank) Wang
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 * 
 * Version: 1.0
 * Date: March 13, 2025
 * 
 * Description:
 * This procedural maze generator creates playable maze levels with:
 * - Dynamic layouts using depth-first search algorithm
 * - Customizable terrain sets with different visual styles
 * - Collectible items, breakable walls, and decorative elements
 * - Intelligent enemy path generation
 */

using System.Collections.Generic;
using System.Linq;
using StarterAssets;
using UnityEngine;

#region Terrain Assets Class
// Class to store assets for a single terrain type
[System.Serializable]
public class TerrainAssets
{
    public List<GameObject> floorPrefabs;           // List of floor prefabs
    public List<GameObject> northWallPrefabs;       // List of north wall prefabs
    public List<GameObject> southWallPrefabs;       // List of south wall prefabs
    public List<GameObject> eastWallPrefabs;        // List of east wall prefabs
    public List<GameObject> westWallPrefabs;        // List of west wall prefabs
    public List<GameObject> decorationPrefabs;      // List of decoration prefabs
}
#endregion

public class MapGenerator : MonoBehaviour
{
    #region Inspector Fields
    [Header("Maze Settings")]
    public int width = 10;                        // Maze width in cells
    public int height = 10;                       // Maze height in cells
    public float cellSize = 1f;                   // Size of each cell

    [Header("Terrain Assets")]
    public TerrainAssets[] terrainAssets;         // Array of terrain assets (should contain 3 elements)
    
    [Header("Collectibles & Decorations")]
    public List<GameObject> collectiblePrefabs;   // List of collectible prefabs
    [Range(0f, 1f)]
    public float collectibleSpawnChance = 0.2f;   // Chance to spawn a collectible in a cell
    [Range(0f, 1f)]
    public float decorationSpawnChance = 0.1f;    // Chance to spawn decoration in a cell

    [Header("Wall Settings")]
    [Range(0f, 1f)]
    public float breakableWallChance = 0.3f;      // Chance for a wall (if not outermost) to be breakable
    public float wallThickness = 0.1f;            // Thickness for each wall so they fit in cell

    [Header("Enemy Path Settings")]
    [Range(0f, 1f)]
    public float enemyPathSpawnChance = 0.1f;     // Chance to generate an enemy path from a cell
    public bool debugDrawPaths = true;            // Toggle for debugging enemy paths

    [Header("Prefabs")]
    public GameObject enemyPrefab;                // Enemy prefab to be spawned at path start
    public GameObject playerPrefab;               // Player prefab to be spawned at start cell (SW corner)
    public GameObject endpointPrefab;             // Endpoint prefab to be spawned at NE corner (triggers win)
    #endregion

    #region Internal Variables
    // Internal data
    private TerrainAssets _selectedTerrainAssets;  // Randomly selected terrain assets for the current maze
    private MazeCell[,] _maze;                     // Maze grid data
    private List<List<Vector3>> _enemyPaths;       // Enemy paths stored as lists of world positions (waypoints)
    private HashSet<Vector2Int> _usedPathCells;    // Tracks which grid cells are used by enemy paths
    private bool[,] _collectibleInCell;            // Tracks if a cell already has a collectible
    private Vector2Int _playerSpawnCell = new Vector2Int(0, 0); // Player spawn location (SW corner)
    #endregion

    #region Unity Lifecycle Methods
    private void Awake()
    {
        if (terrainAssets != null && terrainAssets.Length > 0)
        {
            // Randomly select one set of terrain assets
            _selectedTerrainAssets = terrainAssets[Random.Range(0, terrainAssets.Length)];
            // Try to set cellSize based on the north wall prefab's length
            if (_selectedTerrainAssets.northWallPrefabs != null && _selectedTerrainAssets.northWallPrefabs.Count > 0)
            {
                GameObject sampleWall = _selectedTerrainAssets.northWallPrefabs[0];
                MeshFilter mf = sampleWall.GetComponent<MeshFilter>();
                if (mf != null)
                {
                    cellSize = mf.sharedMesh.bounds.size.x * sampleWall.transform.localScale.x;
                    Debug.Log("Cell size set to " + cellSize + " based on sample wall prefab.");
                }
            }
        }
        else
        {
            Debug.LogError("No terrain assets assigned!");
        }
    }
    
    private void Start()
    {
        // Initialize collectible tracker array
        _collectibleInCell = new bool[width, height];

        GenerateMaze();         // Create the maze data
        DrawMaze();             // Instantiate floors, walls, collectibles, and waypoints
        SpawnPlayerAndEndpoint(); // Spawn the player and endpoint at designated cells
        GenerateEnemyPaths();   // Generate enemy wandering paths
        SpawnEnemies();         // Spawn an enemy at the start point of each enemy path
        SpawnDecorations();     // Spawn decorations on cell edges (after walls are instantiated)
    }
    #endregion

    #region Maze Generation
    /// <summary>
    /// Generate the maze using a Depth-First Search (DFS) algorithm.
    /// </summary>
    private void GenerateMaze()
    {
        _maze = new MazeCell[width, height];
        // Initialize each cell in the maze
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                _maze[x, y] = new MazeCell();

        Stack<Vector2Int> stack = new Stack<Vector2Int>();
        Vector2Int current = new Vector2Int(0, 0);
        _maze[0, 0].Visited = true;

        // DFS loop: visit unvisited neighbors and remove walls accordingly
        do
        {
            List<Vector2Int> unvisitedNeighbours = new List<Vector2Int>();

            // Check North
            if (current.y < height - 1 && !_maze[current.x, current.y + 1].Visited)
                unvisitedNeighbours.Add(new Vector2Int(current.x, current.y + 1));
            // Check South
            if (current.y > 0 && !_maze[current.x, current.y - 1].Visited)
                unvisitedNeighbours.Add(new Vector2Int(current.x, current.y - 1));
            // Check East
            if (current.x < width - 1 && !_maze[current.x + 1, current.y].Visited)
                unvisitedNeighbours.Add(new Vector2Int(current.x + 1, current.y));
            // Check West
            if (current.x > 0 && !_maze[current.x - 1, current.y].Visited)
                unvisitedNeighbours.Add(new Vector2Int(current.x - 1, current.y));

            if (unvisitedNeighbours.Count > 0)
            {
                // Choose a random unvisited neighbor
                Vector2Int chosen = unvisitedNeighbours[Random.Range(0, unvisitedNeighbours.Count)];
                stack.Push(current);
                RemoveWall(current, chosen);  // Remove the wall between current and chosen cell
                _maze[chosen.x, chosen.y].Visited = true;
                current = chosen;
            }
            else if (stack.Count > 0)
            {
                // Backtrack if no unvisited neighbors
                current = stack.Pop();
            }
        } while (stack.Count > 0);
    }

    /// <summary>
    /// Remove the wall between two adjacent cells based on their relative positions.
    /// </summary>
    private void RemoveWall(Vector2Int current, Vector2Int chosen)
    {
        int dx = chosen.x - current.x;
        int dy = chosen.y - current.y;

        if (dx == 1)
        {
            _maze[current.x, current.y].HasEastWall = false;
            _maze[chosen.x, chosen.y].HasWestWall = false;
        }
        else if (dx == -1)
        {
            _maze[current.x, current.y].HasWestWall = false;
            _maze[chosen.x, chosen.y].HasEastWall = false;
        }
        else if (dy == 1)
        {
            _maze[current.x, current.y].HasNorthWall = false;
            _maze[chosen.x, chosen.y].HasSouthWall = false;
        }
        else if (dy == -1)
        {
            _maze[current.x, current.y].HasSouthWall = false;
            _maze[chosen.x, chosen.y].HasNorthWall = false;
        }
    }

    /// <summary>
    /// Check if the path between two adjacent cells is clear (i.e., no wall exists between them).
    /// </summary>
    private bool IsPathClear(Vector2Int from, Vector2Int to)
    {
        int dx = to.x - from.x;
        int dy = to.y - from.y;
        if (dx == 1 && dy == 0)
            return !_maze[from.x, from.y].HasEastWall && !_maze[to.x, to.y].HasWestWall;
        else if (dx == -1 && dy == 0)
            return !_maze[from.x, from.y].HasWestWall && !_maze[to.x, to.y].HasEastWall;
        else if (dx == 0 && dy == 1)
            return !_maze[from.x, from.y].HasNorthWall && !_maze[to.x, to.y].HasSouthWall;
        else if (dx == 0 && dy == -1)
            return !_maze[from.x, from.y].HasSouthWall && !_maze[to.x, to.y].HasNorthWall;
        return false;
    }
    #endregion

    #region Maze Drawing
    /// <summary>
    /// Instantiate floors, walls, collectibles, and waypoints based on the maze data.
    /// </summary>
    private void DrawMaze()
    {
        int waypointCounter = 1;
        // Loop over each cell in the grid
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                // Calculate cell center position
                Vector3 cellCenter = new Vector3(x * cellSize, 0, y * cellSize);
                MazeCell cell = _maze[x, y];

                // Instantiate the floor prefab and scale it to exactly fill the cell.
                if (_selectedTerrainAssets.floorPrefabs != null && _selectedTerrainAssets.floorPrefabs.Count > 0)
                {
                    GameObject floorToSpawn = _selectedTerrainAssets.floorPrefabs[Random.Range(0, _selectedTerrainAssets.floorPrefabs.Count)];
                    GameObject floor = Instantiate(floorToSpawn, cellCenter, Quaternion.identity, transform);
                    floor.transform.localScale = new Vector3(cellSize, floor.transform.localScale.y, cellSize);
                }

                // Instantiate collectible (if chance passes) at cell center with a small vertical offset.
                if (collectiblePrefabs != null && collectiblePrefabs.Count > 0 && Random.value < collectibleSpawnChance)
                {
                    GameObject collectibleToSpawn = collectiblePrefabs[Random.Range(0, collectiblePrefabs.Count)];
                    Vector3 collectiblePos = cellCenter + new Vector3(0, 0.5f, 0);
                    GameObject collectible = Instantiate(collectibleToSpawn, collectiblePos, Quaternion.identity, transform);
                    collectible.transform.localScale = new Vector3(cellSize / 4, cellSize / 4, cellSize / 4);
                    collectible.AddComponent<Collectible>();
                    MeshCollider meshCollider = collectible.AddComponent<MeshCollider>();
                    meshCollider.convex = true;
                    meshCollider.isTrigger = true;
                    _collectibleInCell[x, y] = true; // Mark cell as having a collectible
                }

                // Instantiate walls. Each wall is resized to match cellSize and wallThickness.
                // North wall
                if (cell.HasNorthWall && _selectedTerrainAssets.northWallPrefabs != null && _selectedTerrainAssets.northWallPrefabs.Count > 0)
                {
                    GameObject northWall = _selectedTerrainAssets.northWallPrefabs[Random.Range(0, _selectedTerrainAssets.northWallPrefabs.Count)];
                    Vector3 pos = cellCenter + new Vector3(0, 0, cellSize / 2);
                    GameObject instantiatedNorthWall = Instantiate(northWall, pos, Quaternion.identity, transform);
                    // Only add BreakableWall if not on outer boundary AND not part of L or T shape
                    if (Random.value < breakableWallChance && y < height - 1 && !IsWallPartOfLOrTShape(x, y, "North"))
                        instantiatedNorthWall.AddComponent<BreakableWall>();
                }

                // South wall
                if (cell.HasSouthWall && _selectedTerrainAssets.southWallPrefabs != null && _selectedTerrainAssets.southWallPrefabs.Count > 0)
                {
                    GameObject southWall = _selectedTerrainAssets.southWallPrefabs[Random.Range(0, _selectedTerrainAssets.southWallPrefabs.Count)];
                    Vector3 pos = cellCenter + new Vector3(0, 0, -cellSize / 2);
                    GameObject instantiatedSouthWall = Instantiate(southWall, pos, Quaternion.identity, transform);
                    if (Random.value < breakableWallChance && y > 0 && !IsWallPartOfLOrTShape(x, y, "South"))
                        instantiatedSouthWall.AddComponent<BreakableWall>();
                }

                // East wall
                if (cell.HasEastWall && _selectedTerrainAssets.eastWallPrefabs != null && _selectedTerrainAssets.eastWallPrefabs.Count > 0)
                {
                    GameObject eastWall = _selectedTerrainAssets.eastWallPrefabs[Random.Range(0, _selectedTerrainAssets.eastWallPrefabs.Count)];
                    Vector3 pos = cellCenter + new Vector3(cellSize / 2, 0, 0);
                    GameObject instantiatedEastWall = Instantiate(eastWall, pos, Quaternion.Euler(0, 90, 0), transform);
                    if (Random.value < breakableWallChance && x < width - 1 && !IsWallPartOfLOrTShape(x, y, "East"))
                        instantiatedEastWall.AddComponent<BreakableWall>();
                }

                // West wall
                if (cell.HasWestWall && _selectedTerrainAssets.westWallPrefabs != null && _selectedTerrainAssets.westWallPrefabs.Count > 0)
                {
                    GameObject westWall = _selectedTerrainAssets.westWallPrefabs[Random.Range(0, _selectedTerrainAssets.westWallPrefabs.Count)];
                    Vector3 pos = cellCenter + new Vector3(-cellSize / 2, 0, 0);
                    GameObject instantiatedWestWall = Instantiate(westWall, pos, Quaternion.Euler(0, 90, 0), transform);
                    if (Random.value < breakableWallChance && x > 0 && !IsWallPartOfLOrTShape(x, y, "West"))
                        instantiatedWestWall.AddComponent<BreakableWall>();
                }

                // Instantiate a waypoint at the center of the cell (for navigation/debugging)
                GameObject waypoint = new GameObject("Waypoint" + waypointCounter.ToString("00"));
                waypoint.transform.position = cellCenter;
                waypoint.transform.parent = transform;
                waypointCounter++;
            }
        }
    }

    /// <summary>
    /// Determines if a wall is part of an L-shaped or T-shaped wall configuration,
    /// which would make it ineligible to be a breakable wall.
    /// </summary>
    /// <param name="x">X-coordinate of the cell</param>
    /// <param name="y">Y-coordinate of the cell</param>
    /// <param name="wallDirection">Direction of the wall: "North", "South", "East", or "West"</param>
    /// <returns>True if the wall is part of an L or T shape, false otherwise</returns>
    private bool IsWallPartOfLOrTShape(int x, int y, string wallDirection)
    {
        // Skip check for outer walls - they can't be breakable anyway
        if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
            return false;
            
        MazeCell cell = _maze[x, y];
        MazeCell northCell = (y < height - 1) ? _maze[x, y + 1] : null;
        MazeCell southCell = (y > 0) ? _maze[x, y - 1] : null;
        MazeCell eastCell = (x < width - 1) ? _maze[x + 1, y] : null;
        MazeCell westCell = (x > 0) ? _maze[x - 1, y] : null;
        
        switch (wallDirection)
        {
            case "North":
                // Check for L-shape: North + East or North + West
                if ((cell.HasNorthWall && cell.HasEastWall) || (cell.HasNorthWall && cell.HasWestWall))
                    return true;
                    
                // Check for T-shape: North + East + West
                if (cell.HasNorthWall && cell.HasEastWall && cell.HasWestWall)
                    return true;
                    
                // Check if adjacent cells form L or T with this North wall
                if (northCell != null)
                {
                    // L-shape from the north cell's perspective: South + East or South + West
                    if ((northCell.HasSouthWall && northCell.HasEastWall) || (northCell.HasSouthWall && northCell.HasWestWall))
                        return true;
                    
                    // T-shape from the north cell's perspective: South + East + West
                    if (northCell.HasSouthWall && northCell.HasEastWall && northCell.HasWestWall)
                        return true;
                }
                break;
                
            case "South":
                // Check for L-shape: South + East or South + West
                if ((cell.HasSouthWall && cell.HasEastWall) || (cell.HasSouthWall && cell.HasWestWall))
                    return true;
                    
                // Check for T-shape: South + East + West
                if (cell.HasSouthWall && cell.HasEastWall && cell.HasWestWall)
                    return true;
                    
                // Check if adjacent cells form L or T with this South wall
                if (southCell != null)
                {
                    // L-shape from the south cell's perspective: North + East or North + West
                    if ((southCell.HasNorthWall && southCell.HasEastWall) || (southCell.HasNorthWall && southCell.HasWestWall))
                        return true;
                    
                    // T-shape from the south cell's perspective: North + East + West
                    if (southCell.HasNorthWall && southCell.HasEastWall && southCell.HasWestWall)
                        return true;
                }
                break;
                
            case "East":
                // Check for L-shape: East + North or East + South
                if ((cell.HasEastWall && cell.HasNorthWall) || (cell.HasEastWall && cell.HasSouthWall))
                    return true;
                    
                // Check for T-shape: East + North + South
                if (cell.HasEastWall && cell.HasNorthWall && cell.HasSouthWall)
                    return true;
                    
                // Check if adjacent cells form L or T with this East wall
                if (eastCell != null)
                {
                    // L-shape from the east cell's perspective: West + North or West + South
                    if ((eastCell.HasWestWall && eastCell.HasNorthWall) || (eastCell.HasWestWall && eastCell.HasSouthWall))
                        return true;
                    
                    // T-shape from the east cell's perspective: West + North + South
                    if (eastCell.HasWestWall && eastCell.HasNorthWall && eastCell.HasSouthWall)
                        return true;
                }
                break;
                
            case "West":
                // Check for L-shape: West + North or West + South
                if ((cell.HasWestWall && cell.HasNorthWall) || (cell.HasWestWall && cell.HasSouthWall))
                    return true;
                    
                // Check for T-shape: West + North + South
                if (cell.HasWestWall && cell.HasNorthWall && cell.HasSouthWall)
                    return true;
                    
                // Check if adjacent cells form L or T with this West wall
                if (westCell != null)
                {
                    // L-shape from the west cell's perspective: East + North or East + South
                    if ((westCell.HasEastWall && westCell.HasNorthWall) || (westCell.HasEastWall && westCell.HasSouthWall))
                        return true;
                    
                    // T-shape from the west cell's perspective: East + North + South
                    if (westCell.HasEastWall && westCell.HasNorthWall && westCell.HasSouthWall)
                        return true;
                }
                break;
        }
        
        return false;
    }
    #endregion

    #region Player & Endpoint Spawning
    /// <summary>
    /// Spawn the player at the southwest corner and the endpoint at the northeast corner.
    /// </summary>
    private void SpawnPlayerAndEndpoint()
    {
        // Calculate positions for start and endpoint cells
        Vector3 startPos = new Vector3(_playerSpawnCell.x * cellSize, 0, _playerSpawnCell.y * cellSize); // Southwest corner (cell 0,0)
        Vector3 endPos = new Vector3((width - 1) * cellSize, 0, (height - 1) * cellSize); // Northeast corner (cell width-1, height-1)

        // Instantiate player prefab at the start cell if assigned
        if (playerPrefab != null)
            Instantiate(playerPrefab, startPos, Quaternion.identity, transform);
        else
            Debug.LogWarning("Player prefab is not assigned!");

        // Instantiate endpoint prefab at the endpoint cell if assigned.
        // The endpoint floor should have a trigger to detect player arrival and trigger the win condition.
        if (endpointPrefab != null)
            Instantiate(endpointPrefab, endPos, Quaternion.identity, transform);
        else
            Debug.LogWarning("Endpoint prefab is not assigned!");
    }
    #endregion

    #region Enemy Paths & Spawning
    /// <summary>
    /// Generate enemy paths based on grid cells.
    /// Two types of paths are generated: I-shaped (straight) and L-shaped.
    /// Only cells with a clear path and not already used by another path are selected.
    /// </summary>
    private void GenerateEnemyPaths()
    {
        _enemyPaths = new List<List<Vector3>>();
        _usedPathCells = new HashSet<Vector2Int> {
            // Mark the player spawn location as used to prevent paths from starting there
            _playerSpawnCell };

        // Iterate through each cell as a potential starting point
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector2Int startCell = new Vector2Int(x, y);
                // Skip if cell is already used by an enemy path or is the player spawn
                if (_usedPathCells.Contains(startCell))
                    continue;

                if (Random.value < enemyPathSpawnChance)
                {
                    bool isIStraight = (Random.value < 0.5f);
                    List<Vector2Int> candidate = isIStraight ? GenerateIStraightPath(startCell) : GenerateLShapedPath(startCell);
                    if (candidate != null && candidate.Count >= 3)
                    {
                        foreach (Vector2Int cell in candidate)
                            _usedPathCells.Add(cell);
                        _enemyPaths.Add(ConvertPathToWorld(candidate));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Check if a cell is the player spawn cell (0,0)
    /// </summary>
    private bool IsPlayerSpawnCell(Vector2Int cell)
    {
        return cell.x == _playerSpawnCell.x && cell.y == _playerSpawnCell.y;
    }

    /// <summary>
    /// Generate an I-shaped (straight) enemy path starting from a given cell.
    /// Returns a list of grid coordinates if the generated path is at least 3 cells long.
    /// </summary>
    private List<Vector2Int> GenerateIStraightPath(Vector2Int start)
    {
        List<Vector2Int> path = new List<Vector2Int> { start };

        // Determine all valid directions from the starting cell that are clear and not used.
        List<Vector2Int> validDirs = new List<Vector2Int>();
        if (start.y < height - 1 && IsPathClear(start, new Vector2Int(start.x, start.y + 1)) && !_usedPathCells.Contains(new Vector2Int(start.x, start.y + 1)) && !IsPlayerSpawnCell(new Vector2Int(start.x, start.y + 1)))
            validDirs.Add(new Vector2Int(0, 1));
        if (start.y > 0 && IsPathClear(start, new Vector2Int(start.x, start.y - 1)) && !_usedPathCells.Contains(new Vector2Int(start.x, start.y - 1)) && !IsPlayerSpawnCell(new Vector2Int(start.x, start.y - 1)))
            validDirs.Add(new Vector2Int(0, -1));
        if (start.x < width - 1 && IsPathClear(start, new Vector2Int(start.x + 1, start.y)) && !_usedPathCells.Contains(new Vector2Int(start.x + 1, start.y)) && !IsPlayerSpawnCell(new Vector2Int(start.x + 1, start.y)))
            validDirs.Add(new Vector2Int(1, 0));
        if (start.x > 0 && IsPathClear(start, new Vector2Int(start.x - 1, start.y)) && !_usedPathCells.Contains(new Vector2Int(start.x - 1, start.y)) && !IsPlayerSpawnCell(new Vector2Int(start.x - 1, start.y)))
            validDirs.Add(new Vector2Int(-1, 0));

        if (validDirs.Count == 0)
            return null;

        Vector2Int dir = validDirs[Random.Range(0, validDirs.Count)];
        Vector2Int current = start;
        bool canExtend = true;
        // Ensure the path has at least 3 cells by forcing at least two extensions.
        while (canExtend)
        {
            Vector2Int next = current + dir;
            if (next.x < 0 || next.x >= width || next.y < 0 || next.y >= height)
                break;
            if (!IsPathClear(current, next) || _usedPathCells.Contains(next) || IsPlayerSpawnCell(next))
                break;

            path.Add(next);
            current = next;
            if (path.Count >= 3)
            {
                if (Random.value < 0.5f)
                    break;
            }
        }
        return (path.Count >= 3) ? path : null;
    }

    /// <summary>
    /// Generate an L-shaped enemy path from a starting cell.
    /// The path consists of two segments (primary and perpendicular) with a turning point.
    /// Returns a list of grid coordinates if the generated path is at least 3 cells long.
    /// </summary>
    private List<Vector2Int> GenerateLShapedPath(Vector2Int start)
    {
        List<Vector2Int> primary = new List<Vector2Int> { start };

        // Determine valid primary directions from the starting cell.
        List<Vector2Int> validPrimary = new List<Vector2Int>();
        if (start.y < height - 1 && IsPathClear(start, new Vector2Int(start.x, start.y + 1)) && !_usedPathCells.Contains(new Vector2Int(start.x, start.y + 1)) && !IsPlayerSpawnCell(new Vector2Int(start.x, start.y + 1)))
            validPrimary.Add(new Vector2Int(0, 1));
        if (start.y > 0 && IsPathClear(start, new Vector2Int(start.x, start.y - 1)) && !_usedPathCells.Contains(new Vector2Int(start.x, start.y - 1)) && !IsPlayerSpawnCell(new Vector2Int(start.x, start.y - 1)))
            validPrimary.Add(new Vector2Int(0, -1));
        if (start.x < width - 1 && IsPathClear(start, new Vector2Int(start.x + 1, start.y)) && !_usedPathCells.Contains(new Vector2Int(start.x + 1, start.y)) && !IsPlayerSpawnCell(new Vector2Int(start.x + 1, start.y)))
            validPrimary.Add(new Vector2Int(1, 0));
        if (start.x > 0 && IsPathClear(start, new Vector2Int(start.x - 1, start.y)) && !_usedPathCells.Contains(new Vector2Int(start.x - 1, start.y)) && !IsPlayerSpawnCell(new Vector2Int(start.x - 1, start.y)))
            validPrimary.Add(new Vector2Int(-1, 0));

        if (validPrimary.Count == 0)
            return null;

        Vector2Int primaryDir = validPrimary[Random.Range(0, validPrimary.Count)];
        Vector2Int current = start;
        while (true)
        {
            Vector2Int next = current + primaryDir;
            if (next.x < 0 || next.x >= width || next.y < 0 || next.y >= height)
                break;
            if (!IsPathClear(current, next) || _usedPathCells.Contains(next) || IsPlayerSpawnCell(next))
                break;

            primary.Add(next);
            current = next;
            if (primary.Count >= 2)
            {
                if (Random.value < 0.5f)
                    break;
            }
        }
        if (primary.Count < 2)
            return null;

        // The turning cell is the last cell of the primary segment.
        Vector2Int turnCell = primary[primary.Count - 1];

        // Determine valid perpendicular directions from the turning cell.
        List<Vector2Int> perpDirs = new List<Vector2Int>();
        if (primaryDir.x != 0) // Primary is horizontal; valid perpendicular directions are vertical.
        {
            if (turnCell.y < height - 1 && IsPathClear(turnCell, new Vector2Int(turnCell.x, turnCell.y + 1)) && !_usedPathCells.Contains(new Vector2Int(turnCell.x, turnCell.y + 1)) && !IsPlayerSpawnCell(new Vector2Int(turnCell.x, turnCell.y + 1)))
                perpDirs.Add(new Vector2Int(0, 1));
            if (turnCell.y > 0 && IsPathClear(turnCell, new Vector2Int(turnCell.x, turnCell.y - 1)) && !_usedPathCells.Contains(new Vector2Int(turnCell.x, turnCell.y - 1)) && !IsPlayerSpawnCell(new Vector2Int(turnCell.x, turnCell.y - 1)))
                perpDirs.Add(new Vector2Int(0, -1));
        }
        else // Primary is vertical; valid perpendicular directions are horizontal.
        {
            if (turnCell.x < width - 1 && IsPathClear(turnCell, new Vector2Int(turnCell.x + 1, turnCell.y)) && !_usedPathCells.Contains(new Vector2Int(turnCell.x + 1, turnCell.y)) && !IsPlayerSpawnCell(new Vector2Int(turnCell.x + 1, turnCell.y)))
                perpDirs.Add(new Vector2Int(1, 0));
            if (turnCell.x > 0 && IsPathClear(turnCell, new Vector2Int(turnCell.x - 1, turnCell.y)) && !_usedPathCells.Contains(new Vector2Int(turnCell.x - 1, turnCell.y)) && !IsPlayerSpawnCell(new Vector2Int(turnCell.x - 1, turnCell.y)))
                perpDirs.Add(new Vector2Int(-1, 0));
        }
        if (perpDirs.Count == 0)
            return null;

        Vector2Int perpDir = perpDirs[Random.Range(0, perpDirs.Count)];
        List<Vector2Int> perpendicular = new List<Vector2Int> { turnCell };

        current = turnCell;
        while (true)
        {
            Vector2Int next = current + perpDir;
            if (next.x < 0 || next.x >= width || next.y < 0 || next.y >= height)
                break;
            if (!IsPathClear(current, next) || _usedPathCells.Contains(next) || IsPlayerSpawnCell(next))
                break;

            perpendicular.Add(next);
            current = next;
            if (perpendicular.Count >= 2)
            {
                if (Random.value < 0.5f)
                    break;
            }
        }
        if (perpendicular.Count < 2)
            return null;

        // Combine primary and perpendicular segments into one L-shaped path.
        List<Vector2Int> candidate = new List<Vector2Int>(primary);
        // Skip the duplicate turning cell from the perpendicular segment.
        for (int i = 1; i < perpendicular.Count; i++)
            candidate.Add(perpendicular[i]);

        return (candidate.Count >= 3) ? candidate : null;
    }

    /// <summary>
    /// Convert a list of grid coordinates to world positions.
    /// </summary>
    private List<Vector3> ConvertPathToWorld(List<Vector2Int> gridPath)
    {
        List<Vector3> worldPath = new List<Vector3>();
        foreach (Vector2Int cell in gridPath)
            worldPath.Add(new Vector3(cell.x * cellSize, 0, cell.y * cellSize));
        return worldPath;
    }

    /// <summary>
    /// Spawn an enemy prefab at the start point of each enemy path.
    /// </summary>
    private void SpawnEnemies()
    {
        if (enemyPrefab == null)
        {
            Debug.LogWarning("Enemy prefab is not assigned!");
            return;
        }

        foreach (var path in _enemyPaths)
        {
            if (path.Count < 2) continue; // Ensure 2 points for patrolling
            
            // Instantiate the enemy at the first point of its path
            var enemyInstance = Instantiate(enemyPrefab, path[0], Quaternion.identity, transform);
                
            var enemyController = enemyInstance.GetComponent<EnemyController>();
            if (enemyController != null)
            {
                var pointCount = Mathf.Min(path.Count, 3); // 2 - line, 3 - corner
                var patrolPoints = new Transform[pointCount];
                    
                for (var i = 0; i < pointCount; i++)
                {
                    var pointObj = new GameObject($"PatrolPoint_{enemyInstance.name}_{i+1}")
                    {
                        transform =
                        {
                            position = path[i],
                            parent = transform // Parent to the MapGenerator
                        }
                    };

                    patrolPoints[i] = pointObj.transform;
                }
                    
                // init patrol points in enemy controller
                enemyController.patrolPoints = patrolPoints;
                    
                // Log the patrol setup for debugging
                Debug.Log($"Enemy spawned with {pointCount} patrol points: {string.Join(", ", patrolPoints.Select(t => t.position))}");
            }
            else
            {
                Debug.LogWarning("Enemy prefab does not have an EnemyController component!");
            }
        }
    }
    #endregion

    #region Decorations
    /// <summary>
    /// Spawn decorations on cell edges.
    /// Decorations are placed near a wall edge if the cell has a wall on that side.
    /// If the cell already has a collectible, search for the nearest cell without one.
    /// Do not spawn decorations in cells used by enemy paths.
    /// </summary>
    private void SpawnDecorations()
    {
        // Offset to place decoration near the wall edge (10% of cellSize)
        float offset = cellSize * 0.1f;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector2Int cellCoord = new Vector2Int(x, y);
                // Skip cells that have a collectible or are part of an enemy path.
                if (_collectibleInCell[x, y] || (_usedPathCells != null && _usedPathCells.Contains(cellCoord)))
                    continue;

                // Check decoration spawn chance.
                if (Random.value >= decorationSpawnChance)
                    continue;

                // If the cell already has a collectible (should not occur due to above check), find nearest eligible cell.
                Vector2Int targetCell = cellCoord;
                if (_collectibleInCell[x, y])
                {
                    Vector2Int? nearest = FindNearestCellWithoutCollectible(cellCoord);
                    if (nearest.HasValue)
                        targetCell = nearest.Value;
                    else
                        continue;
                }

                // Determine candidate positions along walls for the target cell.
                Vector3 cellCenter = new Vector3(targetCell.x * cellSize, 0, targetCell.y * cellSize);
                List<Vector3> candidatePositions = new List<Vector3>();
                MazeCell cell = _maze[targetCell.x, targetCell.y];
                if (cell.HasNorthWall)
                    candidatePositions.Add(cellCenter + new Vector3(0, 0, cellSize / 2 - offset));
                if (cell.HasSouthWall)
                    candidatePositions.Add(cellCenter + new Vector3(0, 0, -cellSize / 2 + offset));
                if (cell.HasEastWall)
                    candidatePositions.Add(cellCenter + new Vector3(cellSize / 2 - offset, 0, 0));
                if (cell.HasWestWall)
                    candidatePositions.Add(cellCenter + new Vector3(-cellSize / 2 + offset, 0, 0));

                if (candidatePositions.Count == 0)
                    continue;

                // Randomly choose one candidate position for the decoration.
                Vector3 decoPos = candidatePositions[Random.Range(0, candidatePositions.Count)];
                if (_selectedTerrainAssets.decorationPrefabs != null && _selectedTerrainAssets.decorationPrefabs.Count > 0)
                {
                    GameObject decoPrefab = _selectedTerrainAssets.decorationPrefabs[Random.Range(0, _selectedTerrainAssets.decorationPrefabs.Count)];
                    Instantiate(decoPrefab, decoPos, Quaternion.identity, transform);
                }
            }
        }
    }

    /// <summary>
    /// Use Breadth-First Search (BFS) to find the nearest cell without a collectible and not in an enemy path.
    /// </summary>
    private Vector2Int? FindNearestCellWithoutCollectible(Vector2Int start)
    {
        bool[,] visited = new bool[width, height];
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(start);
        visited[start.x, start.y] = true;
        int[] dx = { 0, 0, 1, -1 };
        int[] dy = { 1, -1, 0, 0 };

        while (queue.Count > 0)
        {
            Vector2Int cur = queue.Dequeue();
            if (!_collectibleInCell[cur.x, cur.y] && (_usedPathCells == null || !_usedPathCells.Contains(cur)))
                return cur;

            for (int i = 0; i < 4; i++)
            {
                Vector2Int next = new Vector2Int(cur.x + dx[i], cur.y + dy[i]);
                if (next.x >= 0 && next.x < width && next.y >= 0 && next.y < height && !visited[next.x, next.y])
                {
                    visited[next.x, next.y] = true;
                    queue.Enqueue(next);
                }
            }
        }
        return null;
    }
    #endregion

    #region Debug
    /// <summary>
    /// Debug: Draw enemy paths in the Scene view.
    /// </summary>
    private void OnDrawGizmos()
    {
        if (_enemyPaths != null && debugDrawPaths)
        {
            Gizmos.color = Color.red;
            foreach (List<Vector3> path in _enemyPaths)
            {
                for (int i = 0; i < path.Count - 1; i++)
                    Gizmos.DrawLine(path[i] + Vector3.up * 1f, path[i + 1] + Vector3.up * 1f);
            }
        }
    }
    #endregion

    #region Internal Classes
    /// <summary>
    /// Internal class representing a cell in the maze.
    /// </summary>
    private class MazeCell
    {
        public bool Visited;
        public bool HasNorthWall = true;
        public bool HasSouthWall = true;
        public bool HasEastWall = true;
        public bool HasWestWall = true;
    }
    #endregion
}

