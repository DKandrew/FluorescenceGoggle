#region Unity Build

#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// This is a fake NirImageManager Class created to pass the Unity build process
public class NirImageManager : MonoBehaviour
{
    // Use this for initialization
    void Start()
    {
        Quaternion toQuat = Camera.main.transform.localRotation;
        toQuat.x = 0;
        toQuat.z = 0;
        this.transform.rotation = toQuat;

        // Lay the object down
        this.transform.Rotate(new Vector3(-90, 0, 0));
    }

    // Update is called once per frame
    void Update()
    {
        
    }

}

#endif

#endregion

#if !UNITY_EDITOR
using HoloToolkit.Sharing;
using HoloToolkit.Unity.InputModule;
using HoloToolkit.Unity.SpatialMapping;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class NirImageManager : MonoBehaviour, IInputClickHandler, IFocusable,
                                IManipulationHandler, INavigationHandler, ISpeechHandler
{
    private GazeStabilizer Stabilizer;

    // Use this for initialization
    void Start()
    {
        RegisterManagers();

        Stabilizer = new GazeStabilizer();

        GestureActionStartup();
        ImageDisplayStartup();
        UiControlStartup();
        SharingHololensStartup();
    }

    // Update is called once per frame
    void Update()
    {
        GestureActionUpdate();
        ImageDisplayUpdate();
        UiControlUpdate();
        SharingHololensUpdate();
    }

    private void OnDestroy()
    {
        UnregisterManagers();
    }

    private void RegisterManagers()
    {
        if(InputManager.Instance != null)
        {
            InputManager.Instance.AddGlobalListener(gameObject);
        }
    }

    private void UnregisterManagers()
    {
        if (InputManager.Instance != null)
        {
            InputManager.Instance.RemoveGlobalListener(gameObject);
        }
    }

    // ---------- Gesture Action ----------

    #region Gesture Action

    private bool IsPlacing = false;

    private bool IsManipulating = false;

    private bool IsNavigating = false;

    /// <summary>
    /// Keep track of the position for the manipulation gesture
    /// </summary>
    private Vector3 ManipulationPosition;
    private Vector3 ManipulationPreviousPosition;

    /// <summary>
    /// Keep track of the position (relative, from -1.0 to 1.0) from the navigation gesture
    /// </summary>
    private Vector3 RelativePosition;

    /// <summary>
    /// Enum for the current gesture state
    /// </summary>
    private enum GestureStateEnum
    {
        /// <summary>
        /// No Gesture is enabled
        /// </summary>
        None = -1,
        /// <summary>
        /// Tap to place
        /// </summary>
        Placement,
        /// <summary>
        /// Manipulation in vertical direction 
        /// </summary>
        Manipulation_V,
        /// <summary>
        /// Manipulation in horizonal direction
        /// </summary>
        Manipulation_H,
        /// <summary>
        /// Rotation in horizonal direction
        /// </summary>
        Rotation
    }

    private GestureStateEnum gestureState = GestureStateEnum.None;

    /// <summary>
    /// Perform the gesture action in the Start()
    /// </summary>
    private void GestureActionStartup()
    {
        // By default the NirImage accepts placement gesture
        ChangeGestureState(GestureStateEnum.Placement);

        RotateImageToFaceUser();
    }

    /// <summary>
    /// Perform the gesture action in the Update() 
    /// </summary>
    private void GestureActionUpdate()
    {
        // If this HoloLen is a slave, no gesture action is allowed
        if (!IsMaster)
        {
            ChangeGestureState(GestureStateEnum.None);
            return;
        }

        // Check placement
        if (IsPlacing)
        {
            PlaceImage();
        }

        DisplaySpatialMappingMesh(IsPlacing);

        // Check manipulation
        if (IsManipulating)
        {
            ManipulateImage();
        }

        // Check rotation
        if (IsNavigating)
        {
            RotateImage();
        }
    }

    /// <summary>
    /// Change the gesture state to new state
    /// </summary>
    /// <param name="NewState"></param>
    private void ChangeGestureState(GestureStateEnum NewState)
    {
        // If we are currently in placing mode we cannot change the gesture state
        if (!IsPlacing && !IsManipulating && !IsNavigating)
        {
            //if (gestureState == GestureStateEnum.Placement && NewState != GestureStateEnum.Placement)
            //{
            //    if (IsFocusing)
            //    {

            //    }
            //}


            //if (gestureState == GestureStateEnum.Placement && NewState != GestureStateEnum.Placement)
            //{ 
            //    if(InputManager.Instance != null)
            //    {
            //        InputManager.Instance.AddGlobalListener(gameObject);
            //    }
            //}
            //if (gestureState != GestureStateEnum.Placement && NewState == GestureStateEnum.Placement)
            //{
            //    // When move from other gestures to placement gesture, 
            //    // Unregister the global listener in the InputManager to be this game object so that it can stay responsive the the tap gesture
            //    if (InputManager.Instance != null)
            //    {
            //        InputManager.Instance.RemoveGlobalListener(gameObject);
            //    }
            //}

            gestureState = NewState;
        }
    } 

    /// <summary>
    /// This function gets trigged when user tapped. 
    /// When user tapped the NirImage, we may go to the placing mode
    /// </summary>
    /// <param name="eventData">Event data related to this input</param>
    public void OnInputClicked(InputClickedEventData eventData)
    {
        if(gestureState == GestureStateEnum.Placement)
        {
            IsPlacing = !IsPlacing;
        }
        else
        {
            IsPlacing = false;
        }
    }

    /// <summary>
    /// Triggered when user gaze on this object
    /// </summary>
    public void OnFocusEnter()
    {
        // Unregister the object from GlobalListener
        if (InputManager.Instance != null)
        {
            InputManager.Instance.RemoveGlobalListener(gameObject);
        }
    }

    /// <summary>
    /// Triggered when user leave the object
    /// </summary>
    public void OnFocusExit()
    {
        // Register the object from GlobalListener
        if (InputManager.Instance != null)
        {
            InputManager.Instance.AddGlobalListener(gameObject);
        }
    }

    /// <summary>
    /// Speed (the larger, the faster) at which the object settles to the surface upon placement.
    /// </summary>
    private float placementVelocity = 0.1f;

    // The most recent distance to the surface.  This is used to 
    // locate the object when the user's gaze does not intersect
    // with the Spatial Mapping mesh.
    private float lastDistance = 2.0f;

    /// <summary>
    /// Run image placement 
    /// </summary>
    private void PlaceImage()
    {
        Vector3 moveTo = this.transform.position;

        // Stabilize gaze
        Stabilizer.UpdateStability(Camera.main.transform.position, Camera.main.transform.rotation);

        var headPosition = Stabilizer.StablePosition;
        var gazeDirection = Stabilizer.StableRay.direction;

        // Do a raycast into the world that will only hit the Spatial Mapping mesh.
        RaycastHit hitInfo;
        bool hit = Physics.Raycast(headPosition, gazeDirection, out hitInfo,
                            30.0f, SpatialMappingManager.Instance.LayerMask);

        if (hit)
        {
            // Create a little buffer for the object to the surface
            Vector3 distanceFromSurface = 0.01f * hitInfo.normal;
            moveTo = hitInfo.point + distanceFromSurface;

            lastDistance = hitInfo.distance;
        }
        else
        {
            // If nothing is hit, keep the image in the last detect distance
            moveTo = headPosition + (gazeDirection * lastDistance);
        }

        // Follow the user's gaze
        float dist = Mathf.Abs((this.transform.position - moveTo).magnitude);
        this.transform.position = Vector3.Lerp(this.transform.position, moveTo, placementVelocity / dist);

        // Rotate this object to face the user.
        RotateImageToFaceUser();
    }

    /// <summary>
    /// Rotate the image and make it face toward user
    /// </summary>
    private void RotateImageToFaceUser()
    {
        Quaternion toQuat = Camera.main.transform.localRotation;
        toQuat.x = 0;
        toQuat.z = 0;
        this.transform.rotation = toQuat;

        // Lay the object down
        this.transform.Rotate(new Vector3(-90, 0, 0));
    }

    /// <summary>
    /// Determine whether to display Spatial Mapping Mesh
    /// </summary>
    /// <param name="signal">The control signal</param>
    private void DisplaySpatialMappingMesh(bool signal)
    {
        // If the user is in placing mode, display the spatial mapping mesh.
        if (signal)
        {
            SpatialMappingManager.Instance.DrawVisualMeshes = true;
        }
        // If the user is not in placing mode, hide the spatial mapping mesh.
        else
        {
            SpatialMappingManager.Instance.DrawVisualMeshes = false;
        }
    }

    // Manipulation Event Start, Updated, Completed, Canceled
    public void OnManipulationStarted(ManipulationEventData eventData)
    {
        ManipulationStateChange(true);
        ManipulationPosition = eventData.CumulativeDelta;
        ManipulationPreviousPosition = ManipulationPosition;      
    }

    public void OnManipulationUpdated(ManipulationEventData eventData)
    {
        ManipulationStateChange(true);
        ManipulationPosition = eventData.CumulativeDelta;
    }

    public void OnManipulationCompleted(ManipulationEventData eventData)
    {
        ManipulationStateChange(false);
    }

    public void OnManipulationCanceled(ManipulationEventData eventData)
    {
        ManipulationStateChange(false);
    }

    /// <summary>
    /// This function is called to change the IsManipulating variable and add/remove the gameObject from the InputManager Global Listener
    /// </summary>
    /// <param name="NewIsManipulating"></param>
    private void ManipulationStateChange(bool NewIsManipulating)
    {
        if (gestureState == GestureStateEnum.Manipulation_V || gestureState == GestureStateEnum.Manipulation_H)
        {
            IsManipulating = NewIsManipulating;
        }
        else
        {
            // Disable manipulating gesture when gesture state is not related to manipulation
            IsManipulating = false;
        }
    }

    /// <summary>
    /// Change the NirImage position based on the manipulation gesture
    /// </summary>
    private void ManipulateImage()
    {
        Vector3 move = Vector3.zero;

        Vector3 distance = ManipulationPosition - ManipulationPreviousPosition;

        if (gestureState == GestureStateEnum.Manipulation_V)
        {
            move.y = distance.y;
        }
        if (gestureState == GestureStateEnum.Manipulation_H)
        {
            move = distance;
            move.y = 0;
        }

        // Update the manipulationPreviousPosition with the current position.
        ManipulationPreviousPosition = ManipulationPosition;

        // Increment this transform's position by the moveVector.
        this.transform.position += move;
    }

    // Navigation Event Start, Updated, Completed, Canceled
    public void OnNavigationStarted(NavigationEventData eventData)
    {
        NavigationStateChange(true);
        RelativePosition = eventData.CumulativeDelta;
    }

    public void OnNavigationUpdated(NavigationEventData eventData)
    {
        NavigationStateChange(true);
        RelativePosition = eventData.CumulativeDelta;
    }

    public void OnNavigationCompleted(NavigationEventData eventData)
    {
        NavigationStateChange(false);
    }

    public void OnNavigationCanceled(NavigationEventData eventData)
    {
        NavigationStateChange(false);
    }

    /// <summary>
    /// Call to change the IsNavigating variable and add/remove the gameObject from the InputManager Global Listener
    /// </summary>
    /// <param name="NewIsNavigating"></param>
    private void NavigationStateChange(bool NewIsNavigating)
    {
        if (gestureState == GestureStateEnum.Rotation)
        {
            IsNavigating = NewIsNavigating;
        }
        else
        {
            IsNavigating = false;
        }
    }

    /// <summary>
    /// Rotate the NirImage based on the navigation gesture
    /// </summary>
    private void RotateImage()
    {
        // Rotation sensitivity (unit is in degree), the larger it is, the faster rotation is
        float RotationSensitivity = 5.0f;

        // Calculate rotationFactor based on RelativePosition.X and multiply by RotationSensitivity.
        float rotationFactor = RelativePosition.x * RotationSensitivity;
        // Rotate along the Y axis using rotationFactor.
        this.transform.Rotate(new Vector3(0, 0, -1 * rotationFactor));
    }

    #endregion

    // ---------- Voice Command ----------

    #region Voice Command

    public void OnSpeechKeywordRecognized(SpeechKeywordRecognizedEventData eventData)
    {
        // If this HoloLens is a slave, disable all the voice command
        if (!IsMaster)
        {
            return;
        }

        switch (eventData.RecognizedText.ToLower())
        {
            case "move image":          // By default we move the NirImage vertically
            case "move vertically":
                ChangeGestureState(GestureStateEnum.Manipulation_V);
                break;
            case "move horizontally":
                ChangeGestureState(GestureStateEnum.Manipulation_H);
                break;
            case "place image":
                ChangeGestureState(GestureStateEnum.Placement);
                break;
            case "rotate image":
                ChangeGestureState(GestureStateEnum.Rotation);
                break;
            case "move complete":
                ChangeGestureState(GestureStateEnum.None);
                break;
            case "begin network":
                StartNetwork();
                break;
            case "close network":
                CloseNetwork();
                break;
        }
        print("NirImageManager Voice command: " + eventData.RecognizedText.ToLower());
    }

    #endregion

    // ---------- Image Display ----------

    #region Image Display

    // Set the number of pixel in the NIR camera
    private const int numPixel_x = 648, numPixel_y = 488;

    // Set the caliberation board length and width (in inch)
    private const float caliberation_len = 6f, caliberation_wid = 4.5f;
    private const int downsamplingFactor = 2;         // The downsampling factor for acquire the image from camera
    private const int downPixel_x = numPixel_x / downsamplingFactor, downPixel_y = numPixel_y / downsamplingFactor;

    // Texture to write image data
    private Texture2D t;

    // Define the size of the data from the server
    private const int imageDataSize = downPixel_x * downPixel_y * 2;

    // Image data to be displayed
    private byte[] imageData;

    // A buffer hold the next frame's image
    private byte[] imageData_buffer;

    /// <summary>
    /// The IP address of the server
    /// </summary>
    private string ipAddress = "192.168.1.2";

    /// <summary>
    /// The streaming port
    /// </summary>
    private int port = 27015;

    /// <summary>
    /// The network client that receives stream data from server
    /// </summary>
    private NetworkClient Client_Stream = new NetworkClient();
    
    /// <summary>
    /// A flag indicating whether new stream data is available
    /// </summary>
    private bool StreamDataUpdated = false;

    /// <summary>
    /// Convert unit from inch to meter
    /// </summary>
    /// <param name="inch"></param>
    /// <returns>Meter</returns>
    private float inch2meter(float inch)
    {
        return inch * 0.0254f;
    }

    /// <summary>
    /// Controls if we need to Issue Read Data
    /// </summary>
    private bool IssuedReadData = false;

    /// <summary>
    /// Start up functions related to image display
    /// </summary>
    private void ImageDisplayStartup()
    {
        // Adjust NirImage scale
        SetImageScale();

        // Load Default image 
        t = new Texture2D(downPixel_x, downPixel_y, TextureFormat.ARGB4444, false);
        imageData = LoadDefaultImage();
        displayImage(imageData);

        // Adjust the image pixel per unit
        float sprite_ppu = downPixel_x;     // ppu = pixel_per_unit
        SpriteRenderer sr = this.GetComponent<SpriteRenderer>();
        Sprite s = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), sprite_ppu);
        sr.sprite = s;

        // Find the cursor gameObject
        cursorObject = GameObject.Find("Cursor");
    }

    private void ImageDisplayUpdate()
    {
        if (Client_Stream.Connected)
        {
            if (!IssuedReadData)
            {
                ReadStreamData();
            }

            if (StreamDataUpdated)
            {
                imageData = imageData_buffer;
                displayImage(imageData);
                StreamDataUpdated = false;
            }
        }

        // Control the display of cursor
        if (Client_Stream.Connected && gestureState == GestureStateEnum.None)
        {
            ChangeCursorDisplay(false);
        }
        else
        {
            ChangeCursorDisplay(true);
        }

    }

    // This function will adjust the NIGManager scale to fit the actual caliberation board 6 x 4.5 (in inch)
    // To prevent distortion of the image (sprite), the NIGManager GameObject should always be a square.
    // Here we select to scale it to a 6x6 (inch) square. If you decide to change to another scale, the sprite_ppu for the Texture2D need to be changed as well
    // For example, scale to a 4.5x4.5 (inch), sprite_ppu = downPixel_y
    private void SetImageScale()
    {
        float len_meter = inch2meter(caliberation_len);
        //float wid_meter = inch2meter(caliberation_wid);

        Vector3 new_scale = new Vector3(len_meter, len_meter, 0.1f);
        this.transform.localScale = new_scale;
    }

    /// <summary>
    /// Generate a default image for the imageData
    /// </summary>
    /// <returns></returns>
    private byte[] LoadDefaultImage()
    {
        byte[] defaultImage = new byte[imageDataSize];

        for (int i = 0; i < defaultImage.Length; i++)
        {
            if (i < 648 * 488 / downsamplingFactor / downsamplingFactor)
            {
                if (i % 2 == 0)
                {
                    defaultImage[i] = 0x00;
                }
                else
                {
                    defaultImage[i] = 0xFF;
                }
            }
            else
            {
                if (i % 2 == 0)
                {
                    defaultImage[i] = 0xF0;
                }
                else
                {
                    defaultImage[i] = 0xF0;
                }
            }
        }

        return defaultImage;
    }

    /// <summary>
    /// Display image on the gameObject
    /// </summary>
    /// <param name="image">Display image buffer</param>
    private void displayImage(byte[] image)
    {
        t.LoadRawTextureData(image);
        t.Apply();
    }

    /// <summary>
    /// Connect to the network
    /// </summary>
    private async void StartNetwork()
    {
        if (!Client_Stream.Connected)
        {
            // Connect to the server
            await Client_Stream.ConnectToServer(ipAddress, port);

            // Send STEAM request
            string command = "STREAM\n";
            await Client_Stream.sendRequest(command);

            // Send Network status to sharing Hololens
            SendNirImageNetworkStatus();
        }
    }

    /// <summary>
    /// Read data from server,
    /// If success, StreamDataUpdated will be set up to true and data is stored in the imageData_buffer
    /// </summary>
    private async void ReadStreamData()
    {
        IssuedReadData = true;

        int readSize = imageDataSize;
        byte[] buffer = new byte[readSize];

        int rlen = await Client_Stream.readData(readSize, buffer);

        if (rlen == readSize)
        {
            imageData_buffer = buffer;
            StreamDataUpdated = true;
        }

        // Read function is complete here, set IssuedReadData back to false
        IssuedReadData = false;
    }

    /// <summary>
    /// Close the network, Set the NirImage back to default
    /// </summary>
    private void CloseNetwork()
    {
        if (Client_Stream.Connected)
        {
            Client_Stream.CloseConnection();

            imageData = LoadDefaultImage();
            displayImage(imageData);

            // Send Network status to sharing Hololens
            SendNirImageNetworkStatus();
        }
    }

    /// <summary>
    /// States to control the display of cursor
    /// </summary>
    private enum CursorDisplayEnum
    {
        /// <summary>
        /// Cursor display on
        /// </summary>
        On,
        /// <summary>
        /// Cursor display off
        /// </summary>
        Off
    }

    private CursorDisplayEnum cursorDisplay = CursorDisplayEnum.On;
    
    /// <summary>
    /// A reference to the cursor gameObject
    /// </summary>
    private GameObject cursorObject; 

    /// <summary>
    /// Change the display of the cursor
    /// </summary>
    /// <param name="NewState"></param>
    private void ChangeCursorDisplay(CursorDisplayEnum NewState)
    {
        if (NewState != cursorDisplay && cursorObject != null)
        {
            switch (NewState)
            {
                case CursorDisplayEnum.On:
                    cursorObject.SetActive(true);
                    break;
                case CursorDisplayEnum.Off:
                    cursorObject.SetActive(false);
                    break;
            }

            cursorDisplay = NewState;
        }
    }

    /// <summary>
    /// An overrided function to control the display of cursor
    /// If signal == true, the cursor display is enable. If signal == false, the cursor display is disable
    /// </summary>
    /// <param name="Signal"></param>
    private void ChangeCursorDisplay(bool Signal)
    {
        if (Signal)
        {
            ChangeCursorDisplay(CursorDisplayEnum.On);
        }
        else
        {
            ChangeCursorDisplay(CursorDisplayEnum.Off);
        }
    }

    #endregion

    // ---------- UI Control ----------

    #region UI Control

    private GameObject UI_arrow_x;
    private GameObject UI_arrow_y;
    private GameObject UI_arrow_z;
    private GameObject UI_circleArrow_1;
    private GameObject UI_circleArrow_2;
    private GameObject UI_text_object;
    private Text UiText;

    private void UiControlStartup()
    {
        UI_arrow_x = GameObject.Find("maniUI_arrow_x");
        UI_arrow_y = GameObject.Find("maniUI_arrow_y");
        UI_arrow_z = GameObject.Find("maniUI_arrow_z");
        UI_circleArrow_1 = GameObject.Find("maniUI_circleArrow_1");
        UI_circleArrow_2 = GameObject.Find("maniUI_circleArrow_2");

        UI_text_object = gameObject.transform.Find("Canvas/UI_Text").gameObject;
        UiText = UI_text_object.GetComponent<Text>();
    }

    private void UiControlUpdate()
    {
        // Note here we use SetActive to control the visibility of the gameObject
        // When the gameObject doesn't have renderer or scripts, the performance of SetActive() is ok.
        switch (gestureState)
        {
            case GestureStateEnum.None:
            case GestureStateEnum.Placement:
                UI_arrow_x.SetActive(false);
                UI_arrow_y.SetActive(false);
                UI_arrow_z.SetActive(false);
                UI_circleArrow_1.SetActive(false);
                UI_circleArrow_2.SetActive(false);
                break;
            case GestureStateEnum.Manipulation_V:
                UI_arrow_x.SetActive(false);
                UI_arrow_y.SetActive(false);
                UI_arrow_z.SetActive(true);
                UI_circleArrow_1.SetActive(false);
                UI_circleArrow_2.SetActive(false);
                break;
            case GestureStateEnum.Manipulation_H:
                UI_arrow_x.SetActive(true);
                UI_arrow_y.SetActive(true);
                UI_arrow_z.SetActive(false);
                UI_circleArrow_1.SetActive(false);
                UI_circleArrow_2.SetActive(false);
                break;
            case GestureStateEnum.Rotation:
                UI_arrow_x.SetActive(false);
                UI_arrow_y.SetActive(false);
                UI_arrow_z.SetActive(false);
                UI_circleArrow_1.SetActive(true);
                UI_circleArrow_2.SetActive(true);
                break;
        }

        // Control whether to display the UI text
        switch (gestureState)
        {
            case GestureStateEnum.Placement:
                if (!IsPlacing)
                {
                    UiText.text = "Tap the Image to Place";
                }
                else
                {
                    UiText.text = "";
                }
                break;
            default:
                UiText.text = "";
                break;
        }
    }

    #endregion

    // ---------- Sharing HoloLens ----------

    #region Sharing HoloLens

    /// <summary>
    /// Local reference indicating if this HoloLen is the master.
    /// It is always true if it doesn't connect to the sharing server.
    /// </summary>
    /// <remarks>By default, it is true when we are in the local model (No network connection)</remarks>
    private bool IsMaster
    {
        get
        {
            if (SharingStage.Instance != null)
            {
                if (SharingStage.Instance.IsOnlyUser)
                {
                    return true;
                }
                else
                {
                    return SharingStage.Instance.IsSharingMaster;
                }
            }
            else
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Only allow doing sharing program when we connected to a sharing server and there are multiple users connected to the server
    /// Because CheckIsOnlyUser will return true even when the server is not connected (since local user is the only user in the session tracker)
    /// </summary>
    private bool AllowSharing
    {
        get
        {
            if (SharingStage.Instance.IsConnected && !SharingStage.Instance.IsOnlyUser)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
    }

    private void SharingHololensStartup()
    {
        // Sharing Stage should be valid at this point, but it may not connect to the sharing server
        if (SharingStage.Instance.IsConnected)
        {
            SharingServerConnected();
        }
        else
        {
            SharingStage.Instance.SharingManagerConnected += SharingServerConnected;
        }

        // Attach callback functions to custom messages
        CustomMessages.Instance.MessageHandlers[CustomMessages.TestMessageID.NirImageTransform] = UpdateNirImageTransform;
        CustomMessages.Instance.MessageHandlers[CustomMessages.TestMessageID.NirImageNetworkStatus] = UpdateNirImageNetworkStatus;

        // Start sending the custom messages
        // Here I use StartCoroutine instead of Update is because Update will get fired every 1/60 s
        // But for the Transform, we don't need to send it so frequently. Therefore, use StartCoroutine can
        // help us reduce the amount of network traffic in the HoloLens
        StartCoroutine(SendCustomMessages());
    }

    private void SharingHololensUpdate()
    {
        
    }

    private void SharingHololensOnDestroy()
    {
        if (SharingStage.Instance != null)
        {
            if (SharingStage.Instance.SessionUsersTracker != null)
            {
                SharingStage.Instance.SessionUsersTracker.UserJoined -= UserJoinedSession;
                SharingStage.Instance.SessionUsersTracker.UserLeft -= UserLeftSession;
            }
        }
    }

    /// <summary>
    /// Replace the noraml Update() in Unity. Use this function to send the custom messages can reduce the traffic load of the network
    /// </summary>
    /// <returns></returns>
    private IEnumerator SendCustomMessages()
    {
        float delayTime = 0.05f;
        while (true)
        {
            if (IsMaster)
            {
                if (AllowSharing)
                {
                    SendNirImageTransform();
                }
            }
            else
            {
                break;
            }
            yield return new WaitForSeconds(delayTime);
        }
    }

    /// <summary>
    /// Calls when SharingStage connects to the sharing server
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void SharingServerConnected(object sender = null, EventArgs e = null)
    {
        // Unregister this function from the SharingManagerConnected
        SharingStage.Instance.SharingManagerConnected -= SharingServerConnected;

        SharingStage.Instance.SessionUsersTracker.UserJoined += UserJoinedSession;
        SharingStage.Instance.SessionUsersTracker.UserLeft += UserLeftSession;
    }

    /// <summary>
    /// Send the NirImage transform to other users
    /// </summary>
    public void SendNirImageTransform()
    {
        CustomMessages.Instance.SendNirImageTransform(this.transform.localPosition, this.transform.localRotation);
    }

    /// <summary>
    /// Send the NirImage network status to other users
    /// </summary>
    public void SendNirImageNetworkStatus()
    {
        CustomMessages.Instance.SendNirImageNetworkStatus(Client_Stream.Connected);
    }

    /// <summary>
    /// Called when the remote user sends a NirImage transform
    /// </summary>
    /// <param name="msg"></param>
    private void UpdateNirImageTransform(NetworkInMessage msg)
    {
        // Parse the message
        long userID = msg.ReadInt64();

        Vector3 pos = CustomMessages.Instance.ReadVector3(msg);
        Quaternion rot = CustomMessages.Instance.ReadQuaternion(msg);

        this.transform.localPosition = pos;
        this.transform.localRotation = rot;
    }

    /// <summary>
    /// Called when the remote user sends the network status of NirImage
    /// </summary>
    /// <param name="msg"></param>
    private void UpdateNirImageNetworkStatus(NetworkInMessage msg)
    {
        // Parse the message
        long userID = msg.ReadInt64();

        bool connected = CustomMessages.Instance.ReadBoolean(msg);
        if (connected && !Client_Stream.Connected)
        {
            StartNetwork();
        }
        if (!connected && Client_Stream.Connected)
        {
            CloseNetwork();
        }
    }

    /// <summary>
    /// Called when a user is joining the current session.
    /// </summary>
    /// <param name="user">User that joined the current session.</param>
    private void UserJoinedSession(User user)
    {
        if (user.GetID() != SharingStage.Instance.Manager.GetLocalUser().GetID())
        {
            SendNirImageNetworkStatus();
        }
    }

    /// <summary>
    /// Called when a new user is leaving the current session.
    /// </summary>
    /// <param name="user">User that left the current session.</param>
    private void UserLeftSession(User user)
    {
        if (user.GetID() != SharingStage.Instance.Manager.GetLocalUser().GetID())
        {
            
        }
    }

    #endregion
}

#endif