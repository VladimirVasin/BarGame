using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BarPromenade
{
    /// <summary>
    /// A black launch boundary that starts a fresh session and hands the
    /// visible opening directly to the existing Home interior.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MainMenuRoot : MonoBehaviour
    {
        public bool IsLoadingHome { get; private set; }
        public Camera LaunchCamera { get; private set; }

        private void Awake()
        {
            GameLog.SetScene(gameObject.scene.name);
            GameObject cameraObject =
                new GameObject("Black Launch Camera");
            cameraObject.transform.SetParent(transform, false);
            LaunchCamera = cameraObject.AddComponent<Camera>();
            LaunchCamera.clearFlags =
                CameraClearFlags.SolidColor;
            LaunchCamera.backgroundColor = Color.black;
            LaunchCamera.cullingMask = 0;
            LaunchCamera.allowHDR = false;
            LaunchCamera.allowMSAA = false;
            LaunchCamera.depth = -100f;
        }

        private IEnumerator Start()
        {
            yield return null;
            if (IsLoadingHome)
            {
                yield break;
            }

            IsLoadingHome = true;
            Time.timeScale = 1f;
            GameSessionState.BeginNewGame();
            GameSessionState.PrepareHomeArrival(
                HomeArrivalKind.OpeningSleep);
            AsyncOperation load = SceneManager.LoadSceneAsync(
                SceneIds.HomeInterior,
                LoadSceneMode.Single);
            if (load == null)
            {
                IsLoadingHome = false;
                Debug.LogError(
                    "The opening could not load the Home interior.");
                yield break;
            }

            while (!load.isDone)
            {
                yield return null;
            }
        }
    }
}
