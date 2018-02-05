using System;
using UnityEngine;

/// <summary>
/// The class of Image Block
/// This class should handle the Image Scaling, and Image Loading feature
/// </summary>
public class ImageBlock
{
    /// <summary>
    /// This Image block's gameobject in Unity
    /// </summary>
    public GameObject ImageObject { get; private set; }

    /// <summary>
    /// State indicating the status of the image
    /// </summary>
    private enum ImageStateEnum
    {
        /// <summary>
        /// Default mode of image
        /// </summary>
        DEFAULT,
        /// <summary>
        /// The image is zooming in
        /// </summary>
        ZOOMING_IN,
        /// <summary>
        /// The image has been zoomed in
        /// </summary>
        SCALED,
        /// <summary>
        /// The image is zooming out
        /// </summary>
        ZOOMING_OUT,
    }

    private ImageStateEnum imageState;

    private void ChangeImageState(ImageStateEnum newState)
    {
        imageState = newState;
    }

    /// <summary>
    /// Variable that determines the scaling factor of the image 
    /// </summary>
    private float scaleFactor;

    /// <summary>
    /// ImageBlock constructor, it will link its ImageObject to the input GameObject
    /// </summary>
    /// <param name="image">The image object that this ImageBlock will link to</param>
    public ImageBlock(GameObject image)
    {
        ImageObject = image;
        imageState = ImageStateEnum.DEFAULT;
        scaleFactor = 1.0f;

        LoadDefaultImage();
    }

    public void SetTransform(Vector3 posi)
    {
        if (ImageObject == null)
        {
            return;
        }

        ImageObject.transform.localPosition = posi;
    }

    public void ShowImage()
    {
        if (ImageObject != null)
        {
            ImageObject.SetActive(true);
        }
    }

    public void CloseImage()
    {
        if (ImageObject != null)
        {
            ImageObject.SetActive(false);
        }
    }

#if !UNITY_EDITOR
    /// <summary>
    /// Load the image from server 
    /// </summary>
    /// <param name="ipAddr">the ip address of the server</param>
    /// <param name="port">the port number of the server</param>
    /// <param name="imageIdx">the index of image to be loaded</param>
    public async void LoadImage(string ipAddr, int port, int imageIdx)
    {
        // Set the sprite image to be the "loading image" so that user can tell if a new image is loading
        LoadDefaultImage();

        // Connect to the server
        NetworkClient client = new NetworkClient();
        await client.ConnectToServer(ipAddr, port);

        // Send request
        byte[] c1 = System.Text.Encoding.ASCII.GetBytes("GET XRAY\n");  // header protocol
        byte[] c2 = BitConverter.GetBytes(imageIdx);                    // image index
        byte[] c3 = System.Text.Encoding.ASCII.GetBytes("\n");          // header protocol
        byte[] command = new byte[c1.Length + c2.Length + c3.Length];
        // [Note] Use BlockCopy will be faster than Copy
        System.Buffer.BlockCopy(c1, 0, command, 0, c1.Length);
        System.Buffer.BlockCopy(c2, 0, command, c1.Length, c2.Length);
        System.Buffer.BlockCopy(c3, 0, command, c1.Length + c2.Length, c3.Length);
        bool success = await client.sendRequest(command);
        
        // If request sends successfully, read the response
        if (success)
        {
            // [Note] We should read "OK\n[sizeof(ULONG)][fileData]"
            // In the server, sizeof(ULONG) == 4

            // 1, read "OK\n[sizeof(ULONG)]"
            int ShouldReadLen = 3 + 4;
            byte[] buffer = new byte[ShouldReadLen];
            int rlen = await client.readData(ShouldReadLen, buffer);

            int fileSize = -1;      // The size of the image file. If fileSize == -1, we don't do any reading 
            if (rlen == ShouldReadLen)
            {
                // First read 3 bytes to see if the response is "OK\n"
                int hsize = 3;
                byte[] header = new byte[hsize];
                Array.Copy(buffer, header, hsize);
                string hstr = System.Text.Encoding.UTF8.GetString(header);

                if (hstr == "OK\n")
                {
                    fileSize = BitConverter.ToInt32(buffer, hsize);
                }
                else
                {
                    Debug.Log("ImageBlock::LoadImage: Server response has error\n" + System.Text.Encoding.UTF8.GetString(buffer));
                }
            }

            // 2, Read "[fileData]"
            if (fileSize != -1)
            {
                byte[] imageBuf = new byte[fileSize];
                rlen = await client.readData(fileSize, imageBuf);

                if (rlen == fileSize)
                {
                    Texture2D t = new Texture2D(4, 4);
                    t.LoadImage(imageBuf);

                    GenerateSprite(t);
                }
            }
        }

        // Close connection
        client.CloseConnection();
    }
#endif

    /// <summary>
    /// Load the default image while waiting for the network connection
    /// </summary>
    private void LoadDefaultImage()
    {
        // The path to the default image
        string filePath = "XRayImageHolder/LoadingImage";

        Texture2D t = Resources.Load<Texture2D>(filePath);

        GenerateSprite(t);
    }

    /// <summary>
    /// Given a input of Texture 2D, generate the sprite
    /// </summary>
    /// <param name="t"></param>
    private void GenerateSprite(Texture2D t)
    {
        Rect size = new Rect(0, 0, t.width, t.height);
        // Calculate sprite pixel_per_unit value based on the input image's scale
        Vector3 image_scale = ImageObject.transform.localScale;
        float sprite_ppu = t.height / image_scale[0];       // ppu = pixel_per_unit; assuming the image is a square so we just take the first index
                                                            // Also, assuming texture is a square, i.e. height == weight
        Sprite s = Sprite.Create(t, size, new Vector2(0.5f, 0.5f), sprite_ppu);     // Pivot = center -> pivot = (0.5f, 0.5f)

        SpriteRenderer sr = ImageObject.GetComponent<SpriteRenderer>();

        if (s != null)
        {
            sr.sprite = s;
        }
        else
        {
            Debug.Log("ImageBlock::GenerateSprite: Fail to load image.");
        }
    }

    // ---------- Image Scaling ----------

    #region Image Scaling

    /// <summary>
    /// The number of frames to complete the scaling animation
    /// </summary>
    /// <remarks>Use this to control the animation speed</remarks>
    private int Nframe = 40;

    /// <summary>
    /// A counter that use to keep track of the scaling animation.
    /// It records the current frame we are in
    /// </summary>
    private int Ncount = 0;

    /// <summary>
    /// The target position of the image after it is scaled
    /// </summary>
    private Vector3 scaledPosition = new Vector3(0, 0, 0);
    private Vector3 scaledScale;

    /// <summary>
    /// The original position of the image before it zoom in
    /// </summary>
    /// <remarks>Need this for zoom out function</remarks>
    private Vector3 origPosition, origScale;

    /// <summary>
    /// The minimum change of position or scale in a single call of zoom in/out function
    /// </summary>
    private Vector3 stepPosition, stepScale;

    /// <summary>
    /// Fires when an image scaling action is complete (zoom in/out)
    /// </summary>
    public Action OnScaleComplete;
    
    // Run scale state machine
    /// <summary>
    /// The toplevel function to run the state machine related to the image scaling
    /// </summary>
    public void RunScaleSM()
    {
        switch (imageState)
        {
            case ImageStateEnum.DEFAULT:
            case ImageStateEnum.SCALED:
                break;
            case ImageStateEnum.ZOOMING_IN:
                RunZoomIn();
                break;
            case ImageStateEnum.ZOOMING_OUT:
                RunZoomOut();
                break;
        }
    }

    /// <summary>
    /// Issue a request to zoom in
    /// </summary>
    /// <param name="scale_factor">scale factor for the zoom in</param>
    public void RequestZoomIn(float scale_factor)
    {
        if (imageState == ImageStateEnum.DEFAULT)
        {
            scaleFactor = scale_factor;

            //Save the original position and scale so when we zoom out, we know where to go 
            origPosition = ImageObject.transform.localPosition;
            origScale = ImageObject.transform.localScale;

            // Set up the target scale (We should keep the z direction scale unchanged. Setting it to zero will make the gaze manager unable to detect it)
            scaledScale = new Vector3(scaleFactor, scaleFactor, ImageObject.transform.localScale.z);

            // Update the minimal position and scale step
            Vector3 offsetPosition = scaledPosition - ImageObject.transform.localPosition;
            Vector3 offsetScale = scaledScale - ImageObject.transform.localScale;
            stepPosition = offsetPosition / Nframe;
            stepScale = offsetScale / Nframe;

            // Initialize the frame counter
            Ncount = 0;

            // Change the state after all the parameters are set up properly 
            ChangeImageState(ImageStateEnum.ZOOMING_IN);
        }
    }

    /// <summary>
    /// Issue a request to zoom out
    /// </summary>
    public void RequestZoomOut()
    {
        if (imageState == ImageStateEnum.SCALED)
        {
            // Update the minimal position and scale step
            Vector3 offsetPosition = origPosition - scaledPosition;
            Vector3 offsetScale = origScale - scaledScale;
            stepPosition = offsetPosition / Nframe;
            stepScale = offsetScale / Nframe;

            // Initialize the frame counter
            Ncount = 0;

            // Change the state after all the parameters are set up properly 
            ChangeImageState(ImageStateEnum.ZOOMING_OUT);
        }
    }

    /// <summary>
    /// Run the zoom in animation 
    /// </summary>
    private void RunZoomIn()
    {
        if (Ncount == Nframe)
        {
            // The scaling animation is complete, finish up the zoom in
            ImageObject.transform.localPosition = scaledPosition;
            ImageObject.transform.localScale = scaledScale;
            imageState = ImageStateEnum.SCALED;
            OnScaleComplete();
        }
        else
        {
            ImageObject.transform.localPosition += stepPosition;
            ImageObject.transform.localScale += stepScale;
            Ncount++;
        }
    }

    /// <summary>
    /// Run the zoom out animation
    /// </summary>
    private void RunZoomOut()
    {
        if (Ncount == Nframe)
        {
            // The scaling animation is complete, finish up the zoom out
            ImageObject.transform.localPosition = origPosition;
            ImageObject.transform.localScale = origScale;
            imageState = ImageStateEnum.DEFAULT;
            OnScaleComplete();
        }
        else
        {
            ImageObject.transform.localPosition += stepPosition;
            ImageObject.transform.localScale += stepScale;
            Ncount++;
        }
    }




    #endregion

}
