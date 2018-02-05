using System.Collections;
using HoloToolkit.Sharing;
using UnityEngine;

/// <summary>
/// This class is responsible for 
/// 1, Reporting the state of the ImportExportAnchorManager and generating the visual and audio feedback to user
/// </summary>
public class AnchorNotification : MonoBehaviour {

    /// <summary>
    /// Anchor State 
    /// </summary>
    private enum AnchorStateEnum
    {
        /// <summary>
        /// Initial state, idle
        /// </summary>
        IDLE = -1,
        /// <summary>
        /// Connecting to the server; Uploading/downloading anchor
        /// </summary>
        Connecting,
        /// <summary>
        /// Uploading/downloading data, this state is currently a garbage state because I cannot find a good way to 
        /// invoke it in the ImportExportAnchorManager
        /// </summary>
        Loading,
        /// <summary>
        /// Successfully uploaded/downloaded anchor to/from server 
        /// </summary>
        Complete, 
        /// <summary>
        /// Failed to upload/download anchor from server
        /// </summary>
        Failed,
    }

    private AnchorStateEnum anchorState = AnchorStateEnum.IDLE;

    /// <summary>
    /// Local reference to determine if this HoloLen is a master
    /// </summary>
    private bool IsMaster
    {
        get
        {
            if (SharingStage.Instance != null)
            {
                return SharingStage.Instance.IsSharingMaster;
            }
            else
            {
                return false;
            }
        }
    }

    private void ChangeAnchorState(AnchorStateEnum newState)
    {
        // If there is no change in the state, we do nothing
        if (anchorState == newState)
        {
            return;
        }

        switch (newState)
        {
            case AnchorStateEnum.Connecting:
                SetNotificationText("Connecting to the Sharing Server...");
                break;
            case AnchorStateEnum.Loading:
                if (IsMaster)
                {
                    SetNotificationText("Uploading data...");
                }
                else
                {
                    SetNotificationText("Downloading data...");
                }
                break;
            case AnchorStateEnum.Complete:
                SetNotificationText("Connection Complete");

                // Play audio feedback
                PlayAudio();

                // Schedule to close the UI
                float delay = 5.0f;
                StartCoroutine(CloseNotificationText(delay));
                break;
            case AnchorStateEnum.Failed:
                SetNotificationText("Connection failed. Reestablishing...");
                break;
        }

        anchorState = newState;
    }

	// Use this for initialization
	private void Start () {
        // Register AnchorEstablished function to the correct Action
        if (IsMaster)
        {
            ImportExportAnchorManager.Instance.AnchorUploaded += AnchorComplete;
        }
        else
        {
            ImportExportAnchorManager.Instance.AnchorDownloaded += AnchorComplete;
        }
        ImportExportAnchorManager.Instance.AnchorLoading += AnchorLoading;

        ChangeAnchorState(AnchorStateEnum.Connecting);

        AudioControlStartup();
    }
	
	// Update is called once per frame
	private void Update () {
        
    }

    private void OnDestroy()
    {
        // Unregister AnchorEstablished function to the correct Action
        if (IsMaster)
        {
            if (ImportExportAnchorManager.Instance != null)
            {
                ImportExportAnchorManager.Instance.AnchorUploaded -= AnchorComplete;
            }
        }
        else
        {
            if (ImportExportAnchorManager.Instance != null)
            {
                ImportExportAnchorManager.Instance.AnchorDownloaded -= AnchorComplete;
            }
        }

        if (ImportExportAnchorManager.Instance != null)
        {
            ImportExportAnchorManager.Instance.AnchorLoading -= AnchorLoading;
        }
    }

    /// <summary>
    /// Calls when uploading/downloading the anchor completes
    /// </summary>
    /// <param name="successful"></param>
    private void AnchorComplete(bool successful)
    {
        if (successful)
        {
            ChangeAnchorState(AnchorStateEnum.Complete);
        }
        else
        {
            ChangeAnchorState(AnchorStateEnum.Failed);
        }
    }

    /// <summary>
    /// Calls when starts uploading/downloading the anchor
    /// </summary>
    private void AnchorLoading()
    {
        ChangeAnchorState(AnchorStateEnum.Loading);
    }

    // ---------- Audio Control ----------

    private AudioSource audioSource;

    private void AudioControlStartup()
    {
        audioSource = gameObject.GetComponent<AudioSource>();
    }

    private void PlayAudio()
    {
        // If there is no audioSource, we cannot play anything
        if (audioSource == null)
        {
            return;
        }

        audioSource.Play();
    }

    // ---------- UI Control ----------

    /// <summary>
    /// Display the information to user
    /// </summary>
    public TextMesh NotificationText;

    private void SetNotificationText(string msg)
    {
        if (NotificationText == null)
        {
            return;
        }
        
        NotificationText.text = msg;
    }

    /// <summary>
    /// Close the Notification Text after certain delay
    /// </summary>
    /// <param name="delay">the amount of delay time before close the text, in seconds</param>
    /// <returns></returns>
    public IEnumerator CloseNotificationText(float delay)
    {
        yield return new WaitForSeconds(delay);
        SetNotificationText("");
    }
}