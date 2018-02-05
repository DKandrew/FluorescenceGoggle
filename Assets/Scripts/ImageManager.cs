using System;
using UnityEngine;
using UnityEngine.UI;
using HoloToolkit.Unity.InputModule;
using HoloToolkit.Sharing;

#region Unity Build

#if UNITY_EDITOR
public class ImageManager : MonoBehaviour
{
    private void Start()
    {

    }

    private void Update()
    {

    }
}
#endif

#endregion

#if !UNITY_EDITOR
using System.Threading.Tasks;

public class ImageManager : MonoBehaviour, ISpeechHandler, IFocusable
{

    void Start()
    {   
        // Register manager to events
        RegisterManager();

        ImageManagementStartup();
        ImageScalingStartup();
        UIControlStartup();
        SharingHololensStartup();
    }

    void Update()
    {
        ImageManagementUpdate();
        ImageScalingUpdate();
        UIControlUpdate();
        SharingHololensUpdate();
    }

    private void OnDestroy()
    {
        UnregisterManager();
        SharingHololensOnDestroy();
    }

    /// <summary>
    /// Register to the event that Image Manager needs
    /// </summary>
    private void RegisterManager()
    {
        if (InputManager.Instance != null)
        {
            InputManager.Instance.AddGlobalListener(gameObject);
        }
    }

    /// <summary>
    /// Unregister to the event that Image Manager needs
    /// </summary>
    private void UnregisterManager()
    {
        if (InputManager.Instance != null)
        {
            InputManager.Instance.RemoveGlobalListener(gameObject);
        }
    }

    // ---------- Image Management ----------

    #region Image Management

    /// <summary>
    /// Page number of the images, index starts from 1
    /// </summary>
    private int PageNum = 1;

    /// <summary>
    /// The maximum page number, this should get the information from server, by default it is 1
    /// </summary>
    private int MaxPageNum = 1;

    /// <summary>
    /// The number of image blocks that present to user at one time 
    /// </summary>
    private const int ImageBlockNum = 4;

    /// <summary>
    /// The total number of image available, this should get from server
    /// </summary>
    private int TotalImageNum = ImageBlockNum;

    /// <summary>
    /// An array of Image block objects
    /// </summary>
    /// <remarks>Each of them will hold a game object</remarks>
    private ImageBlock[] ImageBlocks = new ImageBlock[ImageBlockNum];

    // Global reference to the Images GameObject and the Canvas GameObject
    private GameObject ImagesGO, CanvasGO;

    // A bool indicates if manager is activated
    private bool ManagerActivated = true;

    /// <summary>
    /// The IP address of the server
    /// </summary>
    private string ipAddress = "192.168.1.2";

    /// <summary>
    /// The connection port
    /// </summary>
    private int port = 27015;

    /// <summary>
    /// The network client that receives data from server
    /// </summary>
    private NetworkClient netClient = new NetworkClient();

    /// <summary>
    /// A variable to keep track of the current visiblity state of the manager 
    /// </summary>
    private bool IsVisible = true;

    private enum ImageManagerStateEnum
    {
        Idle,
        /// <summary>
        /// Updating the max page number from server
        /// </summary>
        UpdateMaxPageNum,
        /// <summary>
        /// the maxpage number has been successfully updated
        /// </summary>
        UpdateMaxPageNum_Completed,
    }

    private ImageManagerStateEnum imageManagerState = ImageManagerStateEnum.Idle;

    private void ImageManagementStartup()
    {
        // Adjust manager transform
        SetManagerTransform();

        // Find the ImagesGO and CanvasGO
        ImagesGO = this.transform.Find("Images").gameObject;
        CanvasGO = this.transform.Find("Canvas").gameObject;

        // Find the Image Blocks objects
        ImageBlocks[0] = new ImageBlock(this.transform.Find("Images/Image_upLeft").gameObject);
        ImageBlocks[1] = new ImageBlock(this.transform.Find("Images/Image_upRight").gameObject);
        ImageBlocks[2] = new ImageBlock(this.transform.Find("Images/Image_downLeft").gameObject);
        ImageBlocks[3] = new ImageBlock(this.transform.Find("Images/Image_downRight").gameObject);

        // Adjust Image Block position
        float x = 0.6f, y = 0.6f;
        ImageBlocks[0].SetTransform(new Vector3(-x, y, 0));
        ImageBlocks[1].SetTransform(new Vector3(x, y, 0));
        ImageBlocks[2].SetTransform(new Vector3(-x, -y, 0));
        ImageBlocks[3].SetTransform(new Vector3(x, -y, 0));

        // Get the latest maximum page number
        UpdateMaxPageNum();

        LoadImages(PageNum);

        // Close manager display
        ShowManager(false);
    }

    private void ImageManagementUpdate()
    {
        FaceUser();
    }

    /// <summary>
    /// Show / not show the manager
    /// </summary>
    /// <param name="signal">the control signal, true == show, false == not show</param>
    private void ShowManager(bool signal)
    {
        if (IsVisible == signal)
        {
            return;
        }

        // Here we cannot directly use this.gameObject.SetActive(signal), because once this gameObject is disactivated, it will not accept new keywords and thus be unable to activate again. 
        ImagesGO.SetActive(signal);
        CanvasGO.SetActive(signal);
        ManagerActivated = signal;
        IsVisible = signal;

        // Tell other hololens about this operation
        SendXRayImageStatus();
    }

    /// <summary>
    /// Adjust the transform (position, scale and rotation) of the Image Manager so 
    /// that the Image Manager will always facing the Camera position when this function is called
    /// </summary>
    private void SetManagerTransform()
    {
        float ScaleNum = 0.15f;
        float DistanceFromUser = 2.0f;      // Placing Hologram at 2.0m is a comfort zone for the user

        Vector3 posi = Camera.main.transform.position + Camera.main.transform.forward * DistanceFromUser;
        Vector3 scale = new Vector3(ScaleNum, ScaleNum, ScaleNum);

        this.transform.position = posi;
        this.transform.localScale = scale;

        FaceUser();

        SendXRayImageTransform();
    }

    /// <summary>
    /// Adjust the Hologram's transform to face the user
    /// </summary>
    private void FaceUser()
    {
        Vector3 directionToTarget = Camera.main.transform.position - this.transform.position;

        // If we are right next to the camera the rotation is undefined. 
        if (directionToTarget.sqrMagnitude < 0.001f)
        {
            return;
        }

        // Calculate and apply the rotation required to reorient the object
        this.transform.rotation = Quaternion.LookRotation(-directionToTarget);
    }

    // Ask the server for the maxium page num
    private async void UpdateMaxPageNum()
    {
        if (imageManagerState == ImageManagerStateEnum.UpdateMaxPageNum)
        {
            // An update request has been issued, simply return
            return;
        }

        imageManagerState = ImageManagerStateEnum.UpdateMaxPageNum;

        // Update the total image number
        TotalImageNum = await GetTotalImageNum();

        // Update the Maximum page number
        MaxPageNum = TotalImageNum / ImageBlockNum;

        // Refresh the UI
        UI_UpdatePageNumber(PageNum);

        imageManagerState = ImageManagerStateEnum.UpdateMaxPageNum_Completed;
    }

    /// <summary>
    /// Get the total number of images available in the server side
    /// </summary>
    /// <returns></returns>
    private async Task<int> GetTotalImageNum()
    {
        // Connect to the server
        await netClient.ConnectToServer(ipAddress, port);

        // Send request to server
        string command = "GET XRAY TOTALNUM\n";
        bool success = await netClient.sendRequest(command);

        // Initialize the New TotalImageNum to the old TotalImageNum
        int NewTotalImageNum = TotalImageNum;

        // If request sends successfully, read the response
        if (success)
        {
            // We should read "OK\n[sizeof(int)]\n"
            // In the server, sizeof(int) == 4
            int ShouldReadLen = 3 + 4 + 1;
            byte[] buffer = new byte[ShouldReadLen];
            int rlen = await netClient.readData(ShouldReadLen, buffer);

            if (rlen == ShouldReadLen)
            {
                // First read 3 bytes to see if the response is "OK\n"
                int hsize = 3;
                byte[] header = new byte[hsize];
                Array.Copy(buffer, header, hsize);
                string hstr = System.Text.Encoding.UTF8.GetString(header);

                if (hstr == "OK\n")
                {
                    // Find the total image number
                    NewTotalImageNum = BitConverter.ToInt32(buffer, hsize);
                }
                else
                {
                    Debug.Log("ImageManager::GetTotalImageNum: Server response has error\n" + System.Text.Encoding.UTF8.GetString(buffer));
                }
            }
        }

        // Close connection
        netClient.CloseConnection();

        return NewTotalImageNum;
    }

    /// <summary>
    /// Based on the page number, load the image
    /// </summary>
    /// <param name="pageNum"></param>
    private void LoadImages(int pageNum)
    {
        int startIndex = (pageNum - 1) * ImageBlockNum + 1;
        for (int i = 0; i < ImageBlockNum; i++)
        {
            ImageBlocks[i].LoadImage(ipAddress, port, startIndex + i);
        }

        // Update page number 
        PageNum = pageNum;

        // Update the UI
        UI_UpdatePageNumber(pageNum);

        // Send message to other sharing Hololens
        SendXRayImageStatus();
    }

    /// <summary>
    /// Load the next page images
    /// </summary>
    private void LoadNextPage()
    {
        if (PageNum >= MaxPageNum || ScaledImageIdx != NO_IMAGE_SCALED)
        {
            return;
        }
        
        int newPageNum = PageNum + 1;
        LoadImages(newPageNum);
    }

    /// <summary>
    /// Load the previous page images
    /// </summary>
    private void LoadPrevPage()
    {
        if (PageNum <= 1 || ScaledImageIdx != NO_IMAGE_SCALED)
        {
            return;
        }
        
        int newPageNum = PageNum - 1;
        LoadImages(newPageNum);
    }

    // To prevent double handling of the input (like voice command)
    // We unregister the gameobject when it is focused, and register it back when it is unfocused
    public void OnFocusEnter()
    {
        // Unregister ImageManager from the global listener
        if (InputManager.Instance != null)
        {
            InputManager.Instance.RemoveGlobalListener(gameObject);
        }
    }

    public void OnFocusExit()
    {
        // Register imageManager back to the global listener
        if (InputManager.Instance != null)
        {
            InputManager.Instance.AddGlobalListener(gameObject);
        }
    }

    #endregion

    // ---------- Voice Control ----------

    #region Voice Control

    public void OnSpeechKeywordRecognized(SpeechKeywordRecognizedEventData eventData)
    {
        // Disable all the voice commands in slave Hololens
        if (!IsMaster)
        {
            return;
        }

        // When the manager is not activated, we only accept the following command
        if (!ManagerActivated)
        {
            switch (eventData.RecognizedText.ToLower())
            {
                case "show xray":
                    ShowManager(true);
                    break;
            }
        }
        // When the manager is activated, we accept the following command 
        else
        {
            switch (eventData.RecognizedText.ToLower())
            {
                case "close xray":
                    ShowManager(false);
                    break;
                case "reset xray":
                    SetManagerTransform();
                    break;
                case "zoom in one":
                    ZoomInImage(0, scale_factor);
                    break;
                case "zoom in two":
                    ZoomInImage(1, scale_factor);
                    break;
                case "zoom in three":
                    ZoomInImage(2, scale_factor);
                    break;
                case "zoom in four":
                    ZoomInImage(3, scale_factor);
                    break;
                case "zoom out":
                    ZoomOutImage();
                    break;
                case "next page":
                    LoadNextPage();
                    break;
                case "previous page":
                    LoadPrevPage();
                    break;
            }
        }

        //Debug.Log("Image manager Voice command: " + eventData.RecognizedText.ToLower());
    }

    #endregion

    // ---------- Image Scaling ----------

    #region Image Scaling

    /// <summary>
    /// A variable stored the index of the image block that has been scaled. (Index starts from 0)
    /// -1 means no image block has been scaled 
    /// </summary>
    private int ScaledImageIdx;
    private const int NO_IMAGE_SCALED = -1;     // A constant value for ScaledImageIdx

    /// <summary>
    /// Indicates if there is currently an image scaling
    /// Helpful for ImageManger to prevent multiple calls on voice command
    /// </summary>
    /// <remarks>More specifically, it prevents us registering the OnZoomOutComplete multiple times in the ZoomOutImage function</remarks>
    private bool IsScaling;

    /// <summary>
    /// The scale factor when the image zoom in
    /// </summary>
    private float scale_factor = 2.5f;

    private void ImageScalingStartup()
    {
        ScaledImageIdx = NO_IMAGE_SCALED;
        IsScaling = false;
    }

    private void ImageScalingUpdate()
    {
        // Run the state machine and the ImageBlock class will handle the rest. 
        for (int i = 0; i < ImageBlockNum; i++)
        {
            ImageBlocks[i].RunScaleSM();
        }
    }

    /// <summary>
    /// Zoom In an image
    /// </summary>
    /// <param name="idx">The index of the image; starting from 0 </param>
    /// <param name="scale_factor"></param>
    private void ZoomInImage(int idx, float scale_factor)
    {
        if (!IsScaling && ScaledImageIdx == NO_IMAGE_SCALED && idx < ImageBlockNum)
        {
            ImageBlocks[idx].RequestZoomIn(scale_factor);
            ScaledImageIdx = idx;
            IsScaling = true;
            // Register OnZoomInComplete to the Action
            ImageBlocks[idx].OnScaleComplete += OnZoomInComplete;

            // Close all other images
            ShowOtherImage(false, idx);

            // Tell other hololens about this operation
            SendXRayImageStatus();
        }
    }

    /// <summary>
    /// Zoom out an image
    /// </summary>
    private void ZoomOutImage()
    {
        if (!IsScaling && ScaledImageIdx != NO_IMAGE_SCALED)
        {
            ImageBlocks[ScaledImageIdx].RequestZoomOut();
            IsScaling = true;
            // Register OnZoomOutComplete to the Action
            ImageBlocks[ScaledImageIdx].OnScaleComplete += OnZoomOutComplete;
        }
    }

    /// <summary>
    /// Calls when the zoom in is complete 
    /// </summary>
    private void OnZoomInComplete()
    {
        IsScaling = false;

        // Unregister itself
        ImageBlocks[ScaledImageIdx].OnScaleComplete -= OnZoomInComplete;
    }

    /// <summary>
    /// Calls when the zoom out is complete
    /// </summary>
    private void OnZoomOutComplete()
    {
        IsScaling = false;

        // Unregister itself
        ImageBlocks[ScaledImageIdx].OnScaleComplete -= OnZoomOutComplete;

        // Display other images
        ShowOtherImage(true, ScaledImageIdx);

        ScaledImageIdx = NO_IMAGE_SCALED;

        // Tell other hololens about this operation
        SendXRayImageStatus();
    }

    /// <summary>
    /// Show(Not show) images in the ImageBlock array except the one with given index
    /// </summary>
    /// <param name="signal">If the signal is true, all the images except the one with the index will be displayed. Otherwise, all these images will be closed</param>
    /// <param name="idx">the index of image we will skip off</param>
    private void ShowOtherImage(bool signal, int idx)
    {
        for (int i = 0; i < ImageBlockNum; i++)
        {
            if (i != idx)
            {
                if (signal)
                {
                    ImageBlocks[i].ShowImage();
                }
                else
                {
                    ImageBlocks[i].CloseImage();
                }
            }
        }
    }

    #endregion

    // ---------- UI Control ----------

    #region UI Control

    /// <summary>
    /// A local reference to the text of page number
    /// </summary>
    private Text PageNumText;

    private void UIControlStartup()
    {
        GameObject PageNumGO = this.transform.Find("Canvas/PageNum_Text").gameObject;
        PageNumText = this.transform.Find("Canvas/PageNum_Text").gameObject.GetComponent<Text>();

        // Update the UI
        UI_UpdatePageNumber(PageNum);
    }

    private void UIControlUpdate()
    {
        
    }

    /// <summary>
    /// A top level that will be called by others to update the UI
    /// </summary>
    private void UI_UpdatePageNumber(int newPageNum)
    {
        // Update the page number
        if (PageNumText != null)
        {
            PageNumText.text = newPageNum.ToString() + " / " + MaxPageNum.ToString();
        }
    }

    #endregion

    // ---------- Sharing HoloLens ----------

    #region Sharing HoloLens

    /// <summary>
    /// Local reference to SharingStage.Instance.IsSharingMaster
    /// Determine if this hololens is the master of sharing.
    /// </summary>
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
    /// Indicates if the hololens has register the callback function from custommessage
    /// </summary>
    private bool CallbackRegistered = false;

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
    }

    private void SharingHololensUpdate()
    {

    }

    private void SharingHololensOnDestroy()
    {
        if(SharingStage.Instance != null)
        {
            if (SharingStage.Instance.SessionUsersTracker != null)
            {
                SharingStage.Instance.SessionUsersTracker.UserJoined -= UserJoinedSession;
            }
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
    }

    /// <summary>
    /// Called when a user is joining the current session.
    /// </summary>
    /// <param name="user">User that joined the current session.</param>
    private void UserJoinedSession(User user)
    {
        if (user.GetID() != SharingStage.Instance.Manager.GetLocalUser().GetID())
        {

            // Use the IsSharingMaster to determine the logic between slave and master here
            // because we don't know which user ID will be invoked first, it is possible that
            // we see the remote user ID first (while IsMaster is still true because 
            // SharingStage.Instance.IsOnlyUser is true)
            if (SharingStage.Instance.IsSharingMaster)
            {
                // Master should broadcast message to all the slaves
                SendXRayImageTransform();
                SendXRayImageStatus();
            }
            else
            {
                if (!CallbackRegistered)
                {
                    // Slave should register the callback function of custom messages and listen to the master
                    CustomMessages.Instance.MessageHandlers[CustomMessages.TestMessageID.XRayImageTransform] = UpdateXRayImageTransform;
                    CustomMessages.Instance.MessageHandlers[CustomMessages.TestMessageID.XRayImageStatus] = UpdateXRayImageStatus;
                    CallbackRegistered = true;
                }
            }
        }
    }

    /// <summary>
    /// Send the XRay transform to other users
    /// </summary>
    private void SendXRayImageTransform()
    {
        if (IsMaster)
        {
            CustomMessages.Instance.SendXRayImageTransform(this.transform.localPosition, this.transform.localRotation);
        }
    }

    /// <summary>
    /// Send the XRay status to other users
    /// </summary>
    private void SendXRayImageStatus()
    {
        if (IsMaster)
        {
            if (IsVisible)
            {
                CustomMessages.Instance.SendXRayImageStatus(MaxPageNum, PageNum, ScaledImageIdx);
            }
            else
            {
                CustomMessages.Instance.SendXRayImageStatus(MaxPageNum, PageNum, -2);
            }
        }
    }

    /// <summary>
    /// Called when the remote user sends a XRayImage transform
    /// </summary>
    /// <param name="msg"></param>
    private void UpdateXRayImageTransform(NetworkInMessage msg)
    {
        // Parse the message
        long userID = msg.ReadInt64();

        Vector3 pos = CustomMessages.Instance.ReadVector3(msg);
        Quaternion rot = CustomMessages.Instance.ReadQuaternion(msg);

        this.transform.localPosition = pos;
        // Not update the rot because each hololens should have the XRay image facing toward themselves
    }

    /// <summary>
    /// Called when the remote user sends a XRayImageStatus
    /// </summary>
    /// <param name="msg"></param>
    private void UpdateXRayImageStatus(NetworkInMessage msg)
    {
        // Parse the message
        long userID = msg.ReadInt64();

        int recv_TotalPageNum = CustomMessages.Instance.ReadInt(msg);
        int recv_CurrentPageNum = CustomMessages.Instance.ReadInt(msg);
        int recv_ImageStatus = CustomMessages.Instance.ReadInt(msg);

        // Check total page num
        if (recv_TotalPageNum != MaxPageNum)
        {
            UpdateMaxPageNum();
        }

        // Load current page
        if (recv_CurrentPageNum > 0 && recv_CurrentPageNum != PageNum)
        {
            LoadImages(recv_CurrentPageNum);
        }

        // Check image status
        if (recv_ImageStatus >= 0 && recv_ImageStatus < ImageBlockNum)
        {
            ZoomInImage(recv_ImageStatus, scale_factor);
        }
        if (recv_ImageStatus == -1)
        {
            // Zoom out any image if they are currently zoom in
            ZoomOutImage();
        }
        if (recv_ImageStatus == -2)
        {
            ShowManager(false);
        }
        else
        {
            ShowManager(true);
        }
    }

    #endregion
}

#endif