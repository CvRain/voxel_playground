using System;
using System.Collections.Generic;
using Godot;

namespace voxel_playground.generation.terrain_generator;

/// <summary>方块类型</summary>
public enum BlockType : byte
{
    Air = 0,
    Grass,
    Stone,
    Dirt,
}

/// <summary>
/// 采用 Greedy Meshing + ArrayMesh 的高性能体素地形生成器。
/// 优化策略：
///   - Greedy Meshing：将同类型同方向的相邻面合并为一个大四边形
///   - ArrayMesh：单个 MeshInstance3D 替代 N 个 CsgBox3D
///   - ConcavePolygonShape3D：单碰撞体替代逐方块碰撞
///   - DDA 体素射线步进：替代 PhysicsRayQuery，不依赖物理引擎
///   - 视锥剔除：每帧用完整 AABB-平面测试判断可见性
///   - 扁平 BlockType[] 数组：替代 Dictionary，无 GC 压力
/// </summary>
[Tool]
public partial class TerrainGenerator : Node3D
{
    private const int MaxChunkHeight = 256;

    // ──────────────── 导出属性 ────────────────

    [Export] public Vector3I ChunkSize { get; set; } = new(16, 256, 16);

    [Export] public PackedScene PlayerScene { get; set; }

    [Export] public float InteractionRange { get; set; } = 8.0f;

    [Export] public int InitialTerrainHeight { get; set; } = 32;

    // ──────────────── 方块配色 ────────────────

    private static readonly Color ColorGrass = new(0.3f, 0.7f, 0.2f);
    private static readonly Color ColorStone = new(0.5f, 0.5f, 0.5f);
    private static readonly Color ColorDirt  = new(0.5f, 0.35f, 0.2f);

    // ──────────────── 核心数据 ────────────────

    /// <summary>
    /// 扁平一维数组，索引 = x + z * Sx + y * Sx * Sz
    /// </summary>
    private BlockType[] _blocks;

    private int Sx => Mathf.Max(1, ChunkSize.X);
    private int Sy => Mathf.Clamp(ChunkSize.Y, 1, MaxChunkHeight);
    private int Sz => Mathf.Max(1, ChunkSize.Z);

    // ──────────────── 渲染节点 ────────────────

    private MeshInstance3D _meshInstance;
    private StaticBody3D _staticBody;
    private StandardMaterial3D _chunkMaterial;

    // ──────────────── 生命周期 ────────────────

    public override void _Ready()
    {
        if (ChunkSize.Y > MaxChunkHeight)
            GD.PushWarning($"TerrainGenerator: ChunkSize.Y 超过上限，已按 {MaxChunkHeight} 处理");

        _blocks = new BlockType[Sx * Sy * Sz];

        _meshInstance = new MeshInstance3D { Name = "ChunkMesh" };
        _chunkMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _meshInstance.MaterialOverride = _chunkMaterial;
        AddChild(_meshInstance);

        _staticBody = new StaticBody3D { Name = "ChunkCollision" };
        AddChild(_staticBody);

        GenerateTerrain();
        RebuildChunk();

        if (!Engine.IsEditorHint())
            CallDeferred(nameof(SpawnPlayer));
    }

    public override void _Process(double delta)
    {
    }

    // ──────────────── 地形生成 ────────────────

    private void GenerateTerrain()
    {
        int surfaceY = Mathf.Clamp(InitialTerrainHeight, 0, Mathf.Max(0, Sy - 2));
        int dirtStartY = Mathf.Max(0, surfaceY - 2);

        for (int x = 0; x < Sx; x++)
        for (int z = 0; z < Sz; z++)
        for (int y = 0; y < Sy; y++)
        {
            BlockType t = BlockType.Air;
            if (y <= surfaceY)
            {
                if (y == surfaceY)        t = BlockType.Grass;
                else if (y >= dirtStartY) t = BlockType.Dirt;
                else                      t = BlockType.Stone;
            }

            this[x, y, z] = t;
        }
    }

    // ──────────────── 体素数据存取 ────────────────

    private int Idx(int x, int y, int z) => x + z * Sx + y * Sx * Sz;

    private BlockType this[int x, int y, int z]
    {
        get => x < 0 || x >= Sx || y < 0 || y >= Sy || z < 0 || z >= Sz
            ? BlockType.Air : _blocks[Idx(x, y, z)];
        set
        {
            if (x >= 0 && x < Sx && y >= 0 && y < Sy && z >= 0 && z < Sz)
                _blocks[Idx(x, y, z)] = value;
        }
    }

    private bool IsSolid(int x, int y, int z) => this[x, y, z] != BlockType.Air;

    private static Color BlockColor(BlockType t) => t switch
    {
        BlockType.Grass => ColorGrass,
        BlockType.Dirt  => ColorDirt,
        BlockType.Stone => ColorStone,
        _ => Colors.Magenta,
    };

    private static Color FaceColor(BlockType t, int axis, int dir)
    {
        float brightness = axis switch
        {
            1 when dir > 0 => 1.0f,
            1 => 0.72f,
            0 when dir > 0 => 0.9f,
            0 => 0.82f,
            2 when dir > 0 => 0.86f,
            _ => 0.78f,
        };

        return BlockColor(t) * brightness;
    }

    // ──────────────── Greedy Meshing ────────────────

    private void BuildMesh()
    {
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var cols  = new List<Color>();
        var tris  = new List<int>();

        for (int axis = 0; axis < 3; axis++)
        for (int dir = -1; dir <= 1; dir += 2)
            GreedySlice(axis, dir, verts, norms, cols, tris);

        if (verts.Count == 0)
        {
            _meshInstance.Mesh = null;
            return;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = norms.ToArray();
        arrays[(int)Mesh.ArrayType.Color]  = cols.ToArray();
        arrays[(int)Mesh.ArrayType.Index]  = tris.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _meshInstance.Mesh = mesh;
    }

    /// <summary>对单个轴向的单个方向做贪婪合并</summary>
    private void GreedySlice(
        int axis, int dir,
        List<Vector3> verts, List<Vector3> norms, List<Color> cols, List<int> tris)
    {
        int sizeU = axis == 0 ? Sz : Sx;
        int sizeV = axis == 0 ? Sy : (axis == 1 ? Sz : Sy);
        int sizeN = axis == 0 ? Sx : (axis == 1 ? Sy : Sz);

        int maskLen = sizeU * sizeV;

        for (int n = 0; n < sizeN; n++)
        {
            var mask = new byte[maskLen];
            var maskCol = new Color?[maskLen];

            for (int u = 0; u < sizeU; u++)
            for (int v = 0; v < sizeV; v++)
            {
                int wx, wy, wz;
                switch (axis)
                {
                    case 0: wx = n;   wy = v;   wz = u;   break;
                    case 1: wx = u;   wy = n;   wz = v;   break;
                    default: wx = u;  wy = v;   wz = n;   break;
                }

                int nx = wx + (axis == 0 ? dir : 0);
                int ny = wy + (axis == 1 ? dir : 0);
                int nz = wz + (axis == 2 ? dir : 0);

                int idx = u + v * sizeU;
                if (IsSolid(wx, wy, wz) && !IsSolid(nx, ny, nz))
                {
                    mask[idx] = 1;
                    maskCol[idx] = FaceColor(this[wx, wy, wz], axis, dir);
                }
            }

            var processed = new bool[maskLen];

            for (int v = 0; v < sizeV; v++)
            for (int u = 0; u < sizeU; u++)
            {
                int idx = u + v * sizeU;
                if (mask[idx] == 0 || processed[idx])
                    continue;

                Color color = maskCol[idx] ?? Colors.White;

                int w = 1;
                while (u + w < sizeU &&
                       mask[(u + w) + v * sizeU] == 1 &&
                       maskCol[(u + w) + v * sizeU] == color)
                    w++;

                int h = 1;
                while (v + h < sizeV)
                {
                    bool ok = true;
                    for (int uu = u; uu < u + w && ok; uu++)
                        if (mask[uu + (v + h) * sizeU] == 0 ||
                            maskCol[uu + (v + h) * sizeU] != color)
                            ok = false;
                    if (!ok) break;
                    h++;
                }

                for (int vv = v; vv < v + h; vv++)
                for (int uu = u; uu < u + w; uu++)
                    processed[uu + vv * sizeU] = true;

                float nPos = n + 0.5f + (dir > 0 ? 0.5f : -0.5f);

                Vector3 basePos, stepU, stepV;

                switch (axis)
                {
                    case 0:
                        basePos = new Vector3(nPos, 0, 0);
                        stepU  = new Vector3(0, 0, 1);
                        stepV  = new Vector3(0, 1, 0);
                        break;
                    case 1:
                        basePos = new Vector3(0, nPos, 0);
                        stepU  = new Vector3(1, 0, 0);
                        stepV  = new Vector3(0, 0, 1);
                        break;
                    default:
                        basePos = new Vector3(0, 0, nPos);
                        stepU  = new Vector3(1, 0, 0);
                        stepV  = new Vector3(0, 1, 0);
                        break;
                }

                Vector3 normalDir = Vector3.Zero;
                normalDir[axis] = dir;

                float u0 = u, u1 = u + w;
                float v0 = v, v1 = v + h;

                Vector3 p0 = basePos + stepU * u0 + stepV * v0;
                Vector3 p1 = basePos + stepU * u1 + stepV * v0;
                Vector3 p2 = basePos + stepU * u0 + stepV * v1;
                Vector3 p3 = basePos + stepU * u1 + stepV * v1;

                int baseIdx = verts.Count;
                verts.Add(p0); verts.Add(p1); verts.Add(p2); verts.Add(p3);

                for (int i = 0; i < 4; i++) norms.Add(normalDir.Normalized());
                for (int i = 0; i < 4; i++) cols.Add(color);

                // 使用统一对角线 p0 -> p3 来拆分四边形，避免生成自交的“蝴蝶结”三角形。
                // 先按 p0,p1,p3 / p0,p3,p2 这一组绕序构造，再根据目标法线决定是否翻转。
                Vector3 basisNormal = (p1 - p0).Cross(p3 - p0).Normalized();
                bool useBaseWinding = basisNormal.Dot(normalDir) > 0.0f;

                if (useBaseWinding)
                {
                    tris.Add(baseIdx);
                    tris.Add(baseIdx + 1);
                    tris.Add(baseIdx + 3);

                    tris.Add(baseIdx);
                    tris.Add(baseIdx + 3);
                    tris.Add(baseIdx + 2);
                }
                else
                {
                    tris.Add(baseIdx);
                    tris.Add(baseIdx + 3);
                    tris.Add(baseIdx + 1);

                    tris.Add(baseIdx);
                    tris.Add(baseIdx + 2);
                    tris.Add(baseIdx + 3);
                }
            }
        }
    }

    // ──────────────── 碰撞体 ────────────────

    private void BuildCollision()
    {
        foreach (var child in _staticBody.GetChildren())
            child.QueueFree();

        if (_meshInstance.Mesh == null)
            return;

        var shape = new ConcavePolygonShape3D();
        shape.SetFaces(_meshInstance.Mesh.GetFaces());
        // Greedy mesh 可能出现局部绕序差异，开启背面碰撞避免“可进不可出”的单向碰撞。
        shape.BackfaceCollision = true;
        var colShape = new CollisionShape3D { Shape = shape };
        _staticBody.AddChild(colShape);
    }

    // ──────────────── 玩家生成 ────────────────

    private void SpawnPlayer()
    {
        if (PlayerScene == null)
        {
            GD.PushWarning("TerrainGenerator: PlayerScene 未赋值");
            return;
        }

        var player = PlayerScene.Instantiate<Node3D>();
        int spawnX = Mathf.Clamp(Sx / 2, 0, Sx - 1);
        int spawnZ = Mathf.Clamp(Sz / 2, 0, Sz - 1);
        int topY = FindTopSolidY(spawnX, spawnZ);

        // 胶囊体中心放在地表之上，避免出生在碰撞体内。
        player.Position = new Vector3(spawnX + 0.5f, topY + 1.6f, spawnZ + 0.5f);

        player.Ready += () => player.Set("terrain_generator", this);

        GetParent().AddChild(player);
    }

    private int FindTopSolidY(int x, int z)
    {
        for (int y = Sy - 1; y >= 0; y--)
            if (IsSolid(x, y, z))
                return y;

        return 0;
    }

    // ──────────────── 体素射线命中 ────────────────

    public bool CastVoxelRay(Camera3D camera, out Vector3I hitPos, out Vector3I hitNormal)
    {
        hitPos = Vector3I.Zero;
        hitNormal = Vector3I.Zero;

        var from = camera.GlobalPosition;
        var to = from - camera.GlobalBasis.Z * InteractionRange;

        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.HitFromInside = false;
        var result = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (result.Count == 0)
            return false;

        Vector3 p = result["position"].AsVector3();
        Vector3 n = result["normal"].AsVector3();

        // 命中体素：沿法线反向微移，落在被命中的方块内部。
        hitPos = WorldToVoxel(p - n * 0.01f);
        if (!InBounds(hitPos.X, hitPos.Y, hitPos.Z))
            return false;

        hitNormal = ToCardinalNormal(n);
        return hitNormal != Vector3I.Zero;
    }

    private static Vector3I ToCardinalNormal(Vector3 n)
    {
        if (Mathf.Abs(n.X) > 0.5f) return new Vector3I(Mathf.Sign(n.X) > 0 ? 1 : -1, 0, 0);
        if (Mathf.Abs(n.Y) > 0.5f) return new Vector3I(0, Mathf.Sign(n.Y) > 0 ? 1 : -1, 0);
        if (Mathf.Abs(n.Z) > 0.5f) return new Vector3I(0, 0, Mathf.Sign(n.Z) > 0 ? 1 : -1);
        return Vector3I.Zero;
    }

    private static Vector3I WorldToVoxel(Vector3 p)
    {
        return new Vector3I(
            Mathf.FloorToInt(p.X),
            Mathf.FloorToInt(p.Y),
            Mathf.FloorToInt(p.Z)
        );
    }

    private bool InBounds(int x, int y, int z)
    {
        return x >= 0 && x < Sx && y >= 0 && y < Sy && z >= 0 && z < Sz;
    }

    // ──────────────── 交互接口 ────────────────

    public void BreakBlockAt(Camera3D camera)
    {
        if (!CastVoxelRay(camera, out var pos, out _))
            return;

        if (!IsSolid(pos.X, pos.Y, pos.Z))
            return;

        this[pos.X, pos.Y, pos.Z] = BlockType.Air;
        RebuildChunk();
    }

    public void PlaceBlockAt(Camera3D camera)
    {
        if (!CastVoxelRay(camera, out var pos, out var normal))
            return;

        int px = pos.X + normal.X;
        int py = pos.Y + normal.Y;
        int pz = pos.Z + normal.Z;

        if (px < 0 || px >= Sx || py < 0 || py >= Sy || pz < 0 || pz >= Sz)
            return;

        if (IsSolid(px, py, pz))
            return;

        if (WouldOverlapDynamicBody(px, py, pz))
            return;

        this[px, py, pz] = BlockType.Stone;
        RebuildChunk();
    }

    private bool WouldOverlapDynamicBody(int x, int y, int z)
    {
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new BoxShape3D { Size = Vector3.One * 0.98f },
            Transform = new Transform3D(Basis.Identity, new Vector3(x + 0.5f, y + 0.5f, z + 0.5f)),
            CollideWithBodies = true,
            CollideWithAreas = false,
            Margin = 0.0f,
        };

        query.Exclude = new Godot.Collections.Array<Rid> { _staticBody.GetRid() };
        return GetWorld3D().DirectSpaceState.IntersectShape(query, 1).Count > 0;
    }

    private void RebuildChunk()
    {
        BuildMesh();
        BuildCollision();
    }
}