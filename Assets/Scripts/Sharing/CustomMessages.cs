using System;
using System.Collections.Generic;
using HoloToolkit.Unity;
using UnityEngine;

namespace HoloToolkit.Sharing
{
    /// <summary>
    /// Test class for demonstrating how to send custom messages between clients.
    /// </summary>
    public class CustomMessages : Singleton<CustomMessages>
    {
        /// <summary>
        /// Message enum containing our information bytes to share.
        /// The first message type has to start with UserMessageIDStart
        /// so as not to conflict with HoloToolkit internal messages.
        /// </summary>
        public enum TestMessageID : byte
        {
            HeadTransform = MessageID.UserMessageIDStart,
            NirImageTransform,      // the msg related to the position of the nirimage
            NirImageNetworkStatus,  // the msg related to the network status of the nirimage
            XRayImageTransform,     // the msg related to the position of the xray
            XRayImageStatus,        // the msg related to the image content of xray
            Max
        }

        public enum UserMessageChannels
        {
            Anchors = MessageChannel.UserMessageChannelStart
        }

        /// <summary>
        /// Cache the local user's ID to use when sending messages
        /// </summary>
        public long LocalUserID
        {
            get; set;
        }

        public delegate void MessageCallback(NetworkInMessage msg);
        private Dictionary<TestMessageID, MessageCallback> messageHandlers = new Dictionary<TestMessageID, MessageCallback>();

        /// <summary>
        /// A dictionary that holds the (TestMessageID, CallbackFunction) pair. 
        /// You can assign a TestMessageID to the callback function you want to invoke when the TestMessageID is received.  
        /// </summary>
        public Dictionary<TestMessageID, MessageCallback> MessageHandlers
        {
            get
            {
                return messageHandlers;
            }
        }

        /// <summary>
        /// Helper object that we use to route incoming message callbacks to the member
        /// functions of this class
        /// </summary>
        private NetworkConnectionAdapter connectionAdapter;

        /// <summary>
        /// Cache the connection object for the sharing service
        /// </summary>
        private NetworkConnection serverConnection;

        private void Start()
        {
            // SharingStage should be valid at this point, but we may not be connected.
            if (SharingStage.Instance.IsConnected)
            {
                Connected();
            }
            else
            {
                SharingStage.Instance.SharingManagerConnected += Connected;
            }
        }

        private void Connected(object sender = null, EventArgs e = null)
        {
            SharingStage.Instance.SharingManagerConnected -= Connected;
            InitializeMessageHandlers();
        }

        private void InitializeMessageHandlers()
        {
            SharingStage sharingStage = SharingStage.Instance;

            if (sharingStage == null)
            {
                Debug.Log("Cannot Initialize CustomMessages. No SharingStage instance found.");
                return;
            }

            serverConnection = sharingStage.Manager.GetServerConnection();
            if (serverConnection == null)
            {
                Debug.Log("Cannot initialize CustomMessages. Cannot get a server connection.");
                return;
            }

            connectionAdapter = new NetworkConnectionAdapter();
            connectionAdapter.MessageReceivedCallback += OnMessageReceived;

            // Cache the local user ID
            LocalUserID = SharingStage.Instance.Manager.GetLocalUser().GetID();

            for (byte index = (byte)TestMessageID.HeadTransform; index < (byte)TestMessageID.Max; index++)
            {
                if (MessageHandlers.ContainsKey((TestMessageID)index) == false)
                {
                    MessageHandlers.Add((TestMessageID)index, null);
                }

                serverConnection.AddListener(index, connectionAdapter);
            }
        }

        private NetworkOutMessage CreateMessage(byte messageType)
        {
            NetworkOutMessage msg = serverConnection.CreateMessage(messageType);
            msg.Write(messageType);
            // Add the local userID so that the remote clients know whose message they are receiving
            msg.Write(LocalUserID);
            return msg;
        }

        #region Send Message Functions

        public void SendHeadTransform(Vector3 position, Quaternion rotation)
        {
            // If we are connected to a session, broadcast our head info
            if (serverConnection != null && serverConnection.IsConnected())
            {
                // Create an outgoing network message to contain all the info we want to send
                NetworkOutMessage msg = CreateMessage((byte)TestMessageID.HeadTransform);

                AppendTransform(msg, position, rotation);

                // Send the message as a broadcast, which will cause the server to forward it to all other users in the session.
                serverConnection.Broadcast(
                    msg,
                    MessagePriority.Immediate,
                    MessageReliability.UnreliableSequenced,
                    MessageChannel.Avatar);
            }
        }

        public void SendNirImageTransform(Vector3 position, Quaternion rotation)
        {
            // If we are connected to a session, broadcast the info we want
            if (this.serverConnection != null && this.serverConnection.IsConnected())
            {
                // Create an outgoing network message to contain all the info we want to send
                NetworkOutMessage msg = CreateMessage((byte)TestMessageID.NirImageTransform);

                AppendTransform(msg, position, rotation);

                // Send the message as a broadcast, which will cause the server to forward it to all other users in the session.
                this.serverConnection.Broadcast(
                    msg,
                    MessagePriority.Immediate,
                    MessageReliability.ReliableOrdered,
                    MessageChannel.Avatar);
            }
        }

        public void SendNirImageNetworkStatus(bool connected)
        {
            // If we are connected to a session, broadcast the info we want
            if (this.serverConnection != null && this.serverConnection.IsConnected())
            {
                // Create an outgoing network message to contain all the info we want to send
                NetworkOutMessage msg = CreateMessage((byte)TestMessageID.NirImageNetworkStatus);

                AppendBoolean(msg, connected);

                // Send the message as a broadcast, which will cause the server to forward it to all other users in the session.
                this.serverConnection.Broadcast(
                    msg,
                    MessagePriority.Immediate,
                    MessageReliability.ReliableOrdered,
                    MessageChannel.Avatar);
            }
        }

        public void SendXRayImageTransform(Vector3 position, Quaternion rotation)
        {
            // If we are connected to a session, broadcast the info we want
            if (this.serverConnection != null && this.serverConnection.IsConnected())
            {
                // Create an outgoing network message to contain all the info we want to send
                NetworkOutMessage msg = CreateMessage((byte)TestMessageID.XRayImageTransform);

                AppendTransform(msg, position, rotation);

                // Send the message as a broadcast, which will cause the server to forward it to all other users in the session.
                this.serverConnection.Broadcast(
                    msg,
                    MessagePriority.Immediate,
                    MessageReliability.ReliableOrdered,
                    MessageChannel.Avatar);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="TotalPageNum"></param>
        /// <param name="CurrentPageNum"></param>
        /// <param name="ImageStatus">
        /// If ImageStatus >= 0, then ImageStatus stands for the zoom-in image index. (indexing starts from 0)
        /// If ImageStatus = -1, then none of the image is zoom in, the image manager is visible to user.
        /// If ImageStatus = -2, then the image manager is not visible to user (However, the image can be still in zoom in state) 
        /// </param>
        public void SendXRayImageStatus(int TotalPageNum, int CurrentPageNum, int ImageStatus)
        {
            // If we are connected to a session, broadcast the info we want
            if (this.serverConnection != null && this.serverConnection.IsConnected())
            {
                // Create an outgoing network message to contain all the info we want to send
                NetworkOutMessage msg = CreateMessage((byte)TestMessageID.XRayImageStatus);

                AppendInt(msg, TotalPageNum);
                AppendInt(msg, CurrentPageNum);
                AppendInt(msg, ImageStatus);

                // Send the message as a broadcast, which will cause the server to forward it to all other users in the session.
                this.serverConnection.Broadcast(
                    msg,
                    MessagePriority.Immediate,
                    MessageReliability.ReliableOrdered,
                    MessageChannel.Avatar);
            }
        }

        #endregion

        protected override void OnDestroy()
        {
            base.OnDestroy();

            if (serverConnection != null)
            {
                for (byte index = (byte)TestMessageID.HeadTransform; index < (byte)TestMessageID.Max; index++)
                {
                    serverConnection.RemoveListener(index, connectionAdapter);
                }
                connectionAdapter.MessageReceivedCallback -= OnMessageReceived;
            }
        }

        private void OnMessageReceived(NetworkConnection connection, NetworkInMessage msg)
        {
            byte messageType = msg.ReadByte();
            MessageCallback messageHandler = MessageHandlers[(TestMessageID)messageType];
            if (messageHandler != null)
            {
                messageHandler(msg);
            }
        }

        #region HelperFunctionsForWriting

        private void AppendTransform(NetworkOutMessage msg, Vector3 position, Quaternion rotation)
        {
            AppendVector3(msg, position);
            AppendQuaternion(msg, rotation);
        }

        private void AppendVector3(NetworkOutMessage msg, Vector3 vector)
        {
            msg.Write(vector.x);
            msg.Write(vector.y);
            msg.Write(vector.z);
        }

        private void AppendQuaternion(NetworkOutMessage msg, Quaternion rotation)
        {
            msg.Write(rotation.x);
            msg.Write(rotation.y);
            msg.Write(rotation.z);
            msg.Write(rotation.w);
        }

        private void AppendBoolean(NetworkOutMessage msg, bool boolean)
        {
            if (boolean)
            {
                msg.Write(1.0f);
            }
            else
            {
                msg.Write(0.0f);
            }
        }

        private void AppendInt(NetworkOutMessage msg, int value)
        {
            msg.Write(value);
        }

        #endregion

        #region HelperFunctionsForReading

        public Vector3 ReadVector3(NetworkInMessage msg)
        {
            return new Vector3(msg.ReadFloat(), msg.ReadFloat(), msg.ReadFloat());
        }

        public Quaternion ReadQuaternion(NetworkInMessage msg)
        {
            return new Quaternion(msg.ReadFloat(), msg.ReadFloat(), msg.ReadFloat(), msg.ReadFloat());
        }

        public bool ReadBoolean(NetworkInMessage msg)
        {
            float val = msg.ReadFloat();

            if (val == 1.0f)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public int ReadInt(NetworkInMessage msg)
        {
            return msg.ReadInt32();     // This is a x86 system and therefore should read 32 bits.
        }

        #endregion
    }
}