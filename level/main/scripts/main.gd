extends Node3D

@export var world_size: Vector3i = Vector3i(16, 16, 16)
@export_range(0.0, 1.0) var terrain_density: float = 0.65
@export_range(0.001, 1.0, 0.001) var noise_frequency: float = 0.08
@export var noise_seed: int = 1337
@export var player_scene: PackedScene

@onready var mulit_mesh_instace: MultiMeshInstance3D = $MultiMeshInstance3D

# 存储所有生成的方块位置
var generated_blocks: Array[Vector3i] = []
var generated_block_lookup: Dictionary = {}

func add_block(block_position: Vector3i) -> void:
	if generated_block_lookup.has(block_position):
		return

	generated_blocks.append(block_position)
	generated_block_lookup[block_position] = true

# 使用广度优先搜索找到距离目标位置最近的方块
func find_nearest_block(start_pos: Vector3i) -> Vector3i:
	if generated_blocks.is_empty():
		return start_pos
	
	# BFS 队列
	var queue: Array[Vector3i] = [start_pos]
	var visited: Dictionary = {}
	visited[start_pos] = true
	
	# 6 个方向的邻居 (上、下、左、右、前、后)
	var directions: Array[Vector3i] = [
		Vector3i(0, 1, 0), # 上
		Vector3i(0, -1, 0), # 下
		Vector3i(1, 0, 0), # 右
		Vector3i(-1, 0, 0), # 左
		Vector3i(0, 0, 1), # 前
		Vector3i(0, 0, -1) # 后
	]
	
	while not queue.is_empty():
		var current: Vector3i = queue.pop_front()
		
		# 如果当前位置有方块，返回这个位置（玩家将站在这个方块上方）
		if generated_block_lookup.has(current):
			return current
		
		# 检查所有邻居
		for dir in directions:
			var neighbor: Vector3i = current + dir
			
			# 记录已访问的位置（不在水面下搜索以提高效率）
			if not visited.has(neighbor) and neighbor.y >= 0:
				visited[neighbor] = true
				queue.append(neighbor)
	
	# 如果没找到，返回原位置
	return start_pos

func generate_terrain() -> void:
	generated_blocks.clear()
	generated_block_lookup.clear()

	var noise := FastNoiseLite.new()
	noise.seed = noise_seed
	noise.frequency = noise_frequency

	var max_height: int = maxi(1, world_size.y - 1)
	var filled_height: float = clampf(terrain_density, 0.0, 1.0) * float(max_height)

	for x in range(world_size.x):
		for z in range(world_size.z):
			var noise_sample: float = (noise.get_noise_2d(x, z) + 1.0) * 0.5
			var column_height: int = clampi(int(round(noise_sample * filled_height)), 0, max_height)

			for y in range(column_height + 1):
				add_block(Vector3i(x, y, z))

func rebuild_multimesh() -> void:
	var multimesh: MultiMesh = mulit_mesh_instace.multimesh
	multimesh.instance_count = generated_blocks.size()

	for i in range(multimesh.instance_count):
		multimesh.set_instance_transform(i, Transform3D(Basis(), Vector3(generated_blocks[i])))

func _ready() -> void:
	Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
	generate_terrain()
	rebuild_multimesh()

	# 生成地形完毕后，将玩家放置在世界中心上方
	# 使用广度优先搜索找到玩家脚底最近的方块位置
	var center_x: int = floori(float(world_size.x) * 0.5)
	var center_z: int = floori(float(world_size.z) * 0.5)
	var player_spawn_pos: Vector3i = Vector3i(center_x, world_size.y - 1, center_z)
	var nearest_block: Vector3i = find_nearest_block(player_spawn_pos)
	
	# 将玩家放置在最近的方块上方
	var player_position: Vector3 = Vector3(nearest_block) + Vector3(0, 1, 0)
	
	# 实例化玩家并添加到场景
	if player_scene:
		var player: CharacterBody3D = player_scene.instantiate() as CharacterBody3D
		player.position = player_position
		add_child(player)
	else:
		push_warning("Player scene not assigned! Please set 'player_scene' in the inspector.")

func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		get_tree().quit()
