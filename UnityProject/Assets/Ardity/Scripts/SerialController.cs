﻿/**
 * Ardity (Serial Communication for Arduino + Unity)
 * Author: Daniel Wilches <dwilches@gmail.com>
 *
 * This work is released under the Creative Commons Attributions license.
 * https://creativecommons.org/licenses/by/2.0/
 */

using UnityEngine;
using System.Threading;

/**
 * This class allows a Unity program to continually check for messages from a
 * serial device.
 *
 * It creates a Thread that communicates with the serial port and continually
 * polls the messages on the wire.
 * That Thread puts all the messages inside a Queue, and this SerialController
 * class polls that queue by means of invoking SerialThread.GetSerialMessage().
 *
 * The serial device must send its messages separated by a newline character.
 * Neither the SerialController nor the SerialThread perform any validation
 * on the integrity of the message. It's up to the one that makes sense of the
 * data.
 */
public class SerialController : MonoBehaviour
{
    [Tooltip("Port name with which the SerialPort object will be created.")]
    [HideInInspector]
    public string portName = "COM3";

    [Tooltip("Baud rate that the serial device is using to transmit data.")]
    [HideInInspector]
    public int baudRate = 9600;

    [Tooltip("Reference to an scene object that will receive the events of connection, " +
             "disconnection and the messages from the serial device.")]
    [HideInInspector]
    public GameObject messageListener;

    [Tooltip("After an error in the serial communication, or an unsuccessful " +
             "connect, how many milliseconds we should wait.")]
    [HideInInspector]
    public int reconnectionDelay = 1000;

    [Tooltip("Maximum number of unread data messages in the queue.\n" +
             "When overflowing, messages will be discarded depending on \"Drop Old Message\" value")]
    [HideInInspector]
    public int messageQueueSize = 1;

    [Tooltip("How the controller handles the message queue.\n" + 
             "Poll the oldest, newest or all messages from the queue.")]
    [HideInInspector]
    public QueueBehaviour queueBehaviour;
    public enum QueueBehaviour
    {
        onlyOldest,
        onlyNewest,
        allMessages
    }

    [Tooltip("When the queue is full, prefer dropping the oldest message in the queue " +
             "instead of the new incoming message. Use this if you prefer to keep the " +
             "newest messages from the port.")]
    [HideInInspector]
    public bool dropOldMessage;

    [Tooltip("DTR switch\nGranular serial config for certain connections.")]
    [HideInInspector]
    public bool dtrEnable = false;
    [Tooltip("RTS switch\nGranular serial config for certain connections.")]
    [HideInInspector]
    public bool rtsEnable = false;
    // Constants used to mark the start and end of a connection. There is no
    // way you can generate clashing messages from your serial device, as I
    // compare the references of these strings, no their contents. So if you
    // send these same strings from the serial device, upon reconstruction they
    // will have different reference ids.
    public const string SERIAL_DEVICE_CONNECTED = "__Connected__";
    public const string SERIAL_DEVICE_DISCONNECTED = "__Disconnected__";

    // Internal reference to the Thread and the object that runs in it.
    protected Thread thread;
    protected SerialThreadLines serialThread;


    // ------------------------------------------------------------------------
    // Invoked whenever the SerialController gameobject is activated.
    // It creates a new thread that tries to connect to the serial device
    // and start reading from it.
    // ------------------------------------------------------------------------
    void OnEnable()
    {
        serialThread = new SerialThreadLines(portName,
                                             baudRate,
                                             reconnectionDelay,
                                             messageQueueSize,
                                             dropOldMessage,
                                             dtrEnable,
                                             rtsEnable);
        thread = new Thread(new ThreadStart(serialThread.RunForever));
        thread.Start();
    }

    // ------------------------------------------------------------------------
    // Invoked whenever the SerialController gameobject is deactivated.
    // It stops and destroys the thread that was reading from the serial device.
    // ------------------------------------------------------------------------
    void OnDisable()
    {
        // If there is a user-defined tear-down function, execute it before
        // closing the underlying COM port.
        if (userDefinedTearDownFunction != null)
            userDefinedTearDownFunction();

        // The serialThread reference should never be null at this point,
        // unless an Exception happened in the OnEnable(), in which case I've
        // no idea what face Unity will make.
        if (serialThread != null)
        {
            serialThread.RequestStop();
            serialThread = null;
        }

        // This reference shouldn't be null at this point anyway.
        if (thread != null)
        {
            thread.Join();
            thread = null;
        }
    }

    // ------------------------------------------------------------------------
    // Polls messages from the queue that the SerialThread object keeps. Once a
    // message has been polled it is removed from the queue.
    // ------------------------------------------------------------------------
    void Update()
    {
        // If the user prefers to poll the messages instead of receiving them
        // via SendMessage, then the message listener should be null.
        if (messageListener == null)
            return;

        // Read the next message from the queue
        string message = (string)serialThread.ReadMessage();
        if (message == null)
            return;

        switch (queueBehaviour)
        {
            case QueueBehaviour.onlyOldest: { SendUnityMessage(message); break; }
            case QueueBehaviour.onlyNewest: { ReadOnlyNewest(message); break; }
            case QueueBehaviour.allMessages: { ReadAllMessages(message); break; }
        }
    }

    // ------------------------------------------------------------------------
    // Calls different handlers depending on serial message type
    // ------------------------------------------------------------------------
    void SendUnityMessage(string message)
    {
        // Debug.Log("Polling Message");
        // Check if the message is plain data or a connect/disconnect event.
        if (ReferenceEquals(message, SERIAL_DEVICE_CONNECTED))
            messageListener.SendMessage("OnConnectionEvent", true);
        else if (ReferenceEquals(message, SERIAL_DEVICE_DISCONNECTED))
            messageListener.SendMessage("OnConnectionEvent", false);
        else
            messageListener.SendMessage("OnMessageArrived", message);
    }

    // ------------------------------------------------------------------------
    // Calls message handlers for every message in the queue
    // ------------------------------------------------------------------------
    void ReadAllMessages(string message)
    {

        // Debug.Log("Polling All Messages");
        int count = 0;
        while (message != null)
        {
            count++;
            SendUnityMessage(message);
            message = (string)serialThread.ReadMessage();
        }
        // Debug.Log(count + " messages in queue");
    }

    // ------------------------------------------------------------------------
    // Calls message handlers only for the newest message in the queue
    // ------------------------------------------------------------------------
    void ReadOnlyNewest(string message)
    {
        // Debug.Log("Polling Newest Message (oldest in queue: "+message+")");
        string lastMessage = null;
        while (message != null)
        {
            lastMessage = message;
            message = (string)serialThread.ReadMessage();
        }
        SendUnityMessage(lastMessage);
    }

    // ------------------------------------------------------------------------
    // Returns a new unread message from the serial device. You only need to
    // call this if you don't provide a message listener.
    // ------------------------------------------------------------------------
    public string ReadSerialMessage()
    {
        // Read the next message from the queue
        return (string)serialThread.ReadMessage();
    }

    // ------------------------------------------------------------------------
    // Puts a message in the outgoing queue. The thread object will send the
    // message to the serial device when it considers it's appropriate.
    // ------------------------------------------------------------------------
    public void SendSerialMessage(string message)
    {
        serialThread.SendMessage(message);
    }

    // ------------------------------------------------------------------------
    // Executes a user-defined function before Unity closes the COM port, so
    // the user can send some tear-down message to the hardware reliably.
    // ------------------------------------------------------------------------
    public delegate void TearDownFunction();

    private TearDownFunction userDefinedTearDownFunction;

    public void SetTearDownFunction(TearDownFunction userFunction)
    {
        this.userDefinedTearDownFunction = userFunction;
    }
}
