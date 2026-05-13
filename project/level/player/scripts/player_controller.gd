extends CharacterBody3D

# 移动参数
@export var move_speed: float = 8.0
@export var fly_speed: float = 10.0
@export var jump_velocity: float = 6.0
@export var gravity: float = 20.0
@export var mouse_sensitivity: float = 0.003

# 节点引用
@onready var head: Node3D = $Head
@onready var eye_camera: Camera3D = $Head/EyeCamera

# 飞行模式状态
@export var flying: bool = false

# TerrainGenerator 引用（由 TerrainGenerator.cs 在 SpawnPlayer 后注入）
var terrain_generator: Node = null

func _ready() -> void:
	Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)
	_create_crosshair()

func _create_crosshair() -> void:
	var canvas_layer := CanvasLayer.new()
	add_child(canvas_layer)

	# 横线
	var h_bar := ColorRect.new()
	h_bar.color = Color(1.0, 1.0, 1.0, 0.85)
	h_bar.set_anchors_preset(Control.PRESET_CENTER)
	h_bar.offset_left   = -10
	h_bar.offset_right  =  10
	h_bar.offset_top    =  -1
	h_bar.offset_bottom =   1
	canvas_layer.add_child(h_bar)

	# 竖线
	var v_bar := ColorRect.new()
	v_bar.color = Color(1.0, 1.0, 1.0, 0.85)
	v_bar.set_anchors_preset(Control.PRESET_CENTER)
	v_bar.offset_left   =  -1
	v_bar.offset_right  =   1
	v_bar.offset_top    = -10
	v_bar.offset_bottom =  10
	canvas_layer.add_child(v_bar)

func _physics_process(delta: float) -> void:
	# 飞行模式切换
	if Input.is_action_just_pressed("fly_toggle"):
		flying = not flying
		# 切换到飞行模式时清零垂直速度
		if flying:
			velocity.y = 0
	
	# 飞行模式处理
	if flying:
		# 飞行时不受重力影响
		velocity.y = 0
		
		# 垂直飞行控制：空格上升，Shift下降
		if Input.is_action_pressed("jump"):
			velocity.y = fly_speed
		elif Input.is_action_pressed("crouch"):
			velocity.y = - fly_speed
	else:
		# 行走模式：添加重力
		if not is_on_floor():
			velocity.y -= gravity * delta
		
		# 地面跳跃
		if Input.is_action_just_pressed("jump") and is_on_floor():
			velocity.y = jump_velocity
	
	# 获取 WASD 输入方向
	var input_dir := Input.get_vector("move_left", "move_right", "move_forward", "move_backward")
	
	# 计算移动方向（相对于玩家朝向）
	var direction := Vector3.ZERO
	if input_dir.length() > 0:
		# 获取当前朝向
		var forward: Vector3 = transform.basis.z
		var right: Vector3 = transform.basis.x
		
		# 归一化水平方向
		forward.y = 0
		forward = forward.normalized()
		right.y = 0
		right = right.normalized()
		
		# 计算移动方向
		direction = (forward * input_dir.y + right * input_dir.x).normalized()
	
	# 根据模式选择移动速度
	var current_speed: float = fly_speed if flying else move_speed
	
	# 应用移动速度
	if direction.length() > 0:
		velocity.x = direction.x * current_speed
		velocity.z = direction.z * current_speed
	else:
		velocity.x = move_toward(velocity.x, 0, current_speed)
		velocity.z = move_toward(velocity.z, 0, current_speed)
	
	# 移动
	move_and_slide()

func _unhandled_input(event: InputEvent) -> void:
	# 鼠标移动 - 视角旋转（仅捕获状态下响应）
	if event is InputEventMouseMotion and Input.get_mouse_mode() == Input.MOUSE_MODE_CAPTURED:
		var relative: Vector2 = event.relative * mouse_sensitivity
		rotate_y(-relative.x)
		eye_camera.rotation.x = clamp(eye_camera.rotation.x - relative.y, deg_to_rad(-89), deg_to_rad(89))

	# ESC 切换鼠标捕获
	if event.is_action_pressed("ui_cancel"):
		if Input.get_mouse_mode() == Input.MOUSE_MODE_CAPTURED:
			Input.set_mouse_mode(Input.MOUSE_MODE_VISIBLE)
		else:
			Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)

	# 鼠标左键：捕获 or 破坏方块
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		if Input.get_mouse_mode() != Input.MOUSE_MODE_CAPTURED:
			Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)
		elif terrain_generator != null:
			terrain_generator.BreakBlockAt(eye_camera)

	# 鼠标右键：放置方块
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_RIGHT:
		if Input.get_mouse_mode() == Input.MOUSE_MODE_CAPTURED and terrain_generator != null:
			terrain_generator.PlaceBlockAt(eye_camera)
