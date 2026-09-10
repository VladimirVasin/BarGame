using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityCanneryController
    {
        private Transform shopLeaf;
        private Vector3 shopLeafClosed, shopHinge;
        private Quaternion shopLeafRotation;
        private BoxCollider ownedShopLeafCollider;
        private Transform portLeaf;
        private Vector3 portLeafClosed;
        private Vector3 portLeafScale;
        private Vector3 ShopInward => -(Route.ShopPose.Rotation * Vector3.right);

        private void CreateShopReceivingDoor()
        {
            if (!gameObject.activeInHierarchy) return;
            portLeaf=Require(port.Dock,"MOVE_EastLoadingDoor");
            portLeafClosed=portLeaf.position;
            portLeafScale=portLeaf.localScale;
            foreach (SupermarketExteriorAssetRegistry registry in transform.parent.GetComponentsInChildren<SupermarketExteriorAssetRegistry>())
                foreach (SupermarketExteriorPartBinding part in registry.Parts)
                    if (part.SourceName == "Closed Rear Service Door" && part.Renderer != null)
                    {
                        shopLeaf = part.Renderer.transform;
                        shopLeafClosed = shopLeaf.position;
                        shopLeafRotation = shopLeaf.rotation;
                        shopHinge = Route.ShopDoorPoint - (Route.ShopPose.Rotation * Vector3.forward) * .735f;
                        MeshFilter mesh = shopLeaf.GetComponent<MeshFilter>();
                        if (mesh == null || mesh.sharedMesh == null)
                            throw new System.InvalidOperationException("The shop receiving door needs its authored mesh for physical collision.");
                        Bounds bounds = mesh.sharedMesh.bounds;
                        ownedShopLeafCollider = shopLeaf.gameObject.AddComponent<BoxCollider>();
                        ownedShopLeafCollider.center = bounds.center;
                        ownedShopLeafCollider.size = bounds.size;
                        return;
                    }
        }

        private void ApplyShopReceivingDoor()
        {
            if(portLeaf!=null)
            {
                float raised=Snapshot.Stage==CityFishSupplyStage.LoadFish
                    ? Mathf.Min(Ease((float)Snapshot.Seconds/4),Ease((float)(Snapshot.Duration-Snapshot.Seconds)/4)) : 0;
                portLeaf.position=portLeafClosed+Vector3.up*(3.05f*raised);
                portLeaf.localScale=new Vector3(portLeafScale.x,portLeafScale.y*Mathf.Lerp(1,.02f,raised),portLeafScale.z);
            }
            if (shopLeaf == null) return;
            float open=Snapshot.Stage==CityFishSupplyStage.UnloadShop
                ? Mathf.Min(Ease((float)Snapshot.Seconds/4),Ease((float)(Snapshot.Duration-Snapshot.Seconds)/4)) : 0;
            Quaternion turn=Quaternion.AngleAxis(100*open,Vector3.up);
            shopLeaf.SetPositionAndRotation(shopHinge+turn*(shopLeafClosed-shopHinge),turn*shopLeafRotation);
        }

        private void RestoreShopReceivingDoor()
        {
            if (shopLeaf != null) shopLeaf.SetPositionAndRotation(shopLeafClosed,shopLeafRotation);
            if (ownedShopLeafCollider != null)
            {
                if (Application.isPlaying) Destroy(ownedShopLeafCollider); else DestroyImmediate(ownedShopLeafCollider);
                ownedShopLeafCollider = null;
            }
            if (portLeaf != null) { portLeaf.position=portLeafClosed; portLeaf.localScale=portLeafScale; }
        }
    }
}
