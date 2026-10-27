using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;

public class SimpleNetworkUI : MonoBehaviour
{
    [Header("Optional UI Elements")]
    [SerializeField] private InputField ipInputField;
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;

    [Header("Network Connection Settings")]
    [SerializeField] private string targetIpAddress = "127.0.0.1";
    [SerializeField] private ushort connectionPort = 7777;

    private NetworkManager subscribedNetworkManager;
    private string connectionStatus = "Waiting - start as Host or Client";

    private void Start()
    {
        if (hostButton != null)
        {
            hostButton.onClick.AddListener(StartHostGame);
        }

        if (clientButton != null)
        {
            clientButton.onClick.AddListener(StartClientGame);
        }

        if (ipInputField != null)
        {
            ipInputField.text = targetIpAddress;
        }

        SubscribeToNetworkEvents();
    }

    private void Update()
    {
        SubscribeToNetworkEvents();
    }

    private void OnDestroy()
    {
        UnsubscribeFromNetworkEvents();
    }

    public void StartHostGame()
    {
        if (!TryGetNetworkManager(out NetworkManager networkManager))
        {
            return;
        }

        UnityTransport transport = networkManager.GetComponent<UnityTransport>();
        if (transport == null)
        {
            SetStatus("FAILED - Unity Transport is missing", true);
            return;
        }

        transport.SetConnectionData("0.0.0.0", connectionPort);
        SetStatus($"Starting Host on port {connectionPort}...", false);

        if (!networkManager.StartHost())
        {
            SetStatus("FAILED - StartHost returned false; check Console", true);
        }
    }

    public void StartClientGame()
    {
        if (!TryGetNetworkManager(out NetworkManager networkManager))
        {
            return;
        }

        string ipToConnect = (ipInputField != null && !string.IsNullOrEmpty(ipInputField.text))
            ? ipInputField.text
            : targetIpAddress;

        UnityTransport transport = networkManager.GetComponent<UnityTransport>();
        if (transport == null)
        {
            SetStatus("FAILED - Unity Transport is missing", true);
            return;
        }

        transport.SetConnectionData(ipToConnect, connectionPort);
        SetStatus($"Connecting to {ipToConnect}:{connectionPort}...", false);

        if (!networkManager.StartClient())
        {
            SetStatus("FAILED - StartClient returned false; check Console", true);
        }
    }

    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(20, 20, 320, 250), GUI.skin.box);
        GUILayout.Label("LAN Boxing Multiplayer");
        GUILayout.Label($"Status: {connectionStatus}", GUILayout.MinHeight(36));

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null)
        {
            GUILayout.Label("NetworkManager not found");
            GUILayout.EndArea();
            return;
        }

        if (networkManager.IsListening)
        {
            string role = networkManager.IsServer
                ? (networkManager.IsClient ? "HOST" : "SERVER")
                : "CLIENT";
            int connectedPlayers = networkManager.ConnectedClientsIds.Count;
            GUILayout.Label($"Role: {role} | Client ID: {networkManager.LocalClientId}");
            GUILayout.Label($"Players connected: {connectedPlayers}");
        }
        else
        {
            GUILayout.Label("Connect to Host IP:");
            targetIpAddress = GUILayout.TextField(targetIpAddress, GUILayout.Height(25));

            if (GUILayout.Button("Start Host (Player 1)", GUILayout.Height(32)))
            {
                StartHostGame();
            }

            if (GUILayout.Button("Join Client (Player 2)", GUILayout.Height(32)))
            {
                StartClientGame();
            }
        }

        GUILayout.EndArea();
    }

    private bool TryGetNetworkManager(out NetworkManager networkManager)
    {
        networkManager = NetworkManager.Singleton;
        if (networkManager != null)
        {
            SubscribeToNetworkEvents();
            return true;
        }

        SetStatus("FAILED - No NetworkManager found in scene", true);
        return false;
    }

    private void SubscribeToNetworkEvents()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == subscribedNetworkManager)
        {
            return;
        }

        UnsubscribeFromNetworkEvents();
        subscribedNetworkManager = networkManager;

        if (subscribedNetworkManager == null)
        {
            return;
        }

        subscribedNetworkManager.OnClientConnectedCallback += OnClientConnected;
        subscribedNetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
        subscribedNetworkManager.OnServerStarted += OnServerStarted;
        subscribedNetworkManager.OnTransportFailure += OnTransportFailure;
    }

    private void UnsubscribeFromNetworkEvents()
    {
        if (subscribedNetworkManager == null)
        {
            return;
        }

        subscribedNetworkManager.OnClientConnectedCallback -= OnClientConnected;
        subscribedNetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        subscribedNetworkManager.OnServerStarted -= OnServerStarted;
        subscribedNetworkManager.OnTransportFailure -= OnTransportFailure;
        subscribedNetworkManager = null;
    }

    private void OnClientConnected(ulong clientId)
    {
        NetworkManager networkManager = subscribedNetworkManager;
        if (networkManager == null)
        {
            return;
        }

        string role = networkManager.IsServer ? "HOST" : "CLIENT";
        string message = clientId == networkManager.LocalClientId
            ? $"CONNECTED as {role} (Client ID {clientId})"
            : $"Client {clientId} connected ({networkManager.ConnectedClientsIds.Count} players)";
        SetStatus(message, false);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        NetworkManager networkManager = subscribedNetworkManager;
        string message = networkManager != null && clientId == networkManager.LocalClientId
            ? "DISCONNECTED from session"
            : $"Client {clientId} disconnected";
        SetStatus(message, true);
    }

    private void OnServerStarted()
    {
        SetStatus($"HOST RUNNING on port {connectionPort}", false);
    }

    private void OnTransportFailure()
    {
        SetStatus("TRANSPORT FAILURE - check address, port, and Console", true);
    }

    private void SetStatus(string message, bool isError)
    {
        connectionStatus = message;
        if (isError)
        {
            Debug.LogError($"[Network Debug] {message}");
        }
        else
        {
            Debug.Log($"[Network Debug] {message}");
        }
    }
}
