using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

public class PlayerHealth : NetworkBehaviour
{
    [Header("Health Settings")]
    [SerializeField] private int maxHealth = 100;
    [SerializeField] private float minPunchSpeed = 1.2f;
    [SerializeField] private float maxPunchSpeed = 5f;
    [SerializeField] private float damageMultiplier = 8f;
    [SerializeField] private float hitCooldown = 0.25f;

    private readonly Dictionary<ulong,float> lastHitTimes = new();

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
    public void RequestPunchServerRPC(float reportedPunchSpeed, ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;

        ulong attackerClientId = rpcParams.Receive.SenderClientId;

        if(attackerClientId == OwnerClientId || currentHealth.Value <= 0) return;

        if (float.IsNaN(reportedPunchSpeed) || 
            float.IsInfinity(reportedPunchSpeed) ||
            reportedPunchSpeed < minPunchSpeed ||
            reportedPunchSpeed > maxPunchSpeed) return;

        float currentTime = Time.time;

        if (lastHitTimes.TryGetValue(attackerClientId, out float lastHitTime) && currentTime-lastHitTime < hitCooldown) return;
        

        lastHitTimes[attackerClientId] = currentTime;


        int damage = Mathf.RoundToInt(reportedPunchSpeed*damageMultiplier);
        int previousHealth = currentHealth.Value;

        currentHealth.Value = Mathf.Max(0, currentHealth.Value - damage);

        Debug.Log(
            $"Punch from {attackerClientId}"+$"Damage : {damage} and Health : {previousHealth} ---> {currentHealth.Value}"
        );

        if (previousHealth > 0 && currentHealth.Value == 0)
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