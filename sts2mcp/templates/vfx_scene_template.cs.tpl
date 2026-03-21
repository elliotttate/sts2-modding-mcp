[gd_scene format=3]

[node name="{node_name}" type="Node2D"]

[node name="Particles" type="GPUParticles2D" parent="."]
emitting = false
amount = {particle_count}
lifetime = {lifetime}
one_shot = {one_shot}
explosiveness = {explosiveness}
visibility_rect = Rect2(-100, -100, 200, 200)
