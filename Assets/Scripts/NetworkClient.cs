#if UNITY_EDITOR

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NetworkClient
{

}

#endif

#if !UNITY_EDITOR

using UnityEngine;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.Networking;
using System;
using System.Threading.Tasks;

public class NetworkClient
{
    /// <summary>
    /// A public variable indicates if the network is connected
    /// </summary>
    public bool Connected
    {
        get
        {
            return connectionState == ConnectionStateEnum.Connected;
        }
    }

    /// <summary>
    /// States relates to the connection
    /// </summary>
    private enum ConnectionStateEnum
    {
        NotConnected,
        /// <summary>
        /// Indicates if the client has started connecting to the network
        /// </summary>
        StartConnecting,
        /// <summary>
        /// indicats if the connection is established
        /// </summary>
        Connected,
    }

    private ConnectionStateEnum connectionState = ConnectionStateEnum.NotConnected;

    /// <summary>
    /// Network Stream Socket
    /// </summary>
    private StreamSocket serverSocket;

    /// <summary>
    /// Connect to the server given its IP and connection port
    /// </summary>
    /// <param name="ServerIP">The IPv4 Address of the remote server</param>
    /// <param name="ConnectionPort">The connection port to the server</param>
    public async Task ConnectToServer(string ServerIP, int ConnectionPort)
    {
        // Return if the connectionState is not in the NotConnected state
        if (connectionState != ConnectionStateEnum.NotConnected)
        {
            return;
        }

        connectionState = ConnectionStateEnum.StartConnecting;
        // Get host information
        HostName hostName;
        try
        {
            hostName = new HostName(ServerIP.Trim());
        }
        catch (ArgumentException)
        {
            // The host name may be invalid. Catch this error and report to the user.
            Debug.Log("Invalid host name: " + ServerIP.Trim());
            connectionState = ConnectionStateEnum.NotConnected;
            return; 
        }

        // Connect to server
        serverSocket = new StreamSocket();
        try
        {
            await serverSocket.ConnectAsync(hostName, ConnectionPort.ToString());

            // Unfortunately, we don't know if the ConnectAsync is success or not
            // here we just assume that it succeeds.
            connectionState = ConnectionStateEnum.Connected;
        }
        catch (Exception ex)
        {
            Debug.Log("Connect socket with error:" + ex.Message);
            CloseConnection();
        }
    }

    /// <summary>
    /// Send Request to server (With string)
    /// </summary>
    /// <param name="command"></param>
    /// <returns>Return true if request sent, false otherwise</returns>
    public async Task<bool> sendRequest(string command)
    {
        // A variable indicates whether send request is complete
        bool sendComplete = false;

        // If connection is not established, we cannot perform request
        if (connectionState != ConnectionStateEnum.Connected)
        {
            return sendComplete;
        }

        // 'using' has a implicit Dispose call to the DataWriter Object
        // i.e. the networkWriter will be closed beyond the 'using' scope
        // the OutputStream will also be closed when the networkWriter is closed
        // To solve this problem, we can use DetachStream() function. See my comments below
        using (DataWriter networkWriter = new DataWriter(serverSocket.OutputStream))
        {
            try
            {
                networkWriter.WriteString(command);

                // Send command to server
                await networkWriter.StoreAsync();

                sendComplete = true;
            }
            catch (Exception ex)
            {
                Debug.Log("Send request to socket fails with error:" + ex.Message);
                sendComplete = false;
                CloseConnection();
            }

            // In order to prolong the lifetime of the stream, detach it from the 
            // DataWriter so that it will not be closed when Dispose() is called on 
            // dataWriter. Were we to fail to detach the stream, the call to 
            // dataWriter.Dispose() would close the underlying stream, preventing 
            // its subsequent use by the DataReader below.

            // However, here I do want to close the OutputStream
            // networkWriter.DetachStream();
        }

        return sendComplete;
    }

    /// <summary>
    /// Send Request to server (With byte[])
    /// </summary>
    /// <param name="command"></param>
    /// <returns>Return true if request sent, false otherwise</returns>
    public async Task<bool> sendRequest(byte[] command)
    {
        // A variable indicates whether send request is complete
        bool sendComplete = false;

        // If connection is not established, we cannot perform request
        if (connectionState != ConnectionStateEnum.Connected)
        {
            return sendComplete;
        }

        // 'using' has a implicit Dispose call to the DataWriter Object
        // i.e. the networkWriter will be closed beyond the 'using' scope
        // the OutputStream will also be closed when the networkWriter is closed
        // To solve this problem, we can use DetachStream() function. See my comments below
        using (DataWriter networkWriter = new DataWriter(serverSocket.OutputStream))
        {
            try
            {
                networkWriter.WriteBytes(command);

                // Send command to server
                await networkWriter.StoreAsync();

                sendComplete = true;
            }
            catch (Exception ex)
            {
                Debug.Log("Send request to socket fails with error:" + ex.Message);
                sendComplete = false;
                CloseConnection();
            }
        }

        return sendComplete;
    }

    /// <summary>
    /// Read data from server
    /// </summary>
    /// <param name="readSize">Number of bytes you want to read from server</param>
    /// <param name="buffer">the data will be stored in this buffer</param>
    /// <returns>The actual number read from server</returns>
    public async Task<int> readData(int readSize, byte[] buffer)
    {
        // If connection is not established, we cannot perform read
        if (connectionState != ConnectionStateEnum.Connected)
        {
            return 0;
        }

        // the total number of bytes read from network
        int rlen = 0;

        // Instantiate the resource object outside the using scope is not recommended
        using (DataReader networkReader = new DataReader(serverSocket.InputStream))
        {
            try
            {
                // count is the number of bytes reader from network, the maximum value should be <= readSize
                uint count = await networkReader.LoadAsync((uint)readSize);

                networkReader.ReadBytes(buffer);

                rlen = (int)count;
            }
            catch (Exception ex)
            {
                Debug.Log("Read from socket fails with error:" + ex.Message);
                rlen = 0;
            }

            // Detach InputStream (See the comments in sendRequest())
            networkReader.DetachStream();
        }

        return rlen;
    }

    /// <summary>
    /// Close the connection 
    /// </summary>
    public void CloseConnection()
    {
        // Close connection socket
        serverSocket.Dispose();

        connectionState = ConnectionStateEnum.NotConnected;
    }

}

#endif