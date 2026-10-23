"""Refresh packed Bar colour/response images after a texture-only repaint.

Run through the pinned run-blender launcher. This does not rebuild geometry,
change UVs or export FBX; it keeps the editable Blender preview in step with
the registered published texture pair.
"""
from pathlib import Path
import sys

sys.dont_write_bytecode = True
import bpy

ROOT=Path(__file__).resolve().parents[1]
BLEND=ROOT/"ArtSource/Bar/Blender/Bar3D.blend"
TEXTURES=ROOT/"Assets/Resources/Bar/Textures"


def main():
    bpy.ops.wm.open_mainfile(filepath=str(BLEND))
    replaced=0
    for name,colour_space in (("BarInteriorAlbedo.png","sRGB"),
                              ("BarInteriorResponse.png","Non-Color")):
        old=[image for image in bpy.data.images if Path(image.filepath).name==name]
        image=bpy.data.images.load(str(TEXTURES/name),check_existing=False)
        image.colorspace_settings.name=colour_space
        image.pack()
        for material in bpy.data.materials:
            if material.node_tree is None:
                continue
            for node in material.node_tree.nodes:
                if node.type=="TEX_IMAGE" and node.image in old:
                    node.image=image
                    replaced+=1
        for previous in old:
            bpy.data.images.remove(previous)
        image.name=name
    if replaced==0:
        raise RuntimeError("Bar preview contains no published interior texture nodes")
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND))
    print("BAR PACKED PREVIEW OK: registered colour/response refreshed; geometry and FBX retained.")


if __name__=="__main__":
    main()
