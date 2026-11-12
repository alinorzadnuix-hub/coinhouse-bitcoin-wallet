# Blender Python Script to generate a Low-Poly Tree
# Can be run inside Blender: Scripting Tab -> New -> Paste -> Run Script
# Or via CLI: blender.exe --python create_lowpoly_tree.py

import bpy
import bmesh

def generate_low_poly_tree():
    # 1. Clear existing objects
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete()

    # 2. Setup Materials
    # Trunk Material (Brown)
    mat_trunk = bpy.data.materials.new(name="Mat_Trunk")
    mat_trunk.use_nodes = True
    bsdf_trunk = mat_trunk.node_tree.nodes.get("Principled BSDF")
    if bsdf_trunk:
        bsdf_trunk.inputs['Base Color'].default_value = (0.36, 0.22, 0.12, 1.0)
        bsdf_trunk.inputs['Roughness'].default_value = 0.9

    # Leaves Material (Green)
    mat_leaves = bpy.data.materials.new(name="Mat_Leaves")
    mat_leaves.use_nodes = True
    bsdf_leaves = mat_leaves.node_tree.nodes.get("Principled BSDF")
    if bsdf_leaves:
        bsdf_leaves.inputs['Base Color'].default_value = (0.16, 0.52, 0.20, 1.0)
        bsdf_leaves.inputs['Roughness'].default_value = 0.8

    # 3. Create Trunk (6-sided cylinder)
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=6, radius=0.32, depth=2.0, location=(0, 0, 1.0)
    )
    trunk = bpy.context.active_object
    trunk.name = "Tree_Trunk"
    trunk.data.materials.append(mat_trunk)

    # Taper trunk
    bpy.ops.object.mode_set(mode='EDIT')
    bm = bmesh.from_edit_mesh(trunk.data)
    for v in bm.verts:
        if v.co.z > 0.5:
            v.co.x *= 0.65
            v.co.y *= 0.65
    bmesh.update_edit_mesh(trunk.data)
    bpy.ops.object.mode_set(mode='OBJECT')

    for p in trunk.data.polygons:
        p.use_smooth = False

    # 4. Create Foliage Tiers (Low-poly Cones)
    tiers = [
        {"z": 2.2, "radius": 1.5, "depth": 1.7, "vertices": 8},
        {"z": 3.1, "radius": 1.2, "depth": 1.5, "vertices": 7},
        {"z": 3.9, "radius": 0.85, "depth": 1.3, "vertices": 6}
    ]

    foliage_objs = []
    for i, tier in enumerate(tiers):
        bpy.ops.mesh.primitive_cone_add(
            vertices=tier["vertices"],
            radius1=tier["radius"],
            radius2=0.0,
            depth=tier["depth"],
            location=(0, 0, tier["z"])
        )
        cone = bpy.context.active_object
        cone.name = f"Foliage_Tier_{i+1}"
        cone.data.materials.append(mat_leaves)
        for p in cone.data.polygons:
            p.use_smooth = False
        foliage_objs.append(cone)

    # 5. Join together
    bpy.ops.object.select_all(action='DESELECT')
    trunk.select_set(True)
    for fol in foliage_objs:
        fol.select_set(True)
    bpy.context.view_layer.objects.active = trunk
    bpy.ops.object.join()
    trunk.name = "LowPoly_Tree"

    print("Successfully generated LowPoly_Tree!")

if __name__ == "__main__":
    generate_low_poly_tree()
