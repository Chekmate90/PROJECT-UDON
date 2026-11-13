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
    private GUIStyle panelStyle;
    private GUIStyle titleStyle;
    private GUIStyle labelStyle;
    private GUIStyle statusStyle;
    private GUIStyle buttonStyle;

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
            SetStatus("FAILED - Unity Transport is missing");
            return;
        }

        // Accept connections from both Localhost and LAN
        networkManager.NetworkConfig.ForceSamePrefabs = false;
        string lanIp = GetLocalIPv4();
        transport.SetConnectionData(lanIp, connectionPort, "0.0.0.0");
        SetStatus($"Starting Host on 0.0.0.0:{connectionPort} (LAN: {lanIp})...");

        if (!networkManager.StartHost())
        {
            SetStatus("FAILED - StartHost returned false; check Console");
        }
    }

    public void StartClientGame()
    {
        StartClientGame(null);
    }

    public void StartClientGame(string customIp = null)
    {
        if (!TryGetNetworkManager(out NetworkManager networkManager))
        {
            return;
        }

        string ipToConnect = !string.IsNullOrEmpty(customIp)
            ? customIp
            : ((ipInputField != null && !string.IsNullOrEmpty(ipInputField.text))
                ? ipInputField.text
                : targetIpAddress);

        if (string.IsNullOrEmpty(ipToConnect))
        {
            ipToConnect = "127.0.0.1";
        }

        UnityTransport transport = networkManager.GetComponent<UnityTransport>();
        if (transport == null)
        {
            SetStatus("FAILED - Unity Transport is missing");
            return;
        }

        networkManager.NetworkConfig.ForceSamePrefabs = false;
        transport.SetConnectionData(ipToConnect, connectionPort);
        SetStatus($"Connecting to {ipToConnect}:{connectionPort}...");

        if (!networkManager.StartClient())
        {
            SetStatus("FAILED - StartClient returned false; check Console");
        }
    }

    public static string GetLocalIPv4()
    {
        try
        {
            using (var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, 0))
            {
                socket.Connect("8.8.8.8", 65530);
                var endPoint = socket.LocalEndPoint as System.Net.IPEndPoint;
                if (endPoint != null) return endPoint.Address.ToString();
            }
        }
        catch { }
        return "127.0.0.1";
    }

    private void OnGUI()
    {
        EnsureStyles();
        GUILayout.BeginArea(new Rect(20, 20, 320, 280), panelStyle);
        GUILayout.Label("LAN BOXING MULTIPLAYER", titleStyle);
        GUILayout.Space(4f);
        GUILayout.Label(connectionStatus, statusStyle, GUILayout.MinHeight(34));

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null)
        {
            GUILayout.Label("Network manager unavailable", labelStyle);
            GUILayout.EndArea();
            return;
        }

        if (networkManager.IsListening)
        {
            string role = networkManager.IsServer
                ? (networkManager.IsClient ? "HOST" : "SERVER")
                : "CLIENT";
            int connectedPlayers = networkManager.ConnectedClientsIds.Count;
            GUILayout.Space(8f);
            GUILayout.Label($"SESSION   {role}", labelStyle);
            GUILayout.Label($"PLAYERS   {connectedPlayers}", labelStyle);
            if (networkManager.IsServer)
            {
                GUILayout.Label($"LAN IP:   {GetLocalIPv4()}:{connectionPort}", labelStyle);
                GUILayout.Label($"LOCAL:    127.0.0.1:{connectionPort}", labelStyle);
            }
        }
        else
        {
            string lanIp = GetLocalIPv4();

            if (GUILayout.Button("Start Host (LAN & Local)", buttonStyle, GUILayout.Height(32)))
            {
                StartHostGame();
            }

            GUILayout.Space(4f);
            if (GUILayout.Button("Join Localhost (127.0.0.1)", buttonStyle, GUILayout.Height(28)))
            {
                StartClientGame("127.0.0.1");
            }

            if (lanIp != "127.0.0.1")
            {
                GUILayout.Space(2f);
                if (GUILayout.Button($"Join LAN ({lanIp})", buttonStyle, GUILayout.Height(28)))
                {
                    StartClientGame(lanIp);
                }
            }

            GUILayout.Space(6f);
            GUILayout.Label("CUSTOM IP ADDRESS", labelStyle);
            targetIpAddress = GUILayout.TextField(targetIpAddress, GUILayout.Height(24));
            if (GUILayout.Button("Join Custom IP", buttonStyle, GUILayout.Height(26)))
            {
                StartClientGame(targetIpAddress);
            }
        }

        GUILayout.EndArea();
    }

    private void EnsureStyles()
    {
        if (panelStyle != null) return;

        panelStyle = new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(16, 16, 14, 14),
            normal = { background = MakeTexture(new Color(0.035f, 0.05f, 0.075f, 0.94f)) }
        };
        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            normal = { textColor = new Color(0.78f, 0.84f, 0.92f) }
        };
        statusStyle = new GUIStyle(labelStyle)
        {
            wordWrap = true,
            normal = { textColor = new Color(0.45f, 0.85f, 0.68f) }
        };
        buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold
        };
    }

    private static Texture2D MakeTexture(Color color)
    {
        Texture2D texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }

    private bool TryGetNetworkManager(out NetworkManager networkManager)
    {
        networkManager = NetworkManager.Singleton;
        if (networkManager != null)
        {
            SubscribeToNetworkEvents();
            return true;
        }

        SetStatus("FAILED - No NetworkManager found in scene");
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
        SetStatus(message);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        NetworkManager networkManager = subscribedNetworkManager;
        string reason = networkManager != null && !string.IsNullOrEmpty(networkManager.DisconnectReason)
            ? $" (Reason: {networkManager.DisconnectReason})"
            : string.Empty;

        string message = networkManager != null && clientId == networkManager.LocalClientId
            ? $"DISCONNECTED from session{reason}"
            : $"Client {clientId} disconnected{reason}";
        SetStatus(message);
    }

    private void OnServerStarted()
    {
        SetStatus($"HOST RUNNING on port {connectionPort}");
    }

    private void OnTransportFailure()
    {
        SetStatus("TRANSPORT FAILURE - check address and port");
    }

    private void SetStatus(string message)
    {
        connectionStatus = message;
    }
}
