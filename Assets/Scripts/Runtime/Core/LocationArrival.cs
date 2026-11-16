using UnityEngine;

namespace BarPromenade
{
    internal interface IResidentArrival
    {
        void Resume();
    }

    internal static class LocationArrival
    {
        internal static void PlacePlayer(PlayerRuntime player, Vector3 position, Quaternion rotation)
        {
            player.Motor.Teleport(position);
            player.GameObject.transform.rotation = rotation;
            player.Motor.SetInputEnabled(true);
        }

        internal static void EnterInterior(PlayerRuntime player, Vector3 position, Camera camera,
            IntoxicationStatusController status, IntoxicationHudView hud)
        {
            PlacePlayer(player, position, Quaternion.identity);
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            follow.Initialize(camera, player.GameObject.transform, true);
            status.Initialize(player, follow, hud);
        }

        internal static void Retire(GameObject host)
        {
            if (host == null) return;
            host.SetActive(false);
            Object.Destroy(host);
        }
    }
}
