using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static partial class CityCanneryAssetProvider
    {
        private readonly struct Surface
        {
            public readonly string Texture;
            public readonly Color Tint;
            public readonly float Smoothness, Metallic;
            public Surface(string texture, Color tint, float smoothness, float metallic=0)
            { Texture=texture; Tint=tint; Smoothness=smoothness; Metallic=metallic; }
        }
        private static readonly Dictionary<string, Surface> Surfaces = new Dictionary<string, Surface>
        {
            { "CanneryFloor", new Surface("CanneryFloorAlbedo",new Color(.61f,.68f,.65f),.24f) },
            { "WetFloor", new Surface("CanneryFloorAlbedo",new Color(.43f,.52f,.49f),.58f) },
            { "WashWall", new Surface("CanneryWashWallAlbedo",new Color(.74f,.81f,.76f),.26f) },
            { "Stainless", new Surface("CanneryStainlessAlbedo",new Color(.87f,.93f,.90f),.40f,.32f) },
            { "Insulation", new Surface("CanneryInsulationAlbedo",new Color(.80f,.85f,.79f),.21f) },
            { "Cardboard", new Surface("CanneryCardboardAlbedo",new Color(.78f,.78f,.73f),.04f) }
        };
        private static readonly Dictionary<string, Material> SurfaceMaterials = new Dictionary<string, Material>();
        public static Material GetSurfaceMaterial(string role)
        {
            if (!Surfaces.TryGetValue(role,out Surface definition)) return CityPortAssetProvider.GetSurfaceMaterial(role);
            if (SurfaceMaterials.TryGetValue(role,out Material material)&&material!=null) return material;
            Texture2D texture=Resources.Load<Texture2D>(ResourceFolder+"Textures/"+definition.Texture);
            Shader shader=Resources.Load<Shader>("Shaders/Ps1Lit");
            if (texture==null||shader==null) throw new InvalidOperationException("Cannery surface is missing: "+role);
            material=new Material(shader){name="Cannery "+role+" Shared",hideFlags=HideFlags.HideAndDontSave};
            material.SetTexture("_BaseMap",texture);
            material.SetColor("_BaseColor",definition.Tint.linear);
            material.SetFloat("_Smoothness",definition.Smoothness);
            material.SetFloat("_Metallic",definition.Metallic);
            SurfaceMaterials[role]=material;
            return material;
        }
    }
}
