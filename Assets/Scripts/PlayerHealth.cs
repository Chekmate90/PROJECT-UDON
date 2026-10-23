using Unity.Netcode;
using UnityEngine;

public class PlayerHealth : NetworkBehaviour
{
    [Header("Health Settings")]
    [SerializeField] private int maxHealth = 100;

    // Synchronized variable across all connected clients
    public NetworkVariable<int> currentHealth = new NetworkVariable<int>(
        100,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public override void OnNetworkSpawn()
    {
        currentHealth.OnValueChanged += OnHealthChanged;
        if (IsServer)
        {
            currentHealth.Value = maxHealth;
        }
    }

    public override void OnNetworkDespawn()
    {
        currentHealth.OnValueChanged -= OnHealthChanged;
    }

    private void OnHealthChanged(int previousValue, int newValue)
    {
        Debug.Log($"[Player {OwnerClientId}] Health changed from {previousValue} to {newValue}");
    }

    // Called by glove hit detection; executes only on the Server/Host
    [ServerRpc(RequireOwnership = false)]
    public void ApplyDamageServerRpc(int damageAmount)
    {
        if (currentHealth.Value <= 0) return;

        currentHealth.Value = Mathf.Max(0, currentHealth.Value - damageAmount);

        if (currentHealth.Value <= 0)
        {
            TriggerKnockoutClientRpc();
        }
    }

    [ClientRpc]
    private void TriggerKnockoutClientRpc()
    {
        Debug.Log($"Player {OwnerClientId} was knocked out!");
        // Add sound effects, screen fade, or round-end triggers here
    }
}