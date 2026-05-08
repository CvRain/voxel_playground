extends Node3D

@export var world_size: Vector3i = Vector3i(16, 16, 16)
@export_range(-1, 1) var cut_off: float = 0.5
@export var player_scene: PackedScene

@onready var grass_cube: CSGBox3D = $GreenCube
@onready var white_cube: CSGBox3D = $WhiteCube

# 存储所有生成的方块位置
var generated_blocks: Array[Vector3i] = []

func set_block(cube: CSGBox3D, position: Vector3i) -> void:
	cube.position = Vector3(position)
	add_child(cube)
	generated_blocks.append(position)

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
		if generated_blocks.has(current):
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

func _ready() -> void:
	Input.mouse_mode = Input.MOUSE_MODE_CAPTURED

	var random_generator = FastNoiseLite.new()
	
	var random_number_generater = RandomNumberGenerator.new()
	random_number_generater.randomize()

	for x in range(world_size.x):
		for y in range(world_size.y):
			for z in range(world_size.z):
				var random_value = random_generator.get_noise_3d(x, y, z)
				var random_number = random_number_generater.randf()

				if random_value > cut_off && random_number > 0.5:
					set_block(grass_cube.duplicate() as CSGBox3D, Vector3i(x, y, z))

				elif random_value > cut_off && random_number <= 0.5:
					set_block(white_cube.duplicate() as CSGBox3D, Vector3i(x, y, z))

	# 生成地形完毕后，将玩家放置在世界中心上方
	# 使用广度优先搜索找到玩家脚底最近的方块位置
	var player_spawn_pos: Vector3i = Vector3i(world_size.x / 2, world_size.y, world_size.z / 2)
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
