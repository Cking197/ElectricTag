using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    public void OnPlayerHit(PlayerController attacker, PlayerController victim)
    {
        // Reset both players
        attacker.ResetPlayer();
        victim.ResetPlayer();
    }
}
