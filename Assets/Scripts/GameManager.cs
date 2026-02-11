using UnityEngine;
using TMPro;
using UnityEngine.Serialization;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;
    public TextMeshProUGUI player1ScoreUI;
    public TextMeshProUGUI player2ScoreUI;
    
    private int _player1Score;
    private int _player2Score;

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    public void OnPlayerHit(PlayerController attacker, PlayerController victim)
    {
        //increment scores:
        if (attacker.name == "Player1")
        {
            _player1Score++;
        }
        else
        {
            _player2Score++;
        }
        
        //update score UI
        player1ScoreUI.text = _player1Score.ToString();
        player2ScoreUI.text = _player2Score.ToString();
        
        // Reset both players
        attacker.ResetPlayer();
        victim.ResetPlayer();
    }
}
