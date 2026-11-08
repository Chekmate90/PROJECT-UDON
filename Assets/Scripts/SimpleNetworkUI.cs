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

        transport.SetConnectionData("0.0.0.0", connectionPort);
        SetStatus($"Starting Host on port {connectionPort}...");

        if (!networkManager.StartHost())
        {
            SetStatus("FAILED - StartHost returned false; check Console");
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
            SetStatus("FAILED - Unity Transport is missing");
            return;
        }

        transport.SetConnectionData(ipToConnect, connectionPort);
        SetStatus($"Connecting to {ipToConnect}:{connectionPort}...");

        if (!networkManager.StartClient())
        {
            SetStatus("FAILED - StartClient returned false; check Console");
        }
    }

    private void OnGUI()
    {
        EnsureStyles();
        GUILayout.BeginArea(new Rect(20, 20, 300, 220), panelStyle);
        GUILayout.Label("LAN BOXING", titleStyle);
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
        }
        else
        {
            GUILayout.Label("HOST IP ADDRESS", labelStyle);
            targetIpAddress = GUILayout.TextField(targetIpAddress, GUILayout.Height(30));
            GUILayout.Space(8f);

            if (GUILayout.Button("Start Host", buttonStyle, GUILayout.Height(34)))
            {
                StartHostGame();
            }

            if (GUILayout.Button("Join Game", buttonStyle, GUILayout.Height(34)))
            {
                StartClientGame();
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
        string message = networkManager != null && clientId == networkManager.LocalClientId
            ? "DISCONNECTED from session"
            : $"Client {clientId} disconnected";
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
