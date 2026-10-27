using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>A finite graded walking surface and its soil-seated perimeter.</summary>
    public sealed class CityEntranceForecourtPlan
    {
        internal CityEntranceForecourtPlan(Vector3[] top,Vector3[] bottom,int rows,int columns,bool graded)
        {
            Top=new ReadOnlyCollection<Vector3>(top);Bottom=new ReadOnlyCollection<Vector3>(bottom);
            Rows=rows;Columns=columns;IsGraded=graded;
            Bounds bounds=new Bounds(top[0],Vector3.zero);
            foreach(Vector3 point in top)bounds.Encapsulate(point);
            foreach(Vector3 point in bottom)bounds.Encapsulate(point);
            Bounds=bounds;Footprint=Rect.MinMaxRect(bounds.min.x,bounds.min.z,bounds.max.x,bounds.max.z);
        }
        public IReadOnlyList<Vector3> Top {get;}
        public IReadOnlyList<Vector3> Bottom {get;}
        public int Rows {get;}
        public int Columns {get;}
        public bool IsGraded {get;}
        public Bounds Bounds {get;}
        public Rect Footprint {get;}

        public float SampleTop(Vector3 point)
        {
            Vector3 first=Top[0],along=Top[(Rows-1)*Columns]-first;
            Vector2 a=new Vector2(along.x,along.z),p=new Vector2(point.x-first.x,point.z-first.z);
            float row=Mathf.Clamp01(Vector2.Dot(p,a)/a.sqrMagnitude)*(Rows-1);
            int low=Mathf.Min(Rows-2,Mathf.FloorToInt(row));float rowT=row-low;
            Vector3 inner=Vector3.Lerp(Top[low*Columns],Top[(low+1)*Columns],rowT);
            Vector3 outer=Vector3.Lerp(Top[low*Columns+Columns-1],Top[(low+1)*Columns+Columns-1],rowT);
            Vector2 run=new Vector2(outer.x-inner.x,outer.z-inner.z);
            float column=Mathf.Clamp01(Vector2.Dot(new Vector2(point.x-inner.x,point.z-inner.z),run)/run.sqrMagnitude)*(Columns-1);
            int near=Mathf.Min(Columns-2,Mathf.FloorToInt(column));float columnT=column-near;
            float aTop=Top[low*Columns+near].y,bTop=Top[low*Columns+near+1].y;
            float cTop=Top[(low+1)*Columns+near+1].y,dTop=Top[(low+1)*Columns+near].y;
            // Same A-C diagonal as the mesh, including a non-planar crossfall.
            return columnT>=rowT?aTop*(1f-columnT)+bTop*(columnT-rowT)+cTop*rowT:
                aTop*(1f-rowT)+cTop*columnT+dTop*(rowT-columnT);
        }
    }
}
