using Godot;
using Godot.Collections;

namespace voxel_playground.generation.terrain_generator;

[Tool]
public partial class MeshInstance : MeshInstance3D
{
    private Array _surfaceArray;
    private System.Collections.Generic.List<Vector3> _vertices;
    private System.Collections.Generic.List<Vector3> _normals;
    private System.Collections.Generic.List<Color> _colors;

    /// <summary>
    /// 单位立方体的8个顶点（中心在原点，边长1）
    ///     Z
    ///     |
    ///     4─────5
    ///    /|    /|
    ///   7─────6 |   ─── Y
    ///   | 0───|-1  /
    ///   |/    |/  X
    ///   3─────2
    /// 
    /// 0: (-0.5, -0.5,  0.5)  front-bottom-left
    /// 1: ( 0.5, -0.5,  0.5)  front-bottom-right
    /// 2: ( 0.5, -0.5, -0.5)  back-bottom-right
    /// 3: (-0.5, -0.5, -0.5)  back-bottom-left
    /// 4: (-0.5,  0.5,  0.5)  front-top-left
    /// 5: ( 0.5,  0.5,  0.5)  front-top-right
    /// 6: ( 0.5,  0.5, -0.5)  back-top-right
    /// 7: (-0.5,  0.5, -0.5)  back-top-left
    /// </summary>
    private Array<Vector3> _cubeVertices =
    [
        new(-0.5f, -0.5f, 0.5f),
        new(0.5f, -0.5f, 0.5f),
        new(0.5f, -0.5f, -0.5f),
        new(-0.5f, -0.5f, -0.5f),
        new(-0.5f, 0.5f, 0.5f),
        new(0.5f, 0.5f, 0.5f),
        new(0.5f, 0.5f, -0.5f),
        new(-0.5f, 0.5f, -0.5f)
    ];

    private enum Face
    {
        Front,   // z = +0.5,  faceNormal = (0, 0, 1)
        Back,    // z = -0.5,  faceNormal = (0, 0, -1)
        Left,    // x = -0.5,  faceNormal = (-1, 0, 0)
        Right,   // x = +0.5,  faceNormal = (1, 0, 0)
        Bottom,  // y = -0.5,  faceNormal = (0, -1, 0)
        Top      // y = +0.5,  faceNormal = (0, 1, 0)
    }

    /// <summary>
    /// 每个面由2个三角形构成。每个三角形用 Vector3 存储3个顶点索引。
    /// Godot 以顺时针顺序判定正面，因此这里按从面外侧观察时的顺时针顺序组织顶点。
    /// </summary>
    private Dictionary<Face, Array> _faceTriangles = new()
    {
        // Front (z=+0.5): 顶点 0,1,5,4 → 从 +Z 看顺时针: 0→4→5, 0→5→1
        { Face.Front, new Array { new Vector3(0, 4, 5), new Vector3(0, 5, 1) } },
        // Back (z=-0.5): 顶点 2,3,7,6 → 从 -Z 看顺时针: 3→6→7, 3→2→6
        { Face.Back, new Array { new Vector3(3, 6, 7), new Vector3(3, 2, 6) } },
        // Left (x=-0.5): 顶点 0,3,7,4 → 从 -X 看顺时针: 3→7→4, 3→4→0
        { Face.Left, new Array { new Vector3(3, 7, 4), new Vector3(3, 4, 0) } },
        // Right (x=+0.5): 顶点 1,2,6,5 → 从 +X 看顺时针: 2→5→6, 2→1→5
        { Face.Right, new Array { new Vector3(2, 5, 6), new Vector3(2, 1, 5) } },
        // Bottom (y=-0.5): 顶点 0,1,2,3 → 从 -Y 看顺时针: 0→1→2, 0→2→3
        { Face.Bottom, new Array { new Vector3(0, 1, 2), new Vector3(0, 2, 3) } },
        // Top (y=+0.5): 顶点 4,5,6,7 → 从 +Y 看顺时针: 4→6→5, 4→7→6
        { Face.Top, new Array { new Vector3(4, 6, 5), new Vector3(4, 7, 6) } }
    };

    private Dictionary<Face, Vector3> _faceNormals = new()
    {
        { Face.Front, new Vector3(0, 0, 1) },
        { Face.Back, new Vector3(0, 0, -1) },
        { Face.Left, new Vector3(-1, 0, 0) },
        { Face.Right, new Vector3(1, 0, 0) },
        { Face.Bottom, new Vector3(0, -1, 0) },
        { Face.Top, new Vector3(0, 1, 0) }
    };

    private Dictionary<Face, Color> _faceColors = new()
    {
        { Face.Front, Colors.Orange },
        { Face.Back, Colors.Purple },
        { Face.Left, Colors.Blue },
        { Face.Right, Colors.Yellow },
        { Face.Bottom, Colors.Red },
        { Face.Top, Colors.Green }
    };

    public override void _Ready()
    {
        // 初始化所有数组（防止空引用）
        _surfaceArray = new Array();
        _surfaceArray.Resize((int)Mesh.ArrayType.Max);
        _vertices = new System.Collections.Generic.List<Vector3>();
        _normals = new System.Collections.Generic.List<Vector3>();
        _colors = new System.Collections.Generic.List<Color>();

        GenerateMesh();
    }

    private void GenerateMesh()
    {
        // 生成6个面，每个面2个三角形 = 12个三角形 = 36个顶点
        AddFace(Face.Front, Vector3.Zero);
        AddFace(Face.Back, Vector3.Zero);
        AddFace(Face.Left, Vector3.Zero);
        AddFace(Face.Right, Vector3.Zero);
        AddFace(Face.Bottom, Vector3.Zero);
        AddFace(Face.Top, Vector3.Zero);

        CommitMesh();
    }

    private void AddFace(Face face, Vector3 position)
    {
        var triangles = _faceTriangles[face];
        var normal = _faceNormals[face];
        var color = _faceColors[face];

        // 每个三角形是一个 Vector3(x,y,z) 存储三个顶点索引
        foreach (var variant in triangles)
        {
            var tri = (Vector3)variant;
            int i0 = (int)tri.X;
            int i1 = (int)tri.Y;
            int i2 = (int)tri.Z;

            _vertices.Add(_cubeVertices[i0] + position);
            _vertices.Add(_cubeVertices[i1] + position);
            _vertices.Add(_cubeVertices[i2] + position);

            _normals.Add(normal);
            _normals.Add(normal);
            _normals.Add(normal);

            _colors.Add(color);
            _colors.Add(color);
            _colors.Add(color);
        }
    }

    private void CommitMesh()
    {
        _surfaceArray[(int)Mesh.ArrayType.Vertex] = _vertices.ToArray();
        _surfaceArray[(int)Mesh.ArrayType.Normal] = _normals.ToArray();
        _surfaceArray[(int)Mesh.ArrayType.Color] = _colors.ToArray();

        // 如果 Mesh 属性为 null，先创建 ArrayMesh 实例
        if (Mesh == null)
        {
            Mesh = new ArrayMesh();
        }

        var arrayMesh = Mesh as ArrayMesh;
        arrayMesh?.ClearSurfaces();
        arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, _surfaceArray);

        if (MaterialOverride == null)
        {
            MaterialOverride = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled
            };
        }
    }
}