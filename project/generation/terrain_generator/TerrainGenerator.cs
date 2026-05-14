using Godot;

namespace voxel_playground.generation.terrain_generator;

public partial class TerrainGenerator : Node3D
{
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		GD.Print("Loading terrain generator");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}