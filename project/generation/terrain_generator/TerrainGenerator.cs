using System.Collections.Generic;
using Godot;

namespace voxel_playground.generation.terrain_generator;

public partial class TerrainGenerator : Node3D
{
    private MultiMeshInstance3D _multiMeshInstance;

    [Export] public Vector3I WorldSize { get; set; }

    [Export] public PackedScene PlayerScene { get; set; }

    [Export] public float InteractionRange { get; set; } = 8.0f;

    private static readonly Color GrassColor = new Color(0.3f, 0.7f, 0.2f);
    private static readonly Color StoneColor = new Color(0.47f, 0.47f, 0.47f);

    // 数据层：存储所有方块（包括被遮挡的内部方块）
    private readonly Dictionary<Vector3I, Color> _blockData = new();

    // 渲染层：仅存储有暴露面的方块节点
    private readonly Dictionary<Vector3I, CsgBox3D> _blockMap = new();

    private static readonly Vector3I[] Directions =
    {
        new(0, 1, 0), new(0, -1, 0),
        new(1, 0, 0), new(-1, 0, 0),
        new(0, 0, 1), new(0, 0, -1),
    };

    public override void _Ready()
    {
        _multiMeshInstance = GetNode<MultiMeshInstance3D>("MultiMeshInstance3D");
        GenerateTerrain();
        SpawnPlayer();
    }

    public override void _Process(double delta)
    {
    }

    private void GenerateTerrain()
    {
        // 第一步：填充所有数据，不创建节点
        for (int x = 0; x < WorldSize.X; x++)
        for (int y = 0; y < WorldSize.Y; y++)
        for (int z = 0; z < WorldSize.Z; z++)
        {
            bool isTopLayer = y == WorldSize.Y - 1;
            _blockData[new Vector3I(x, y, z)] = isTopLayer ? GrassColor : StoneColor;
        }

        // 第二步：只为有暴露面的方块创建渲染节点
        foreach (var (pos, color) in _blockData)
            if (IsExposed(pos))
                CreateBlockNode(pos, color);
    }

    /// <summary>检查该位置是否有至少一个方向没有相邻方块（即有暴露面）</summary>
    private bool IsExposed(Vector3I pos)
    {
        foreach (var dir in Directions)
            if (!_blockData.ContainsKey(pos + dir))
                return true;
        return false;
    }

    private void SpawnPlayer()
    {
        if (PlayerScene == null)
        {
            GD.PushWarning("TerrainGenerator: PlayerScene 未赋值，请在 Inspector 中设置。");
            return;
        }

        var player = PlayerScene.Instantiate<Node3D>();

        float centerX = WorldSize.X * 0.5f;
        float centerZ = WorldSize.Z * 0.5f;
        float spawnY = WorldSize.Y + 1.0f;
        player.Position = new Vector3(centerX, spawnY, centerZ);

        player.Ready += () =>
        {
            // 注入 TerrainGenerator 引用，让玩家控制器可以调用方块交互方法
            player.Set("terrain_generator", this);
        };

        GetParent().CallDeferred(Node.MethodName.AddChild, player);
    }

    /// <summary>从指定相机位置发射射线，尝试破坏命中的方块（供玩家控制器调用）</summary>
    public void BreakBlockAt(Camera3D camera)
    {
        if (!CastRay(camera, out var hitPosition, out var hitNormal))
            return;
        DestroyBlock(SnapToGrid(hitPosition - hitNormal * 0.5f));
    }

    /// <summary>从指定相机位置发射射线，尝试在命中面的相邻位置放置方块（供玩家控制器调用）</summary>
    public void PlaceBlockAt(Camera3D camera)
    {
        if (!CastRay(camera, out var hitPosition, out var hitNormal))
            return;
        PlaceBlock(SnapToGrid(hitPosition + hitNormal * 0.5f), StoneColor);
    }

    private bool CastRay(Camera3D camera, out Vector3 hitPosition, out Vector3 hitNormal)
    {
        hitPosition = Vector3.Zero;
        hitNormal = Vector3.Zero;

        var spaceState = GetWorld3D().DirectSpaceState;
        var from = camera.GlobalPosition;
        var to = from - camera.GlobalBasis.Z * InteractionRange;

        var query = PhysicsRayQueryParameters3D.Create(from, to);
        var result = spaceState.IntersectRay(query);

        if (result.Count == 0)
            return false;

        hitPosition = result["position"].AsVector3();
        hitNormal = result["normal"].AsVector3();
        return true;
    }

    private static Vector3I SnapToGrid(Vector3 worldPos)
    {
        return new Vector3I(
            Mathf.RoundToInt(worldPos.X),
            Mathf.RoundToInt(worldPos.Y),
            Mathf.RoundToInt(worldPos.Z)
        );
    }

    /// <summary>创建方块渲染节点（仅渲染层，不修改数据层）</summary>
    private void CreateBlockNode(Vector3I position, Color color)
    {
        if (_blockMap.ContainsKey(position))
            return;

        var box = new CsgBox3D();
        box.Size = Vector3.One;
        box.Position = new Vector3(position.X, position.Y, position.Z);
        box.UseCollision = true;

        var material = new StandardMaterial3D();
        material.AlbedoColor = color;
        box.Material = material;

        AddChild(box);
        _blockMap[position] = box;
    }

    /// <summary>放置方块：更新数据层，并动态处理邻居节点可见性</summary>
    private void PlaceBlock(Vector3I position, Color color)
    {
        if (_blockData.ContainsKey(position))
            return;

        _blockData[position] = color;

        // 新方块本身若有暴露面则创建节点
        if (IsExposed(position))
            CreateBlockNode(position, color);

        // 原本暴露的邻居可能因此被遮挡，移除其渲染节点
        foreach (var dir in Directions)
        {
            var neighbor = position + dir;
            if (_blockMap.TryGetValue(neighbor, out var neighborBox) && !IsExposed(neighbor))
            {
                neighborBox.QueueFree();
                _blockMap.Remove(neighbor);
            }
        }
    }

    /// <summary>破坏方块：更新数据层，并动态暴露原本被遮挡的邻居</summary>
    private void DestroyBlock(Vector3I position)
    {
        if (!_blockData.ContainsKey(position))
            return;

        _blockData.Remove(position);

        // 移除渲染节点（如果存在）
        if (_blockMap.TryGetValue(position, out var box))
        {
            box.QueueFree();
            _blockMap.Remove(position);
        }

        // 邻居方块现在可能有了新的暴露面，需要创建渲染节点
        foreach (var dir in Directions)
        {
            var neighbor = position + dir;
            if (_blockData.TryGetValue(neighbor, out var neighborColor) && !_blockMap.ContainsKey(neighbor))
                CreateBlockNode(neighbor, neighborColor);
        }
    }
}
