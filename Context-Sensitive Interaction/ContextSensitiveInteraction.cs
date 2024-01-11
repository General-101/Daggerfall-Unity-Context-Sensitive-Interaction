using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DaggerfallConnect;
using DaggerfallConnect.Save;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Utility;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Guilds;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Utility.AssetInjection;
using DaggerfallWorkshop.Game.Utility.ModSupport.ModSettings;

namespace ContextSensitiveInteractionMod
{
    public class ContextSensitiveInteraction : MonoBehaviour
    {
        static Mod mod;

        //Use the farthest distance
        const float rayDistance = PlayerActivate.StaticNPCActivationDistance;

        const float standardLevitateMoveSpeed = 4.0f;

        Camera playerCamera;
        int playerLayerMask = 0;
        GameObject player;

        PlayerMotor playerMotor;
        PlayerGroundMotor groundMotor;
        PlayerSpeedChanger speedChanger;
        ClimbingMotor climbingMotor;
        LevitateMotor levitateMotor;
        PlayerActivate playerActivate;
        float levitateMoveSpeed = standardLevitateMoveSpeed;
        float moveSpeed = standardLevitateMoveSpeed;
        Vector3 moveDirection = Vector3.zero;

        Transform prevHit;
        bool prevIsStealth;

        [Invoke(StateManager.StateTypes.Start, 0)]
        public static void Init(InitParams initParams)
        {
            mod = initParams.Mod;

            var go = new GameObject(mod.Title);
            go.AddComponent<ContextSensitiveInteraction>();
        }

        public void Start()
        {
            playerCamera = GameManager.Instance.MainCamera;
            playerLayerMask = ~(1 << LayerMask.NameToLayer("Player"));
            player = GameObject.FindWithTag("Player");

            playerMotor = player.GetComponent<PlayerMotor>();
            groundMotor = player.GetComponent<PlayerGroundMotor>();
            speedChanger = player.GetComponent<PlayerSpeedChanger>();
            climbingMotor = player.GetComponent<ClimbingMotor>();
            levitateMotor = player.GetComponent<LevitateMotor>();
            playerActivate = GameManager.Instance.PlayerActivate;

            mod.IsReady = true;
        }

        public void Update()
        {
            Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);

            RaycastHit hit;
            bool hitSomething = Physics.Raycast(ray, out hit, rayDistance, playerLayerMask);

            bool isHitSame = hit.transform == prevHit;
            bool isStealthSame = (speedChanger.isSneaking || playerMotor.IsCrouching) == prevIsStealth;

            if (!levitateMotor.IsLevitating)
            {
                SetMovement();
            }
            if (hitSomething)
            {
                prevHit = hit.transform;
                prevIsStealth = (speedChanger.isSneaking || playerMotor.IsCrouching);
                if (!isHitSame || !isStealthSame)
                {
                    if (!(hit.transform.name.Length > 16 && hit.transform.name.Substring(0, 17) == "DaggerfallTerrain"))
                    {
                        playerActivate.ChangeInteractionMode(GetInteractionMode(hit), false);
                    }
                }
            }
        }

        private PlayerActivateModes GetInteractionMode(RaycastHit hit)
        {
            object comp;

            // Objects with "Mobile NPC" activation distances
            if (hit.distance <= PlayerActivate.MobileNPCActivationDistance)
            {
                if (CheckComponent<MobilePersonNPC>(hit, out comp))
                {
                    //Debug.Log("NPC Found");
                    return (speedChanger.isSneaking || playerMotor.IsCrouching) ? PlayerActivateModes.Steal : PlayerActivateModes.Talk;
                }
                else if (CheckComponent<DaggerfallEntityBehaviour>(hit, out comp))
                {
                    //Debug.Log("Entity Found");
                    return (speedChanger.isSneaking || playerMotor.IsCrouching) ? PlayerActivateModes.Steal : PlayerActivateModes.Talk;
                }
                else if (CheckComponent<DaggerfallBulletinBoard>(hit, out comp))
                {
                    //Debug.Log("Billboard Found");
                    return PlayerActivateModes.Grab;
                }
            }

            // Objects with "Static NPC" activation distances
            if (hit.distance <= PlayerActivate.StaticNPCActivationDistance)
            {
                if (CheckComponent<StaticNPC>(hit, out comp))
                {
                    if (CheckComponent<DaggerfallBillboard>(hit, out comp))
                    {
                        //Debug.Log("NPC Billboard Found");
                        return PlayerActivateModes.Grab;
                    }
                    //Debug.Log("NPC Found");
                    return PlayerActivateModes.Talk;
                }
            }

            // Objects with "Default" activation distances
            if (hit.distance <= PlayerActivate.DefaultActivationDistance)
            {
                if (CheckComponent<DaggerfallAction>(hit, out comp))
                {
                    //Debug.Log("Interactable Found");
                    return (speedChanger.isSneaking || playerMotor.IsCrouching) ? PlayerActivateModes.Steal : PlayerActivateModes.Grab;
                }
                else if (CheckComponent<DaggerfallLadder>(hit, out comp))
                {
                    //Debug.Log("Ladder Found");
                    return PlayerActivateModes.Grab;
                }
                else if (CheckComponent<DaggerfallBookshelf>(hit, out comp))
                {
                    //Debug.Log("Bookshelf Found");
                    return PlayerActivateModes.Grab;
                }
                else if (CheckComponent<QuestResourceBehaviour>(hit, out comp))
                {
                    var qrb = (QuestResourceBehaviour)comp;

                    if (qrb.TargetResource != null)
                    {
                        if (qrb.TargetResource is Item)
                        {
                            if (CheckComponent<DaggerfallBillboard>(hit, out comp))
                            {
                                //Debug.Log("Billboard Found");
                                return PlayerActivateModes.Grab;
                            }
                        }
                    }
                }
            }

            // Checking for loot
            // Corpses have a different activation distance than other containers/loot
            if (CheckComponent<DaggerfallLoot>(hit, out comp))
            {
                var loot = (DaggerfallLoot)comp;

                // If a corpse, and within the corpse activation distance..
                if (loot.ContainerType == LootContainerTypes.CorpseMarker && hit.distance <= PlayerActivate.CorpseActivationDistance)
                {
                    //Debug.Log("Corpse Found");
                    return PlayerActivateModes.Grab;
                }
                else if (hit.distance <= PlayerActivate.TreasureActivationDistance)
                {
                    //Debug.Log("Treasrure Found");
                    return PlayerActivateModes.Grab;
                }
            }

            // Objects with the "Door" activation distances
            if (hit.distance <= PlayerActivate.DoorActivationDistance)
            {
                if (CheckComponent<DaggerfallActionDoor>(hit, out comp))
                {
                    //Debug.Log("Action Door Found");
                    return (speedChanger.isSneaking || playerMotor.IsCrouching) ? PlayerActivateModes.Steal : PlayerActivateModes.Grab;
                }
            }

            Transform doorOwner;
            DaggerfallStaticDoors doors = playerActivate.GetDoors(hit.transform, out doorOwner);
            if (doors)
            {
                Vector3 closestDoorPosition;
                int doorIndex;
                doors.FindClosestDoorToPlayer(player.transform.position, -1, out closestDoorPosition, out doorIndex);
                float dist = Vector3.Distance(closestDoorPosition, player.transform.position);
                if (hit.distance <= PlayerActivate.DoorActivationDistance && dist <= PlayerActivate.DoorActivationDistance)
                {
                    //Debug.Log("Door Found");
                    return (speedChanger.isSneaking || playerMotor.IsCrouching) ? PlayerActivateModes.Steal : PlayerActivateModes.Grab;        
                }
            }

            Transform buildingOwner;
            DaggerfallStaticBuildings buildings = playerActivate.GetBuildings(hit.transform, out buildingOwner);
            if (buildings)
            {
                //Debug.Log("Building Found");
                return PlayerActivateModes.Info;
            }

            return PlayerActivateModes.Grab;
        }

        private void SetMovement()
        {
            if (!playerMotor || !playerCamera || !levitateMotor.IsSwimming)
                return;

            // Cancel levitate movement if player is paralyzed
            if (GameManager.Instance.PlayerEntity.IsParalyzed)
                return;

            float inputX = InputManager.Instance.Horizontal;
            float inputY = InputManager.Instance.Vertical;

            // Up/down
            Vector3 upDownVector = new Vector3 (0, 0, 0);
            bool overEncumbered = (GameManager.Instance.PlayerEntity.CarriedWeight * 4 > 250) && !levitateMotor.IsLevitating && !GameManager.Instance.PlayerEntity.GodMode;
            if ((inputX != 0.0f || inputY != 0.0f) && !overEncumbered)
            {
                float inputModifyFactor = (inputX != 0.0f && inputY != 0.0f && playerMotor.limitDiagonalSpeed) ? .7071f : 1.0f;
                AddMovement(playerCamera.transform.TransformDirection(new Vector3(inputX * inputModifyFactor, 0, inputY * inputModifyFactor)));
            }

            // Execute movement
            if (moveDirection == Vector3.zero)
            {  
                // Hack to make sure that the player can get pushed by moving objects if he's not moving
                const float pos = 0.0001f;
                groundMotor.MoveWithMovingPlatform(Vector3.up * pos);
                groundMotor.MoveWithMovingPlatform(Vector3.down * pos);
                groundMotor.MoveWithMovingPlatform(Vector3.left * pos);
                groundMotor.MoveWithMovingPlatform(Vector3.right * pos);
                groundMotor.MoveWithMovingPlatform(Vector3.forward * pos);
                groundMotor.MoveWithMovingPlatform(Vector3.back * pos);
            }

            groundMotor.MoveWithMovingPlatform(moveDirection);
            moveDirection = Vector3.zero;
        }

        void AddMovement(Vector3 direction, bool upOrDown = true)
        {
            // No up or down movement while swimming without using a float up/float down key, levitating while swimming, or sinking from encumbrance
            if (!upOrDown && levitateMotor.IsSwimming && !levitateMotor.IsLevitating)
                direction.y = 0;

            if (levitateMotor.IsSwimming && GameManager.Instance.PlayerEntity.IsWaterWalking)
            {
                // Swimming with water walking on makes player move at normal speed in water
                moveSpeed = GameManager.Instance.PlayerMotor.Speed;
                moveDirection += direction * moveSpeed;
                return;
            }
            else if (levitateMotor.IsSwimming && !levitateMotor.IsLevitating)
            {
                // Do not allow player to swim up out of water, as he would immediately be pulled back in, making jerky movement and playing the splash sound repeatedly
                if ((direction.y > 0) && (playerMotor.controller.transform.position.y + (50 * MeshReader.GlobalScale) - 0.93f) >=
                (GameManager.Instance.PlayerEnterExit.blockWaterLevel * -1 * MeshReader.GlobalScale) &&
                !levitateMotor.IsLevitating)
                    direction.y = 0;

                // There's a fixed speed for up/down movement in classic (use moveSpeed = 2 here to replicate)
                float baseSpeed = speedChanger.GetBaseSpeed();
                moveSpeed = speedChanger.GetSwimSpeed(baseSpeed);
            }

            moveDirection += direction * moveSpeed;

            // Reset to levitate speed in case it has been changed by swimming
            moveSpeed = levitateMoveSpeed;
        }

        private bool CheckComponent<T>(RaycastHit hit, out object obj)
        {
            obj = hit.transform.GetComponent<T>();
            return obj != null;
        }
    }
}
