using System;
using System.Collections.Generic;
using BarPromenade.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Bounded instanced plants. Only nearby chunks materialise matrices; no per-tuft objects.</summary>
    [DefaultExecutionOrder(350), DisallowMultipleComponent]
    internal sealed class CityGrassField : MonoBehaviour
    {
        internal const string ResourcePath = "City/Grass/CityGrass3D";
        internal const int MaximumCachedChunks = 100, MaximumContacts = 8, MaximumTrails = 24;
        internal const float DrawDistance = 38f;
        private readonly Dictionary<Vector2Int,Chunk> chunks = new Dictionary<Vector2Int,Chunk>();
        private readonly List<Vector2Int> eviction = new List<Vector2Int>(MaximumCachedChunks);
        private readonly Stack<Chunk> pool = new Stack<Chunk>(MaximumCachedChunks);
        private readonly Plane[] planes = new Plane[6];
        private readonly Vector4[] contacts = new Vector4[MaximumContacts + MaximumTrails];
        private readonly Vector4[] previousContacts = new Vector4[MaximumContacts + MaximumTrails];
        private readonly Vector4[] renderedContacts = new Vector4[MaximumContacts + MaximumTrails];
        private readonly Vector3[] nearest = new Vector3[MaximumContacts-1];
        private readonly float[] nearestDistances = new float[MaximumContacts-1];
        private readonly float[] trailTimes = new float[MaximumTrails];
        private readonly Vector3[] trailRoots = new Vector3[MaximumTrails];
        private MaterialPropertyBlock properties;
        private CityGrassSurface surface;
        private CityLayout layout;
        private Template[] templates;
        private Material material;
        private CityWeatherController weather;
        private Transform hero;
        private CityPedestrianDirector pedestrians;
        private float phase, previousPhase, renderedPhase;
        private Vector4 wind,previousWind,renderedWind;
        private int historyFrame=-1,trailCursor;
        private Vector3 previousHero;
        private bool hasHero;
        private bool hasView;
        private Vector3 previousView;
        internal bool DebugStill {get;set;}
        internal Vector3? DebugContact {get;set;}
        internal WindSample? DebugWind {get;set;}
        internal float? DebugPhaseSeconds {get;set;}
        internal float PreviousRenderedPhaseSeconds=>previousWind.w;
        internal float AppliedPhaseSeconds=>wind.w;
        internal int CachedChunkCount=>chunks.Count;
        internal int LastDrawnTufts {get;private set;}
        internal int LastDrawCalls {get;private set;}
        internal int LastDrawnTriangles {get;private set;}
        internal int SurfaceFaceCount=>surface?.FaceCount??0;
        internal double LastPreparationMilliseconds {get;private set;}
        internal CityGrassSurface Surface=>surface;
        internal Vector3 FirstVisibleRoot {get;private set;}
        internal bool TryFindPlant(CityGrassHabitat habitat,out Vector3 root,out Vector3 tip)=>surface.TryFindPlant(habitat,out root,out tip);

        internal static CityGrassField Create(Transform parent, CityLayout city, CityGrassSurface ground)
        {
            var host=new GameObject("City Grass Field"); host.transform.SetParent(parent,false);
            var field=host.AddComponent<CityGrassField>(); field.layout=city; field.surface=ground;
            field.LoadTemplates();
            return field;
        }
        private void Awake()=>properties=new MaterialPropertyBlock();
        internal void Initialize(CityWeatherController controller, Transform player, CityPedestrianDirector crowd=null,Camera camera=null)
        {weather=controller;hero=player;pedestrians=crowd;if(camera!=null)WarmView(camera);}
        internal void WarmView(Camera camera)
        {
            Vector3 eye=surface.ToCity(camera.transform.position);
            var centre=new Vector2Int(Mathf.FloorToInt(eye.x/CityGrassPlan.ChunkSize),Mathf.FloorToInt(eye.z/CityGrassPlan.ChunkSize));
            Evict(centre);
            // Spawn/cuts/teleports publish a complete bounded neighbourhood.
            // Ordinary travel extends this retained cache ahead of visible grass.
            for(int z=-4;z<=4;z++)for(int x=-4;x<=4;x++)
            {
                Vector2Int key=centre+new Vector2Int(x,z);
                var rect=new Rect(key.x*CityGrassPlan.ChunkSize,key.y*CityGrassPlan.ChunkSize,CityGrassPlan.ChunkSize,CityGrassPlan.ChunkSize);
                Vector2 near=new Vector2(Mathf.Clamp(eye.x,rect.xMin,rect.xMax),Mathf.Clamp(eye.z,rect.yMin,rect.yMax));
                if(rect.Overlaps(surface.Bounds)&&Vector2.Distance(new Vector2(eye.x,eye.z),near)<=DrawDistance&&
                    !chunks.ContainsKey(key)&&chunks.Count<MaximumCachedChunks)chunks.Add(key,BuildChunk(key));
            }
            hasView=true;previousView=eye;
        }
        private void Evict(Vector2Int centre)
        {
            eviction.Clear();
            foreach(var pair in chunks)
                if(Mathf.Abs(pair.Key.x-centre.x)>4||Mathf.Abs(pair.Key.y-centre.y)>4)eviction.Add(pair.Key);
            foreach(var key in eviction){pool.Push(chunks[key]);chunks.Remove(key);}
        }
        internal void Invalidate(Rect bounds)
        {
            eviction.Clear();
            foreach(var pair in chunks)
            {
                var rect=new Rect(pair.Key.x*CityGrassPlan.ChunkSize,pair.Key.y*CityGrassPlan.ChunkSize,CityGrassPlan.ChunkSize,CityGrassPlan.ChunkSize);
                if(rect.Overlaps(bounds))eviction.Add(pair.Key);
            }
            foreach(var key in eviction){pool.Push(chunks[key]);chunks.Remove(key);}
            hasView=false;
        }

        private void LoadTemplates()
        {
            GameObject asset=Resources.Load<GameObject>(ResourcePath);
            if(asset==null)throw new InvalidOperationException("Missing authored City grass library.");
            var named=new Dictionary<string,MeshFilter>(StringComparer.Ordinal);
            foreach(var filter in asset.GetComponentsInChildren<MeshFilter>(true))named.Add(filter.name,filter);
            templates=new Template[8*3*3];
            for(int species=0;species<8;species++)for(int variant=0;variant<3;variant++)for(int lod=0;lod<3;lod++)
            {
                string key=$"{(CityGrassSpecies)species}_{variant}_LOD{lod}";
                if(!named.TryGetValue(key,out var source)||source.sharedMesh==null)
                    throw new InvalidOperationException("Missing authored grass variant "+key);
                Mesh mesh=source.sharedMesh;
                Bounds bounds=mesh.bounds;
                float height=source.transform.TransformVector(Vector3.up*bounds.size.y).magnitude;
                if(height<.06f||height>2f)throw new InvalidOperationException("Imported grass units changed: "+key);
                templates[(species*3+variant)*3+lod]=new Template(mesh,source.transform.localToWorldMatrix);
            }
            Shader shader=Resources.Load<Shader>("Shaders/CityGrassLit");
            if(shader==null||!shader.isSupported)throw new InvalidOperationException("Missing supported City grass shader.");
            material=new Material(RuntimePrimitiveFactory.DefaultMaterial)
            {name="City grass shared pigment",shader=shader,enableInstancing=true,hideFlags=HideFlags.HideAndDontSave};
            material.SetColor("_BaseColor",Color.white);
            material.SetFloat("_Cull",0); material.SetFloat("_Smoothness",.06f); material.SetFloat("_Metallic",0);
            material.EnableKeyword("_SCREENSPACEREFLECTIONS_OFF");
            material.SetShaderPassEnabled("MotionVectors",true);
        }
        private void LateUpdate()
        {
            if(surface==null)return;
            if(GameTimeScaleRuntime.IsPaused)return;
            phase+=Time.deltaTime;
            UpdateContacts();
        }
        private void UpdateContacts()
        {
            Array.Clear(contacts,0,contacts.Length);
            Vector3 position=DebugContact??(hero!=null?hero.position:Vector3.zero);
            if(hero!=null||DebugContact.HasValue)
            {
                contacts[0]=new Vector4(position.x,position.y,position.z,.68f);
                if(hasHero&&(position-previousHero).sqrMagnitude>.04f&&(position-previousHero).sqrMagnitude<4f)
                {trailRoots[trailCursor]=previousHero;trailTimes[trailCursor]=phase;trailCursor=(trailCursor+1)%MaximumTrails;}
                previousHero=position;hasHero=true;
            }
            for(int i=0;i<nearestDistances.Length;i++)nearestDistances[i]=float.PositiveInfinity;
            if(pedestrians!=null)
                foreach(var actor in pedestrians.Actors)
                {
                    if(actor==null||!actor.HasPresentation||!actor.gameObject.activeInHierarchy)continue;
                    Vector3 p=actor.Position;float d=(p-position).sqrMagnitude;
                    if(d>18f*18f)continue;
                    for(int slot=0;slot<nearest.Length;slot++)
                        if(d<nearestDistances[slot])
                        {
                            for(int shift=nearest.Length-1;shift>slot;shift--)
                            {nearestDistances[shift]=nearestDistances[shift-1];nearest[shift]=nearest[shift-1];}
                            nearestDistances[slot]=d;nearest[slot]=p;break;
                        }
                }
            for(int i=0;i<nearest.Length;i++)if(!float.IsPositiveInfinity(nearestDistances[i]))
                contacts[i+1]=new Vector4(nearest[i].x,nearest[i].y,nearest[i].z,.52f);
            for(int i=0;i<MaximumTrails;i++)
            {
                float age=phase-trailTimes[i];
                if(trailTimes[i]<=0||age>1.25f)continue;
                Vector3 p=trailRoots[i];
                // Radius also fades, leaving a finite, smoothly recovering footprint.
                contacts[MaximumContacts+i]=new Vector4(p.x,p.y,p.z,.60f*(1-age/1.25f));
            }
        }
        private void OnEnable()=>RenderPipelineManager.beginCameraRendering+=Render;
        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering-=Render;
            historyFrame=-1;hasHero=false;hasView=false;Array.Clear(trailTimes,0,trailTimes.Length);
            Array.Clear(contacts,0,contacts.Length); chunks.Clear();pool.Clear();
        }
        private void OnDestroy(){if(material!=null)Destroy(material);}
        private void Render(ScriptableRenderContext context,Camera camera)
        {
            if(surface==null||templates==null||camera==null||camera.gameObject.scene!=gameObject.scene||
                (camera.cameraType!=CameraType.Game&&camera.cameraType!=CameraType.SceneView))return;
            Draw(camera);
        }
        internal void Draw(Camera camera)
        {
            if(!isActiveAndEnabled||surface==null||material==null)return;
            long started=System.Diagnostics.Stopwatch.GetTimestamp();
            bool worldView=!camera.TryGetComponent<ScenePresentationExclusion>(out _);
            int savedTufts=LastDrawnTufts,savedCalls=LastDrawCalls,savedTriangles=LastDrawnTriangles;
            double savedMilliseconds=LastPreparationMilliseconds;
            Vector3 savedRoot=FirstVisibleRoot;
            Vector3 eye=surface.ToCity(camera.transform.position);
            if(worldView&&(!hasView||(eye-previousView).sqrMagnitude>64))WarmView(camera);
            if(!GameTimeScaleRuntime.IsPaused && DebugContact.HasValue)UpdateContacts();
            WindSample sample=DebugWind??(weather!=null?weather.CurrentWind:GameWeatherRules.EvaluateCurrentWind());
            Vector3 direction=surface.FromCityRotation*sample.HorizontalDirection;
            wind=new Vector4(direction.x,direction.z,DebugStill?0:sample.Strength01,DebugStill?0:DebugPhaseSeconds??phase);
            if(worldView&&historyFrame!=Time.frameCount)
            {
                bool contiguous=historyFrame==Time.frameCount-1;
                previousWind=contiguous?renderedWind:wind; previousPhase=contiguous?renderedPhase:phase;
                Array.Copy(contiguous?renderedContacts:contacts,previousContacts,contacts.Length);
                historyFrame=Time.frameCount;
            }
            if(worldView){renderedWind=wind;renderedPhase=phase;Array.Copy(contacts,renderedContacts,contacts.Length);}
            properties.SetVector("_CityGrassWind",wind);properties.SetVector("_CityGrassPreviousWind",previousWind);
            properties.SetVectorArray("_CityGrassContacts",contacts);properties.SetVectorArray("_CityGrassPreviousContacts",previousContacts);
            Vector2 minimum=new Vector2(float.PositiveInfinity,float.PositiveInfinity),maximum=-minimum;
            int contactCount=0;
            for(int i=0;i<contacts.Length;i++)
            {
                Include(contacts[i]);Include(previousContacts[i]);
                if(contacts[i].w>.015f||previousContacts[i].w>.015f)contactCount=i+1;
            }
            properties.SetVector("_CityGrassContactBounds",new Vector4(minimum.x,minimum.y,maximum.x,maximum.y));
            properties.SetInt("_CityGrassContactCount",contactCount);
            void Include(Vector4 body)
            {
                if(body.w<=.015f)return;
                minimum=Vector2.Min(minimum,new Vector2(body.x-body.w,body.z-body.w));
                maximum=Vector2.Max(maximum,new Vector2(body.x+body.w,body.z+body.w));
            }
            properties.SetVector("_CityGrassState",new Vector4(DebugStill?0:1,phase,previousPhase,MaximumContacts+MaximumTrails));
            Vector2Int centre=new Vector2Int(Mathf.FloorToInt(eye.x/CityGrassPlan.ChunkSize),Mathf.FloorToInt(eye.z/CityGrassPlan.ChunkSize));
            if(worldView)Evict(centre);
            GeometryUtility.CalculateFrustumPlanes(camera,planes);
            LastDrawnTufts=LastDrawCalls=LastDrawnTriangles=0;
            int generated=0;
            // Near first: a teleport always receives immediate foreground coverage.
            for(int ring=0;ring<=4;ring++)for(int z=-ring;z<=ring;z++)for(int x=-ring;x<=ring;x++)
            {
                if(Mathf.Max(Mathf.Abs(x),Mathf.Abs(z))!=ring)continue;
                var key=centre+new Vector2Int(x,z);
                var footprint=new Rect(key.x*CityGrassPlan.ChunkSize,key.y*CityGrassPlan.ChunkSize,CityGrassPlan.ChunkSize,CityGrassPlan.ChunkSize);
                if(!footprint.Overlaps(surface.Bounds))continue;
                Vector2 near=new Vector2(Mathf.Clamp(eye.x,footprint.xMin,footprint.xMax),Mathf.Clamp(eye.z,footprint.yMin,footprint.yMax));
                float distance=Vector2.Distance(new Vector2(eye.x,eye.z),near);
                if(distance>DrawDistance)continue;
                if(!chunks.TryGetValue(key,out Chunk chunk))
                {
                    if(!worldView||chunks.Count>=MaximumCachedChunks||generated>=6 ||
                        (generated>0&&(System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000.0/System.Diagnostics.Stopwatch.Frequency>3))continue;
                    chunk=BuildChunk(key);chunks.Add(key,chunk);generated++;
                }
                if(chunk.Batches.Count==0||!GeometryUtility.TestPlanesAABB(planes,chunk.Bounds))continue;
                int lod=distance<12?0:distance<24?1:2;
                // The last band shrinks continuously into the unchanged ground pigment.
                float fade=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(30,DrawDistance,distance));
                if(worldView&&chunk.RenderedFrame!=Time.frameCount)
                {
                    chunk.PreviousFade=chunk.RenderedFrame==Time.frameCount-1?chunk.Fade:fade;
                    chunk.RenderedFrame=Time.frameCount;
                }
                if(worldView)chunk.Fade=fade;
                properties.SetFloat("_CityGrassFade",fade);
                properties.SetFloat("_CityGrassPreviousFade",worldView?chunk.PreviousFade:fade);
                var rp=new RenderParams(material){camera=camera,worldBounds=chunk.Bounds,matProps=properties,
                    shadowCastingMode=distance<18?ShadowCastingMode.On:ShadowCastingMode.Off,
                    receiveShadows=true,layer=gameObject.layer,motionVectorMode=MotionVectorGenerationMode.Object};
                foreach(var batch in chunk.Batches)
                {
                    Template t=templates[batch.Template*3+lod];
                    Graphics.RenderMeshInstanced(rp,t.Mesh,0,batch.Matrices,batch.Count,batch.Start);
                    LastDrawnTufts+=batch.Count;LastDrawCalls++;LastDrawnTriangles+=t.Triangles*batch.Count;
                    FirstVisibleRoot=batch.FirstRoot;
                }
            }
            LastPreparationMilliseconds=(System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000.0/System.Diagnostics.Stopwatch.Frequency;
            if(!worldView)
            {
                LastDrawnTufts=savedTufts;LastDrawCalls=savedCalls;LastDrawnTriangles=savedTriangles;
                LastPreparationMilliseconds=savedMilliseconds;FirstVisibleRoot=savedRoot;
            }
        }
        private Chunk BuildChunk(Vector2Int key)
        {
            var chunk=pool.Count>0?pool.Pop():new Chunk();chunk.Reset();
            var lists=chunk.Lists;var roots=chunk.Roots;
            for(int z=0;z<CityGrassPlan.CellsPerChunk;z++)for(int x=0;x<CityGrassPlan.CellsPerChunk;x++)
            {
                int gx=key.x*CityGrassPlan.CellsPerChunk+x,gz=key.y*CityGrassPlan.CellsPerChunk+z;
                Vector2 point=CityGrassPlan.Root(layout.Seed,gx,gz);
                if(!surface.TrySample(point,out Vector3 root,out Vector3 normal,out var habitat)||
                    !CityGrassPlan.Plant(layout.Seed,gx,gz,habitat,out var species,out float scale,out int variant))continue;
                int template=(int)species*3+variant;
                Quaternion yaw=Quaternion.Euler(0,CityGrassPlan.Unit(CityGrassPlan.Hash(layout.Seed,gx,gz,871))*360f,0);
                Quaternion slope=Quaternion.FromToRotation(Vector3.up,Vector3.Lerp(Vector3.up,normal,.65f).normalized);
                Matrix4x4 basis=Matrix4x4.TRS(surface.FromCity(root),surface.FromCityRotation*slope*yaw,Vector3.one*scale);
                if(lists[template]==null)lists[template]=new List<Matrix4x4>(128);
                if(lists[template].Count==0)roots[template]=surface.FromCity(root);
                // All LODs have the same authored transform basis.
                lists[template].Add(basis*templates[template*3].Basis);
                Bounds plant=new Bounds(surface.FromCity(root)+Vector3.up*.8f,new Vector3(4.8f,3f,4.8f));
                if(chunk.Batches.Count==0&&chunk.TuftCount==0)chunk.Bounds=plant;else chunk.Bounds.Encapsulate(plant);
                chunk.TuftCount++;
            }
            for(int i=0;i<lists.Length;i++)
            {
                if(lists[i]==null)continue;
                for(int start=0;start<lists[i].Count;start+=511)
                {
                    int count=Mathf.Min(511,lists[i].Count-start);
                    if(chunk.Matrices[i]==null||chunk.Matrices[i].Length<lists[i].Count)
                        chunk.Matrices[i]=new Matrix4x4[Mathf.NextPowerOfTwo(lists[i].Count)];
                    if(start==0)lists[i].CopyTo(chunk.Matrices[i],0);
                    chunk.Batches.Add(new Batch(i,chunk.Matrices[i],count,start,roots[i]));
                }
            }
            return chunk;
        }
        private readonly struct Template
        {
            internal readonly Mesh Mesh;internal readonly Matrix4x4 Basis;internal readonly int Triangles;
            internal Template(Mesh mesh,Matrix4x4 basis){Mesh=mesh;Basis=basis;Triangles=(int)mesh.GetIndexCount(0)/3;}
        }
        private readonly struct Batch
        {
            internal readonly int Template,Count,Start;internal readonly Matrix4x4[] Matrices;internal readonly Vector3 FirstRoot;
            internal Batch(int template,Matrix4x4[] matrices,int count,int start,Vector3 root){Template=template;Matrices=matrices;Count=count;Start=start;FirstRoot=root;}
        }
        private sealed class Chunk
        {
            internal readonly List<Batch>Batches=new List<Batch>(24);
            internal readonly List<Matrix4x4>[] Lists=new List<Matrix4x4>[24];
            internal readonly Matrix4x4[][] Matrices=new Matrix4x4[24][];
            internal readonly Vector3[] Roots=new Vector3[24];
            internal Bounds Bounds;internal int TuftCount;
            internal int RenderedFrame=-1;internal float Fade,PreviousFade;
            internal void Reset(){Batches.Clear();TuftCount=0;RenderedFrame=-1;for(int i=0;i<Lists.Length;i++)Lists[i]?.Clear();}
        }
    }
}
