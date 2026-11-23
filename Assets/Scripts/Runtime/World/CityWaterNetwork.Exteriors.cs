using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityWaterNetwork
    {
        // Blender kit in metres. At a square corner the bend consumes .23 m
        // of each tangent. Keep the survey node at their intersection.
        private const float BendSetback = .23f;
        private const float ClampReach = .287f;
        private Vector3 marketFront, homeFront;

        internal Vector3 StartStreetFacing(BuildingLot lot, Vector3 from)
        {
            if (!Enabled || (!lot.IsSupermarket && !lot.IsPlayerHome)) return lot.FacadeForward;
            // Frame the entrance and its street-facing service together at the
            // two debug sidewalk starts. Ordinary door returns own their facing.
            Vector3 front = lot.IsSupermarket ? marketFront : homeFront;
            if (front == Vector3.zero) return lot.FacadeForward;
            Vector3 heading = Vector3.Lerp(lot.DoorPosition,front,.80f)-from;
            return Vector3.ProjectOnPlane(heading,Vector3.up).normalized;
        }

        private int PressureJoint(int parent, Vector3 at, bool ridge)
        {
            int joint = Node(at);
            junctions[joint].Water = at;
            Edge(parent,joint,drain:false,crossing:true,onRidge:ridge);
            return joint;
        }

        private int ConnectCourse(int parent, Vector3[] path, bool ridge, int first=0)
        {
            for (int i=first; i<path.Length; i++) parent = PressureJoint(parent,path[i],ridge);
            return parent;
        }

        private static Quaternion Along(Vector3 forward)
        {
            return Quaternion.LookRotation(forward,
                Mathf.Abs(Vector3.Dot(forward.normalized,Vector3.up))>.98f ? Vector3.forward : Vector3.up);
        }

        private void DressCourse(Vector3[] path, float scale, Vector3[] mounts, bool shoes = false)
        {
            float setback = BendSetback*scale;
            float[] trims = new float[path.Length];
            for (int i=1; i<path.Length-1; i++)
            {
                Vector3 entering = (path[i]-path[i-1]).normalized;
                Vector3 leaving = (path[i+1]-path[i]).normalized;
                float dot = Vector3.Dot(entering, leaving);
                if (Mathf.Abs(dot)<.005f)
                {
                    trims[i] = setback;
                    Piece(CityWaterKit.StreetBend,path[i],
                        Quaternion.LookRotation(entering,Vector3.Cross(entering,leaving)),Vector3.one*scale);
                }
                else if (dot<.995f)
                    Piece(CityWaterKit.PipeCollar,path[i],Along(entering+leaving),Vector3.one*scale);
            }

            for (int i=0; i<path.Length-1; i++)
            {
                Vector3 direction = (path[i+1]-path[i]).normalized;
                Vector3 start = path[i]+direction*trims[i], end = path[i+1]-direction*trims[i+1];
                float length = Vector3.Distance(start,end);
                Piece(CityWaterKit.StreetBarrel,start,Along(direction),new Vector3(scale,scale,length));
                if (mounts == null || mounts[i] == Vector3.zero || length<.45f) continue;
                int count = Mathf.Max(1,Mathf.CeilToInt(length/2.7f));
                for (int j=0;j<count;j++)
                {
                    Vector3 at = Vector3.Lerp(start,end,(j+.5f)/count);
                    if (shoes && mounts[i]==Vector3.down)
                        Piece(CityWaterKit.GroundShoe,at,Along(direction),Vector3.one*scale);
                    else
                        Piece(CityWaterKit.WallClamp,at,Quaternion.LookRotation(direction,mounts[i]),Vector3.one*scale);
                }
            }
        }

        private void Valve(Vector3 a, Vector3 b, float scale)
        {
            var direction = (b-a).normalized;
            var center = Vector3.Lerp(a,b,.70f);
            Piece(CityWaterKit.LineValve,center,Along(direction),Vector3.one*scale);
            Vector3 offset = direction*(.20f*scale);
            Piece(CityWaterKit.PipeCollar,center-offset,Along(direction),Vector3.one*scale);
            Piece(CityWaterKit.PipeCollar,center+offset,Along(direction),Vector3.one*scale);
        }

        // A real tee in the side run links the outside line either to process
        // equipment through the wall or to the second dwelling across a court.
        private int RegisterExterior(string id, Vector3[] path, int teeIndex, Vector3 branchDirection,
            float scale, bool ridge, Vector3[] mounts, int sharedFeed, bool feedRear, bool processBranch, bool valve)
        {
            int street = TapStreet(path[0],ridge);
            int parent = sharedFeed >= 0 ? sharedFeed : street;
            int tee = -1;
            for (int i=0;i<path.Length-1;i++)
            {
                parent = PressureJoint(parent,path[i],ridge);
                if (i==teeIndex) tee = parent;
            }
            var wall = path[path.Length-1];
            Vector3 drain = path[0] + branchDirection*.62f - Vector3.up*.36f;
            RegisterService(id,wall,drain,street,parent,ridge);
            DressCourse(path,scale,mounts);
            if (valve) Valve(path[1],path[2],scale);
            Piece(CityWaterKit.PipeCollar,Vector3.Lerp(path[path.Length-2],wall,.51f),
                Along(wall-path[path.Length-2]),Vector3.one*scale);
            if (!feedRear && !processBranch) return -1;

            float branchScale = .65f*scale;
            Vector3 origin = path[teeIndex];
            Vector3[] branch = feedRear
                ? new[] { origin, origin+branchDirection*(.28f*scale), origin+branchDirection*(.28f*scale)-Vector3.up*1.65f }
                : new[] { origin, origin+branchDirection*(ClampReach*scale+.13f) };
            int outlet = ConnectCourse(tee,branch,ridge,first:1);
            DressCourse(branch,branchScale,null);
            Piece(CityWaterKit.PipeCollar,origin+branchDirection*(.17f*scale),Along(branchDirection),Vector3.one*branchScale);
            return outlet;
        }

        private int CornerService(string id, Vector3 corner, Vector3 side, Vector3 front, float backLength,
            float frontLength, float height, float scale, bool ridge = false, int sharedFeed = -1,
            bool feedRear = false, float riser = 0f)
        {
            float gap = ClampReach*scale;
            var outer = corner + side*gap + front*gap + Vector3.up*height;
            var rise = outer - front*backLength;
            var tee = Vector3.Lerp(rise,outer,.54f);
            var last = outer - side*frontLength;
            var underground = AtHeight(rise,corner.y-.70f);
            if (riser > 0f)
            {
                var high = last+Vector3.up*riser;
                var entry = new[]{underground,rise,tee,outer,last,high,high-front*(gap+.13f)};
                int branch = RegisterExterior(id,entry,2,side,scale,ridge,
                    new[]{-side,-side,-side,-front,-front,Vector3.zero},sharedFeed,feedRear,false,false);
                Valve(outer,last,scale);
                var focus = Vector3.Lerp(outer,last,.7f);
                if (ridge) homeFront=focus; else marketFront=focus;
                return branch;
            }
            var path = new[] { underground,rise,tee,outer,last,last-front*(gap+.13f) };
            return RegisterExterior(id,path,2,side,scale,ridge,
                new[] {-side,-side,-side,-front,Vector3.zero},sharedFeed,feedRear,false,false);
        }

        private int SurveyOrdinary(string id, BuildingLot lot, bool ridge, int sharedFeed, bool feedRear)
        {
            var pose = CityBuildingPrototypePlacement.ResolveExpectedCityPose(lot);
            Vector3 size = CityBuildingAssetProvider.GetExpectedEnvelope(lot.District,lot.BuildingVariant);
            var forward = pose.Rotation*Vector3.forward;
            bool industrial = lot.District == CityDistrictKind.Industrial;
            bool right = !industrial && (lot.BuildingVariant==3 || (lot.BuildingVariant!=2 && (lot.Cell.x&1)!=0));
            var side = pose.Rotation*(right ? Vector3.right : Vector3.left);
            float halfWidth = size.x*.5f, front = size.z*.5f-(industrial ? 0f : 1.2f);
            var corner = pose.Position + side*halfWidth + forward*front;
            if (!industrial)
            {
                float frontLength = Mathf.Min(3.8f,halfWidth-2.12f);
                float depth = Mathf.Min(6.4f,size.z*.66f);
                // Alternate an ordinary corner feed with a repaired gable riser.
                // The latter turns up at the masonry corner, outside the windows.
                if (!feedRear && (lot.Cell.x & 1)==0 && lot.BuildingVariant!=3)
                {
                    const float branchScale=.68f;
                    float gap=ClampReach*branchScale;
                    Vector3 elbow = corner+(side+forward)*gap + Vector3.up*.67f;
                    Vector3 from = elbow-side*frontLength;
                    Vector3 gable = elbow-forward*.76f;
                    Vector3 high = gable+Vector3.up*1.48f;
                    var course = new[]{AtHeight(from,corner.y-.70f),from,elbow,gable,high,high-side*(gap+.13f)};
                    return RegisterExterior(id,course,2,-side,branchScale,ridge,
                        new[]{-forward,-forward,-side,-side,Vector3.zero},sharedFeed,false,false,false);
                }
                return CornerService(id,corner,side,forward,depth,frontLength,.62f,.68f,ridge,sharedFeed,feedRear);
            }

            bool boiler = lot.IndustrialPurpose == CityIndustrialPurpose.BoilerHouse;
            float scale = boiler ? 1.08f : .87f;
            var outer = corner + side*(ClampReach*scale);
            var rear = outer-forward*(size.z-.88f);
            var end = outer-forward*.63f;
            var rise = rear+Vector3.up*(boiler ? 1.05f : .84f);
            var far = AtHeight(end,rise.y);
            var tee = Vector3.Lerp(rise,far,.58f);
            var turned = far+Vector3.up*(boiler ? 1.12f : .68f);
            var path = new[]{ rear-Vector3.up*.7f,rise,tee,far,turned,turned-side*(ClampReach*scale+.13f) };
            return RegisterExterior(id,path,2,feedRear ? side : -side,scale,ridge,
                new[]{-side,-side,-side,-side,Vector3.zero},sharedFeed,feedRear,boiler,boiler);
        }

        private void SurveyWorksOutlet(CityCoastalFacility works, int street)
        {
            Vector3 front = works.Rotation*Vector3.forward, side = works.Rotation*Vector3.right;
            Vector3 outlet = junctions[station].Water;
            const float scale = 1.10f;
            // The narrow rock cut is behind the building. Keep the exposed
            // lengths on the open apron and leave both working doors clear.
            Vector3 near = outlet+front*(works.Size.z*.5f+.62f-1f);
            Vector3 nearEnd = near-side*.58f;
            Vector3 below = nearEnd-Vector3.up*1.65f;
            Vector3 far = near-side*(works.Size.x+.47f);
            Vector3 farBack = far-front*(works.Size.z+.25f);
            var rightYard = new[]{outlet,near,nearEnd,below};
            DressCourse(rightYard,scale,new[]{Vector3.down,Vector3.zero,Vector3.zero},true);
            int last = ConnectCourse(station,rightYard,false,first:1);
            var leftYard = new[]{AtHeight(far,below.y),far,farBack,farBack-Vector3.up*1.65f};
            DressCourse(leftYard,scale,new[]{Vector3.zero,Vector3.down,Vector3.zero},true);
            last = ConnectCourse(last,leftYard,false);
            Edge(last,street,drain:false,crossing:true);
        }

        private void SurveyStairSupply(CityLayout layout)
        {
            var stair = CityHillsideAccessPlan.ForLayout(layout).SharedCourt;
            if (stair==null || stair.Flights.Count<2) return;
            var first = stair.Flights[stair.Flights.Count-2];
            var second = stair.Flights[stair.Flights.Count-1];
            Vector3 face = first.Right;
            // The two upper flights share an outside stringer and a flat landing.
            // Stand outside the rail with offset hangers anchored below the
            // nosings. Both walking directions see the line above the treads.
            const float scale = .88f;
            Vector3 shift = face*(first.Width*.5f+.32f) + Vector3.up*.49f;
            Vector3 a = first.Start+shift, b = first.End+shift;
            Vector3 c = second.Start+shift, d = second.End+shift;
            var path = new[]{a-Vector3.up*1.15f,a,b,c,d,d-Vector3.up*1.25f};
            int start = TapStreet(path[0],true), end = TapStreet(path[path.Length-1],true);
            int last = ConnectCourse(start,path,true);
            Edge(last,end,drain:false,crossing:true,onRidge:true);
            DressCourse(path,scale,null);
            Quaternion hanger = Quaternion.LookRotation(-first.Direction,Vector3.up);
            for (int i=1;i<4;i++)
            {
                Vector3 run = path[i+1]-path[i];
                int count = Mathf.CeilToInt(run.magnitude/2.3f);
                for (int j=0;j<count;j++)
                {
                    Vector3 at = path[i]+run*((j+.5f)/count);
                    Piece(CityWaterKit.StairBracket,at,hanger);
                    Piece(CityWaterKit.PipeCollar,at,Along(run),Vector3.one*scale);
                }
            }
        }
    }
}
