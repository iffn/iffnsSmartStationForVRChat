
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
[RequireComponent(typeof(VRCStation))]
public class StationManager : UdonSharpBehaviour
{
    /*
    Functionalities:
    - 360 Chair for desktop
    - Adjustment for desktop
    - Only one person in station -> Low player id bias
    - Entry/Exit events
    - Current player in station
    */

    [UdonSynced] Vector3 syncedPlayerPosition;

    [SerializeField] KeyCode desktopMoveUpControl = KeyCode.PageUp;
    [SerializeField] KeyCode desktopMoveDownControl = KeyCode.PageDown;
    [SerializeField] KeyCode desktopMoveForwardControl = KeyCode.Home;
    [SerializeField] KeyCode desktopMoveBackwardControl = KeyCode.End;
    [SerializeField] KeyCode desktopMoveLeftControl = KeyCode.Insert;
    [SerializeField] KeyCode desktopMoveRightControl = KeyCode.Delete;
    [SerializeField] float desktopHeadXOffset = 0.25f;

    [SerializeField] UdonSharpBehaviour[] entryAndExitInformants;
    
    public VRCStation LinkedStation { get; private set; }
    public VRCPlayerApi SeatedPlayer { get; private set; }
    public bool LocalPlayerInStation { get; private set; } = false;
    Collider linkedCollider;


    float transitionSpeed = 0.2f;

    bool isOwner;

    Transform playerMover;

    public Vector3 preferredStationPosition = 0.6f * Vector3.down;

    void ResetStationPosition()
    {
        playerMover.localPosition = preferredStationPosition;
    }

    private void Start()
    {
        LinkedStation = (VRCStation)GetComponent(typeof(VRCStation));

        playerMover = LinkedStation.stationEnterPlayerLocation;

        ResetStationPosition();

        isOwner = Networking.LocalPlayer.IsOwner(gameObject);

        linkedCollider = transform.GetComponent<Collider>();
    }

    public override void Interact()
    {
        LinkedStation.UseStation(Networking.LocalPlayer);
    }

    public float Remap(float iMin, float iMax, float oMin, float oMax, float iValue)
    {
        float t = Mathf.InverseLerp(iMin, iMax, iValue);
        return Mathf.Lerp(oMin, oMax, t);
    }

    private void Update()
    {
        if (SeatedPlayer == null || SeatedPlayer.IsUserInVR()) return;
        if (LinkedStation.PlayerMobility == VRCStation.Mobility.Mobile) return;

        //360 stuff
        Quaternion headRotation;

        //Rotation:
#if UNITY_EDITOR
        headRotation = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation;
#else
        headRotation = SeatedPlayer.GetBoneRotation(HumanBodyBones.Head);
#endif

        Quaternion relativeHeadRotation = Quaternion.Inverse(playerMover.rotation) * headRotation;
        float headHeading = relativeHeadRotation.eulerAngles.y;
        playerMover.localRotation = Quaternion.Euler(headHeading * Vector3.up);

        //Offset:
        float xOffset = 0;
        if (headHeading > 45 && headHeading < 180)
        {
            xOffset = Remap(iMin: 45, iMax: 90, oMin: 0, oMax: desktopHeadXOffset, iValue: headHeading);
        }
        else if (headHeading < 315 && headHeading > 180)
        {
            xOffset = -Remap(iMin: 315, iMax: 270, oMin: 0, oMax: desktopHeadXOffset, iValue: headHeading);
        }

        //Destktop movement stuff
        if (LocalPlayerInStation)
        {
            bool sync = false;

            if (Input.GetKey(desktopMoveUpControl))
            {
                preferredStationPosition += Time.deltaTime * transitionSpeed * Vector3.up;
                sync = true;
            }

            if (Input.GetKey(desktopMoveDownControl))
            {
                preferredStationPosition += Time.deltaTime * transitionSpeed * Vector3.down;
                sync = true;
            }

            if (Input.GetKey(desktopMoveForwardControl))
            {
                preferredStationPosition += Time.deltaTime * transitionSpeed * Vector3.forward;
                sync = true;
            }

            if (Input.GetKey(desktopMoveBackwardControl))
            {
                preferredStationPosition += Time.deltaTime * transitionSpeed * Vector3.back;
                sync = true;
            }

            if (Input.GetKey(desktopMoveLeftControl))
            {
                preferredStationPosition += Time.deltaTime * transitionSpeed * Vector3.left;
                sync = true;
            }

            if (Input.GetKey(desktopMoveRightControl))
            {
                preferredStationPosition += Time.deltaTime * transitionSpeed * Vector3.right;
                sync = true;
            }

            if (sync)
            {
                RequestSerialization();
            }

            playerMover.localPosition = preferredStationPosition + xOffset * Vector3.right;
        }
        else
        {
            playerMover.localPosition = syncedPlayerPosition + xOffset * Vector3.right;
        }
    }

    public override void OnOwnershipTransferred(VRCPlayerApi player)
    {
        if(player.isLocal)
        {
            isOwner = true;

            RequestSerialization();
        }
        else
        {
            if (isOwner)
            {
                preferredStationPosition = playerMover.localPosition;
            }

            isOwner = false;
        }
    }

    public override void OnPreSerialization()
    {
        syncedPlayerPosition = preferredStationPosition;
    }

    public override void OnDeserialization()
    {
        playerMover.localPosition = syncedPlayerPosition;
    }

    public override void InputJump(bool value, UdonInputEventArgs args)
    {
        if (value && LocalPlayerInStation && LinkedStation.disableStationExit)
        {
            LinkedStation.ExitStation(Networking.LocalPlayer);
        }
    }

    void InformOfLocalEntry()
    {
        foreach (UdonSharpBehaviour behavior in entryAndExitInformants)
        {
            behavior.SendCustomEvent("LocalPlayerEntered");
        }
    }

    void InformOfLocalExit()
    {
        foreach (UdonSharpBehaviour behavior in entryAndExitInformants)
        {
            behavior.SendCustomEvent("LocalPlayerExited");
        }
    }

    void InformOfRemoteEntry()
    {
        foreach (UdonSharpBehaviour behavior in entryAndExitInformants)
        {
            behavior.SendCustomEvent("RemotePlayerEntered");
        }
    }

    void InformOfRemoteExit()
    {
        foreach (UdonSharpBehaviour behavior in entryAndExitInformants)
        {
            behavior.SendCustomEvent("RemotePlayerExited");
        }
    }

    public override void OnStationEntered(VRCPlayerApi player)
    {
        //Check for double occupation
        if(SeatedPlayer != null)
        {
            //Kick the player out if they have a higher ID than the new player
            if (SeatedPlayer.playerId > player.playerId)
            {
                if (SeatedPlayer.isLocal)
                {
                    LinkedStation.ExitStation(SeatedPlayer);
                    InformOfLocalExit();
                }
                else
                {
                    InformOfLocalExit();
                }
            }
            else
            {
                return;
            }
        }

        SeatedPlayer = player;

        if (player.isLocal)
        {
            linkedCollider.enabled = false;

            LocalPlayerInStation = true;

            Networking.SetOwner(player, gameObject);

            InformOfLocalEntry();
        }
        else
        {
            InformOfRemoteEntry();
        }
    }

    public override void OnStationExited(VRCPlayerApi player)
    {
        if (SeatedPlayer != player) return;

        SeatedPlayer = null;

        if (player.isLocal)
        {
            linkedCollider.enabled = true;

            LocalPlayerInStation = false;

            InformOfLocalExit();
        }
        else
        {
            ResetStationPosition();

            InformOfRemoteExit();
        }
    }
}
