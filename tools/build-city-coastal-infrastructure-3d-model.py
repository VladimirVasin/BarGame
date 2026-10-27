#!/usr/bin/env python3
"""Bounded passive coastal utility kit, in metres; run with tools/run-blender.py.

Unity owns placement/collision and deforms only the authored CableSpan topology.
No material, animation, light, collider, text or NPC is exported.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import math
from pathlib import Path
import sys
sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
import bar_parts as bp
import interior_kit as kit
from city_building_parts import gable_roof
import bpy

VERSION = '1.0.0'
MODEL_DIR = ROOT / 'Assets/Resources/City/CoastalInfrastructure'
SOURCE_DIR = ROOT / 'ArtSource/City/CoastalInfrastructure'
SIZES = {'Substation': (5.0, 3.7, 4.0),
         'MunicipalWorkshop': (7.4, 4.4, 4.2),
         'PortWorkshop': (6.0, 4.2, 4.4)}
EQUIPMENT = {
    'Substation': ['transformer cooling fins', 'ceramic cable terminations', 'cable cabinet'],
    'MunicipalWorkshop': ['sorted kerb stock', 'drainage sections', 'repair trolley'],
    'PortWorkshop': ['rope drum', 'pulley rack', 'service canopy'],
}
TASK_LIGHTS = {name: (0, 2.95 if name == 'Substation' else 3.06, size[2]*.5+.055)
               for name,size in SIZES.items()}
PITCH = {'Concrete': 3, 'Plaster': 3, 'SteelDark': 1.6, 'Roof': 2,
         'Timber': 1, 'Rubber': 1, 'SteelLight': 1.6, 'Glass': 1, 'TaskLightLens': 1}


def box(c, s):
    return bp.u_box(c, s, min(.025, min(s) * .20))


def tube(c, radius, height):
    return bp.u_cylinder(c, (radius * 2, height / 2, radius * 2), 8)


def make_kit():
    result = {}
    for name, (width, height, depth) in SIZES.items():
        parts = {}
        def add(role, geometry):
            parts[role] = kit.merge(parts[role], geometry) if role in parts else geometry
        eave = height - .6
        add('Concrete', box((0, .16, 0), (width, .32, depth)))
        # Machinery is mounted in real shallow service bays within the reserved
        # logical envelope. No equipment projects into a route or grows a site.
        if name == 'Substation':
            add('Concrete', box((-.45, (eave + .32) / 2, 0),
                                (width - 1.08, eave - .32, depth - .18)))
        else:
            add('Plaster', box((0, (eave + .32) / 2, -.15),
                               (width - .18, eave - .32, depth - .48)))
        # Low pitched sheet roof, with thickness at the eaves and a clear ridge.
        roof = gable_roof((0, 0), width + .12, depth + .12, eave, height)
        add('Roof', bp.to_source((list(roof.vertices), list(roof.faces))))
        front = depth * .5
        if name == 'Substation':
            add('SteelDark', box((0, 1.40, front - .02), (1.65, 2.36, .16)))
            for x in (-1.4, 1.4):
                add('SteelDark', box((x, 2.2, front-.01), (.82, .88, .15)))
                for i in range(5):
                    add('SteelLight', box((x, 1.89 + i * .15, front+.09), (.73, .055, .06)))
            # A recessed transformer bay distinguishes the electrical site from
            # both workshops: cooling fins, busbar and actual cabinet links.
            add('SteelDark', box((width*.5-.57, 1.27, .63), (.62, 1.68, 1.22)))
            for z in (-.02, .15, .32, .49, .66, .83, 1.0, 1.17):
                add('SteelLight', box((width*.5-.49, 1.28, z), (.76, 1.44, .055)))
            add('SteelDark', box((width*.5-.54, 2.25, .16), (.12, .14, 1.42)))
            for z in (-.34, .16, .66):
                add('SteelLight', tube((width*.5-.54, 2.43, z), .09, .30))
                add('SteelDark', box((width*.5-.54, 2.63, z-.25), (.055, .055, .50)))
            # Cable entry cabinet and three ceramic bushings: no warning text.
            add('SteelDark', box((width*.5-.02, 1.55, -.5), (.32, .95, .76)))
            for z in (-.75, -.5, -.25):
                add('SteelLight', tube((width*.5-.02, 2.11, z), .085, .20))
        else:
            front -= .30
            gate_width = 2.7 if name == 'MunicipalWorkshop' else 2.3
            add('SteelDark', box((-.75, 1.49, front-.02), (gate_width, 2.60, .16)))
            for i in range(7):
                add('SteelLight', box((-.75, .39+i*.35, front+.08), (gate_width-.10, .045, .06)))
            add('SteelDark', box((width*.35, 1.37, front-.02), (1.05, 2.36, .16)))
            add('Glass', box((width*.35, 2.1, front+.085), (.72, .50, .035)))
            for x in (width*.35-.39,width*.35+.39):
                add('SteelLight', box((x,2.1,front+.11),(.055,.59,.06)))
            for y in (1.82,2.38):
                add('SteelLight', box((width*.35,y,front+.11),(.83,.05,.06)))
            # A lean working canopy stays within the existing eave envelope.
            add('Roof', box((0, 3.14, front+.02), (width-.54, .09, .76)))
            add('SteelDark', box((0, 3.03, front+.39), (width-.58, .13, .04)))
            if name == 'MunicipalWorkshop':
                # Sorted kerbs and culvert halves are stock for city repairs,
                # on the left; the narrow trolley belongs to the right bay.
                for y in (.42, .62, .82):
                    for x in (-width*.5+.45, -width*.5+.82):
                        add('Concrete', box((x, y, front+.16), (.30, .17, .36)))
                for x in (-width*.5+1.25, -width*.5+1.62):
                    add('Concrete', box((x, .48, front+.17), (.28, .30, .34)))
                    add('SteelDark', box((x, .53, front+.35), (.15, .14, .035)))
                cx=width*.5-.65
                add('SteelDark', box((cx, .59, front+.18), (.67, .10, .32)))
                add('Timber', box((cx, .82, front+.18), (.64, .08, .32)))
                for x in (cx-.24,cx+.24):
                    add('SteelDark', box((x, .62, front+.18), (.055, .65, .055)))
                    add('Rubber', bp.u_rotated_about(tube((x, .43, front+.18), .12, .09),
                                                     (90,0,0), (x, .43, front+.18)))
            else:
                # Rope drum and visibly different pulley sizes make this a
                # marine repair shop, without decorative rust or a new sign.
                cx=-width*.5+.62
                for radius,y in ((.24,1.37),(.17,1.97),(.12,2.43)):
                    add('SteelLight', bp.u_rotated_about(tube((cx,y,front+.21),radius,.12),
                                                        (90,0,0),(cx,y,front+.21)))
                    add('SteelDark', bp.u_rotated_about(tube((cx,y,front+.28),radius*.44,.04),
                                                       (90,0,0),(cx,y,front+.28)))
                add('SteelDark', box((cx,1.48,front+.10),(.07,2.29,.09)))
                drum=width*.5-.55
                for z in (front+.06,front+.30):
                    add('Timber', bp.u_rotated_about(tube((drum,.70,z),.32,.055),
                                                    (90,0,0),(drum,.70,z)))
                add('Rubber', bp.u_rotated_about(tube((drum,.70,front+.18),.23,.23),
                                                (90,0,0),(drum,.70,front+.18)))
            # Quiet service bench is attached at the rear, not in the public route.
            add('Timber', box((0, .87, -depth*.5+.36), (2.15, .11, .52)))
            for x in (-.90, .90):
                add('SteelDark', box((x, .44, -depth*.5+.36), (.075, .85, .40)))
        for x in (-width*.5+.18, width*.5-.18):
            add('SteelDark', tube((x, eave*.5, front-.03), .045, eave-.2))
        # One old repair patch belongs to each shell; metre UVs retain shared maps.
        add('Concrete', box((-width*.28, .81, -depth*.5+.065), (width*.28, 1.05, .10)))
        lamp=TASK_LIGHTS[name]
        add('SteelDark', box((lamp[0],lamp[1]+.055,lamp[2]-.025),(.48,.17,.12)))
        add('TaskLightLens', box((lamp[0],lamp[1]-.045,lamp[2]+.015),(.35,.025,.08)))
        result[name] = parts
    pole = {'Concrete': kit.merge(tube((0, 4.15, 0), .14, 8.3),
                                  box((0, .25, 0), (.38, .5, .38))),
            'SteelDark': kit.merge(box((0, 7.85, 0), (1.65, .12, .12)),
                                    box((0, 7.55, 0), (.13, .70, .13)))}
    insulators = []
    for x in (-.65, 0, .65):
        insulators.extend((tube((x, 8.03, 0), .075, .22),
                           tube((x, 8.08, 0), .10, .06)))
    pole['SteelLight'] = kit.merge_all(insulators)
    result['PowerPole'] = pole
    result['FacadeInlet'] = {'SteelDark': kit.merge(box((0, 0, 0), (.34, .42, .12)),
                                                  tube((0, .28, 0), .07, .2))}
    # The longitudinal parameter is exact on each ring; runtime stretches the
    # centreline while preserving the authored radius and explicit topology.
    vertices, faces = [], []
    rings, sides = 17, 6
    for ring in range(rings):
        t = ring / (rings - 1)
        for side in range(sides):
            angle = math.tau * side / sides
            vertices.append((.015 * math.cos(angle),
                             -.70 * 4*t*(1-t) + .015*math.sin(angle), t))
    for ring in range(rings-1):
        for side in range(sides):
            a=ring*sides+side; b=ring*sides+(side+1)%sides
            faces.append((a, b, b+sides, a+sides))
    faces.extend((tuple(reversed(range(sides))), tuple(range((rings-1)*sides, rings*sides))))
    result['CableSpan'] = {'Rubber': (vertices, faces)}
    return result


def manifest_for(groups):
    records=[]
    total=0
    for name, parts in groups.items():
        points=[v for geometry in parts.values() for v in geometry[0]]
        tris=sum(sum(len(f)-2 for f in g[1]) for g in parts.values())
        total+=tris
        records.append({'name':name,'logical_wall_size_unity':list(SIZES[name]) if name in SIZES else None,'bounds_min_unity':[round(min(v[a] for v in points),6) for a in range(3)],
                        'bounds_max_unity':[round(max(v[a] for v in points),6) for a in range(3)],
                        'triangle_count':tris,'roles':list(parts),
                        'functional_equipment':EQUIPMENT.get(name,[]),
                        'task_light_anchor': {'object_name':'IndustrialTaskLight_'+name,
                            'position_unity':list(TASK_LIGHTS[name]),'forward_unity':[0,-.819152,.573576]}
                            if name in TASK_LIGHTS else None})
    encoded=json.dumps(groups,sort_keys=True,separators=(',',':')).encode()
    return {'generator_version':VERSION,'design_id':'city_coastal_infrastructure_v1',
            'unit_factor':1.0,'scale_mode':'fixed_meters','imported_basis_correction_unity_euler':[0,180,0],'signature':hashlib.sha256(encoded).hexdigest(),
            'cable_span':{'rings':17,'sides':6,'longitudinal_axis':'Unity Z','nominal_sag_m':.7,
                          'radius_m':.015,'maximum_runtime_spans':192},
            'facility_ids':['coastal-substation','coastal-municipal-workshop','coastal-port-workshop'],
            'assemblies':records,'triangle_count':total}


def validate(groups, manifest):
    if set(groups) != set(SIZES)|{'PowerPole','FacadeInlet','CableSpan'}:
        raise ValueError('Coastal utility catalog incomplete')
    if manifest != manifest_for(make_kit()) or manifest['triangle_count'] > 12000:
        raise ValueError('Coastal utility determinism/budget contract failed')
    for name, parts in groups.items():
        for role,g in parts.items():
            if role not in PITCH or not g[0] or not g[1]: raise ValueError('Bad utility role/geometry')
            if not all(math.isfinite(v) for p in g[0] for v in p): raise ValueError('Non-finite utility vertex')
            if any(len(f)<3 or min(f)<0 or max(f)>=len(g[0]) for f in g[1]): raise ValueError('Bad utility topology')
            if bp.signed_volume(bp.to_source(g)) <= 0: raise ValueError('Inverted utility solid '+name+'/'+role)


def main():
    argv=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--model-dir',type=Path,default=MODEL_DIR)
    parser.add_argument('--source-dir',type=Path,default=SOURCE_DIR)
    parser.add_argument('--validate-only',action='store_true')
    parser.add_argument('--no-preview',action='store_true')
    args=parser.parse_args(argv)
    groups=make_kit(); manifest=manifest_for(groups);validate(groups,manifest)
    path=args.model_dir/'CityCoastalInfrastructure3D.json'
    if args.validate_only:
        if not path.exists() or json.loads(path.read_text())!=manifest: raise ValueError('Coastal utility manifest stale')
        print('CITY COASTAL INFRASTRUCTURE VALIDATION OK',manifest['signature']);return
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
    scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
    root=bpy.data.objects.new('ROOT_CityCoastalInfrastructure3D',None);scene.collection.objects.link(root)
    for name,parts in groups.items():
        group=bpy.data.objects.new(name,None);scene.collection.objects.link(group);group.parent=root
        if name in TASK_LIGHTS:
            anchor=bpy.data.objects.new('IndustrialTaskLight_'+name,None)
            scene.collection.objects.link(anchor);anchor.parent=group
            x,y,z=TASK_LIGHTS[name];anchor.location=(x,z,y)
        for role,geo in parts.items():
            mesh=bpy.data.meshes.new(name+'__'+role)
            source=bp.to_source(geo);mesh.from_pydata(source[0],[],source[1]);mesh.update(calc_edges=True)
            uv=mesh.uv_layers.new(name='UVMap')
            for polygon in mesh.polygons:
                axis=max(range(3),key=lambda i:abs(polygon.normal[i]));a,b=((1,2),(0,2),(0,1))[axis]
                points=[mesh.vertices[mesh.loops[loop].vertex_index].co for loop in polygon.loop_indices]
                low=(min(v[a] for v in points),min(v[b] for v in points))
                extent=(max(v[a] for v in points)-low[0],max(v[b] for v in points)-low[1])
                for loop in polygon.loop_indices:
                    vertex=mesh.vertices[mesh.loops[loop].vertex_index].co
                    uv.data[loop].uv=((vertex[a]-low[0])/max(extent[0],.001),
                                      (vertex[b]-low[1])/max(extent[1],.001)) if role=='Glass' else (vertex[a]/PITCH[role],vertex[b]/PITCH[role])
            obj=bpy.data.objects.new(name+'__'+role,mesh);scene.collection.objects.link(obj);obj.parent=group
    args.model_dir.mkdir(parents=True,exist_ok=True);args.source_dir.mkdir(parents=True,exist_ok=True)
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.fbx(filepath=str(args.model_dir/'CityCoastalInfrastructure3D.fbx'),use_selection=True,
        object_types={'EMPTY','MESH'},axis_forward='-Z',axis_up='Y',apply_scale_options='FBX_SCALE_NONE',
        bake_space_transform=False,add_leaf_bones=False,bake_anim=False,path_mode='STRIP')
    path.write_text(json.dumps(manifest,indent=2)+'\n')
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(args.source_dir/'CityCoastalInfrastructure3D.blend'),check_existing=False)
    print('CITY COASTAL INFRASTRUCTURE BUILD OK',manifest['triangle_count'],manifest['signature'])

if __name__=='__main__':main()
