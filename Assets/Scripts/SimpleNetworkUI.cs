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
    }

    public void StartHostGame()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("No NetworkManager found in scene!");
            return;
        }

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport != null)
        {
            // Bind to all local adapters
            transport.SetConnectionData("0.0.0.0", connectionPort);
        }

        NetworkManager.Singleton.StartHost();
    }

    public void StartClientGame()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("No NetworkManager found in scene!");
            return;
        }

        string ipToConnect = (ipInputField != null && !string.IsNullOrEmpty(ipInputField.text))
            ? ipInputField.text
            : targetIpAddress;

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport != null)
        {
            transport.SetConnectionData(ipToConnect, connectionPort);
        }

        NetworkManager.Singleton.StartClient();
    }

    // On-screen buttons rendered automatically in the Game view
    private void OnGUI()
    {
        if (NetworkManager.Singleton == null) return;
        if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer) return;

        GUILayout.BeginArea(new Rect(20, 20, 240, 160), GUI.skin.box);
        GUILayout.Label("<b>LAN Boxing Multiplayer</b>");

        if (GUILayout.Button("Start Host (Player 1)", GUILayout.Height(35)))
        {
            StartHostGame();
        }

        GUILayout.Space(8);
        GUILayout.Label("Connect to Host IP:");
        targetIpAddress = GUILayout.TextField(targetIpAddress, GUILayout.Height(25));

        if (GUILayout.Button("Join Client (Player 2)", GUILayout.Height(35)))
        {
            StartClientGame();
        }

        GUILayout.EndArea();
    }
}