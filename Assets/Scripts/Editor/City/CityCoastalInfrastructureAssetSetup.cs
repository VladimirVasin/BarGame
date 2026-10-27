using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Configure the passive metre kit explicitly in the coordinated asset setup pass.</summary>
    public static class CityCoastalInfrastructureAssetSetup
    {
        public const string ModelPath = "Assets/Resources/City/CoastalInfrastructure/CityCoastalInfrastructure3D.fbx";
        public const string ManifestPath = "Assets/Resources/City/CoastalInfrastructure/CityCoastalInfrastructure3D.json";

        [MenuItem("Bar Promenade/City Coastal Infrastructure/Rebuild And Validate")]
        public static void RunBatch() => BuildOrThrow();

        public static void BuildOrThrow()
        {
            AssetDatabase.ImportAsset(ModelPath,ImportAssetOptions.ForceSynchronousImport);
            ModelImporter importer=AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if(importer==null)throw new InvalidOperationException("Missing coastal utility FBX importer.");
            importer.globalScale=1f;importer.useFileScale=true;importer.bakeAxisConversion=false;
            importer.importAnimation=false;importer.animationType=ModelImporterAnimationType.None;
            importer.materialImportMode=ModelImporterMaterialImportMode.None;
            importer.isReadable=true;importer.addCollider=false;importer.meshCompression=ModelImporterMeshCompression.Off;
            importer.importNormals=ModelImporterNormals.Import;
            importer.SaveAndReimport();
            ValidateOrThrow();
        }
        public static void ValidateOrThrow()
        {
            Manifest manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if(manifest==null || manifest.generator_version!="1.0.0" || manifest.assemblies==null ||
                manifest.assemblies.Length!=6 || manifest.facility_ids==null || manifest.facility_ids.Length!=3 ||
                manifest.triangle_count>12000 || manifest.imported_basis_correction_unity_euler==null ||
                (Vector(manifest.imported_basis_correction_unity_euler)-new Vector3(0f,180f,0f)).sqrMagnitude>.00001f || manifest.signature==null || manifest.signature.Length!=64)
                throw new InvalidOperationException("Coastal utility manifest is stale or incomplete.");
            GameObject model=AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if(model==null)throw new InvalidOperationException("Coastal utility model did not import.");
            foreach(Assembly assembly in manifest.assemblies)
            {
                Transform root=null;
                foreach(Transform candidate in model.GetComponentsInChildren<Transform>(true))
                    if(candidate.name==assembly.name){root=candidate;break;}
                if(root==null)throw new InvalidOperationException("Coastal utility assembly missing: "+assembly.name);
                bool first=true;Bounds measured=default;
                foreach(MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if(filter.sharedMesh==null)continue;
                    Bounds bounds=filter.sharedMesh.bounds;
                    for(int corner=0;corner<8;corner++)
                    {
                        Vector3 local=bounds.center+Vector3.Scale(bounds.extents,new Vector3(
                            (corner&1)==0?-1f:1f,(corner&2)==0?-1f:1f,(corner&4)==0?-1f:1f));
                        Vector3 point=CityCoastalInfrastructureWorldBuilder.ImportedBasisCorrection * filter.transform.TransformPoint(local);
                        if(first){measured=new Bounds(point,Vector3.zero);first=false;}else measured.Encapsulate(point);
                    }
                }
                Vector3 low=Vector(assembly.bounds_min_unity),high=Vector(assembly.bounds_max_unity);
                if (assembly.logical_wall_size_unity != null && assembly.logical_wall_size_unity.Length == 3)
                {
                    Vector3 wall=Vector(assembly.logical_wall_size_unity);
                    if(wall.x<=0f || wall.y<=0f || wall.z<=0f ||
                        low.x>-wall.x*.5f || high.x<wall.x*.5f ||
                        low.z>-wall.z*.5f || high.z<wall.z*.5f || Mathf.Abs(high.y-wall.y)>.002f)
                        throw new InvalidOperationException("Coastal wall/render envelope contract drifted: "+assembly.name);
                }
                if(first || (measured.min-low).magnitude>.025f || (measured.max-high).magnitude>.025f)
                    throw new InvalidOperationException("Coastal utility metre bounds drifted: "+assembly.name+" "+measured+"; expected "+low+".."+high);
            }
            Debug.Log("CITY COASTAL INFRASTRUCTURE ASSET CONTRACT OK");
        }
        private static Vector3 Vector(float[] values)=>new Vector3(values[0],values[1],values[2]);
        [Serializable] private sealed class Manifest
        { public string generator_version;public float[] imported_basis_correction_unity_euler;public string signature;public int triangle_count;public string[] facility_ids;public Assembly[] assemblies; }
        [Serializable] private sealed class Assembly
        { public string name;public float[] logical_wall_size_unity;public float[] bounds_min_unity;public float[] bounds_max_unity; }
    }
}
