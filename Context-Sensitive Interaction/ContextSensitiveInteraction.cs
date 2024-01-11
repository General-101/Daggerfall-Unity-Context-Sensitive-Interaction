using UnityEngine;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using DaggerfallWorkshop;
using DaggerfallConnect;
using System.Reflection;

namespace ContextSensitiveInteractionMod
{
    //https://en.uesp.net/wiki/Daggerfall:Interaction_Mode
    public class ContextSensitiveInteraction : MonoBehaviour
    {
        static Mod mod;

        Camera mainCamera;
        int playerLayerMask = 0;
        PlayerActivate playerActivate;

        const float RayDistance = 3072 * MeshReader.GlobalScale;

        [Invoke(StateManager.StateTypes.Start, 0)]
        public static void Init(InitParams initParams)
        {
            mod = initParams.Mod;

            var go = new GameObject(mod.Title);
            go.AddComponent<ContextSensitiveInteraction>();

            mod.IsReady = true;
        }

        void Start()
        {
            mainCamera = GameManager.Instance.MainCamera;
            playerLayerMask = ~(1 << LayerMask.NameToLayer("Player"));
            playerActivate = GameManager.Instance.PlayerActivate;
        }

        void Update()
        {
            //if no camera, skip
            if (mainCamera == null) return;

            //update mode
            //playerActivate.ChangeInteractionMode(GetMode());
            playerActivate.GetType().GetField("currentMode", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(playerActivate, GetMode());
        }

        PlayerActivateModes GetMode()
        {
            // Fire ray into scene from active mouse cursor or camera
            Ray ray = new Ray();
            if (GameManager.Instance.PlayerMouseLook.cursorActive)
            {
                if (DaggerfallUnity.Settings.RetroRenderingMode > 0)
                {
                    // Need to scale screen mouse position to match actual viewport area when retro rendering enabled
                    // Also need to account for when large HUD is enabled and docked as this changes the retro viewport area
                    // Undocked large HUD does not change retro viewport area
                    float largeHUDHeight = 0;
                    if (DaggerfallUI.Instance.DaggerfallHUD != null && DaggerfallUI.Instance.DaggerfallHUD.LargeHUD.Enabled && DaggerfallUnity.Settings.LargeHUDDocked)
                        largeHUDHeight = DaggerfallUI.Instance.DaggerfallHUD.LargeHUD.ScreenHeight;
                    float xm = Input.mousePosition.x / Screen.width;
                    float ym = (Input.mousePosition.y - largeHUDHeight) / (Screen.height - largeHUDHeight);
                    Vector2 retroMousePos = new Vector2(mainCamera.targetTexture.width * xm, mainCamera.targetTexture.height * ym);
                    ray = mainCamera.ScreenPointToRay(retroMousePos);
                }
                else
                {
                    // Ray from mouse position into viewport
                    ray = mainCamera.ScreenPointToRay(Input.mousePosition);
                }
            }
            else
            {
                // Ray from camera crosshair position
                ray = new Ray(mainCamera.transform.position, mainCamera.transform.forward);
            }

            // Test ray against scene
            RaycastHit hit;
            bool hitSomething = Physics.Raycast(ray, out hit, RayDistance, playerLayerMask);
            if (hitSomething)
            {
                if (hit.transform.GetComponent<MobilePersonNPC>() ||
                    hit.transform.GetComponent<DaggerfallEnemy>())
                {
                    //if mobile npc/enemy
                    return InputManager.Instance.HasAction(InputManager.Actions.Sneak) ?
                        PlayerActivateModes.Steal :
                        PlayerActivateModes.Talk;
                }

                if (hit.transform.GetComponent<StaticNPC>())
                {
                    //if static npc
                    return PlayerActivateModes.Talk;
                }

                if (hit.transform.GetComponent<DaggerfallBulletinBoard>())
                {
                    //if bulletin board
                    return PlayerActivateModes.Info;
                }

                Transform doorOwner;
                DaggerfallStaticDoors doors = playerActivate.GetDoors(hit.transform, out doorOwner);
                if (doors)   //if (doors && playerEnterExit)
                {
                    //if door
                    return InputManager.Instance.HasAction(InputManager.Actions.Sneak) ?
                        PlayerActivateModes.Steal :
                        PlayerActivateModes.Grab;
                }

                Transform buildingOwner;
                StaticBuilding building = new StaticBuilding();
                DaggerfallStaticBuildings buildings = playerActivate.GetBuildings(hit.transform, out buildingOwner);
                if (buildings && buildings.HasHit(hit.point, out building))
                {
                    //if building
                    return PlayerActivateModes.Info;
                }
            }

            //grab as default
            return PlayerActivateModes.Grab;
        }
    }
}
