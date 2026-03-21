[gd_scene load_steps=3 format=3]

[ext_resource type="Script" path="res://src/Core/Nodes/Combat/NCreatureVisuals.cs" id="1_script"]
[ext_resource type="Texture2D" path="res://{mod_name}/MonsterResources/{class_name}/{image_file}" id="2_texture"]

[node name="{class_name}" type="Node2D"]
script = ExtResource("1_script")

[node name="Visuals" type="Sprite2D" parent="."]
unique_name_in_owner = true
position = Vector2(0, -{center_y})
scale = Vector2({scale}, {scale})
texture = ExtResource("2_texture")

[node name="Bounds" type="Control" parent="."]
unique_name_in_owner = true
layout_mode = 3
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
offset_left = -{half_width}
offset_top = -{full_height}
offset_right = {half_width}
grow_horizontal = 2
grow_vertical = 2
mouse_filter = 2

[node name="CenterPos" type="Marker2D" parent="."]
unique_name_in_owner = true
position = Vector2(0, -{center_y})

[node name="IntentPos" type="Marker2D" parent="."]
unique_name_in_owner = true
position = Vector2(0, -{intent_y})
