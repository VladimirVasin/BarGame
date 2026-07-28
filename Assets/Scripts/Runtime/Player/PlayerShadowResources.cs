using System;
using UnityEngine;

namespace BarPromenade
{
    public static class PlayerShadowResources
    {
        public const string ShadowCasterShaderResourcePath =
            "Shaders/PlayerSpriteShadowCaster";
        public const string ContactShadowShaderResourcePath =
            "Shaders/PlayerContactShadow";

        private static Material shadowCasterMaterial;
        private static Material contactShadowMaterial;
        private static Mesh contactShadowMesh;

        public static Material ShadowCasterMaterial
        {
            get
            {
                if (shadowCasterMaterial == null)
                {
                    Shader shader = Resources.Load<Shader>(
                        ShadowCasterShaderResourcePath);
                    if (shader == null)
                    {
                        throw new InvalidOperationException(
                            $"Missing Resources shader " +
                            $"'{ShadowCasterShaderResourcePath}'.");
                    }

                    shadowCasterMaterial = new Material(shader)
                    {
                        name = "Player Sprite Shadow Caster Shared",
                        hideFlags = HideFlags.HideAndDontSave,
                        enableInstancing = true
                    };
                }

                return shadowCasterMaterial;
            }
        }

        public static Material ContactShadowMaterial
        {
            get
            {
                if (contactShadowMaterial == null)
                {
                    Shader shader = Resources.Load<Shader>(
                        ContactShadowShaderResourcePath);
                    if (shader == null)
                    {
                        throw new InvalidOperationException(
                            $"Missing Resources shader " +
                            $"'{ContactShadowShaderResourcePath}'.");
                    }

                    contactShadowMaterial = new Material(shader)
                    {
                        name = "Player Ground Contact Shadow Shared",
                        hideFlags = HideFlags.HideAndDontSave,
                        enableInstancing = true
                    };
                }

                return contactShadowMaterial;
            }
        }

        public static Mesh ContactShadowMesh
        {
            get
            {
                if (contactShadowMesh == null)
                {
                    contactShadowMesh = CreateContactShadowMesh();
                }

                return contactShadowMesh;
            }
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            DestroyCachedObject(shadowCasterMaterial);
            DestroyCachedObject(contactShadowMaterial);
            DestroyCachedObject(contactShadowMesh);
            shadowCasterMaterial = null;
            contactShadowMaterial = null;
            contactShadowMesh = null;
        }

        private static void DestroyCachedObject(
            UnityEngine.Object cachedObject)
        {
            if (cachedObject == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(cachedObject);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(cachedObject);
            }
        }

        private static Mesh CreateContactShadowMesh()
        {
            var mesh = new Mesh
            {
                name = "Shared Player Ground Contact Quad",
                hideFlags = HideFlags.HideAndDontSave,
                vertices = new[]
                {
                    new Vector3(-0.5f, 0f, -0.5f),
                    new Vector3(-0.5f, 0f, 0.5f),
                    new Vector3(0.5f, 0f, 0.5f),
                    new Vector3(0.5f, 0f, -0.5f)
                },
                normals = new[]
                {
                    Vector3.up,
                    Vector3.up,
                    Vector3.up,
                    Vector3.up
                },
                uv = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(0f, 1f),
                    new Vector2(1f, 1f),
                    new Vector2(1f, 0f)
                },
                triangles = new[]
                {
                    0, 1, 2,
                    0, 2, 3
                }
            };
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }
    }
}
