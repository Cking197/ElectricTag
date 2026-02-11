using UnityEngine;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Serialization;

public class GameManager : MonoBehaviour
{
    [Header("References")]
    public static GameManager Instance;               // Singleton reference
    public TextMeshProUGUI player1ScoreUI;           // Player 1 score display
    public TextMeshProUGUI player2ScoreUI;           // Player 2 score display
    public TextMeshProUGUI countdownText;            // Countdown or referee messages

    [Header("Player State")]
    private int _player1Score;
    private int _player2Score;
    private List<PlayerController> _registeredPlayers = new List<PlayerController>();

    public enum BoutState
    {
        WaitingForPlayers,
        Settling,
        Countdown,
        Fencing,
        Resolving
    }
    public BoutState currentState = BoutState.WaitingForPlayers;

    // Coroutines
    private Coroutine _countdownRoutine;
    private Coroutine _falseStartRoutine;
    private Coroutine _haltRoutine;

    // False start tracking
    private bool _falseStartTriggered;
    private PlayerController _falseStartOffender;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    // Register player and start countdown when 2 players exist
    public void RegisterPlayer(PlayerController player)
    {
        if (_registeredPlayers.Contains(player)) return;

        _registeredPlayers.Add(player);

        if (_registeredPlayers.Count == 2)
        {
            StartCountdown();
        }
    }

    // Determines if players are allowed to move
    public bool CanPlayersMove()
    {
        return currentState == BoutState.Countdown || currentState == BoutState.Fencing;
    }

    // Called when a player moves during countdown (potential false start)
    public void OnEarlyMovement(PlayerController offender)
    {
        if (currentState != BoutState.Countdown || _falseStartTriggered)
            return;

        _falseStartTriggered = true;
        _falseStartOffender = offender;

        // Stop countdown if active
        if (_countdownRoutine != null)
        {
            StopCoroutine(_countdownRoutine);
            _countdownRoutine = null;
        }

        _falseStartRoutine = StartCoroutine(FalseStartRoutine());
    }

    // Handles false start sequence
    IEnumerator FalseStartRoutine()
    {
        currentState = BoutState.Resolving;

        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(true);
            countdownText.text = "HALT";
        }

        yield return new WaitForSeconds(0.6f);

        if (countdownText != null)
        {
            string side = _falseStartOffender.name == "Player1" ? "LEFT" : "RIGHT";
            countdownText.text = $"FALSE START {side}";
        }

        yield return new WaitForSeconds(0.9f);

        ResetAllPlayers();
        yield return new WaitForSeconds(0.5f);

        _falseStartTriggered = false;
        _falseStartOffender = null;

        StartCountdown();
    }

    // Starts the countdown routine
    public void StartCountdown()
    {
        if (_countdownRoutine != null)
            StopCoroutine(_countdownRoutine);

        _countdownRoutine = StartCoroutine(CountdownRoutine());
    }

    // Countdown display before fencing begins
    IEnumerator CountdownRoutine()
    {
        currentState = BoutState.Settling;

        ResetAllPlayers();
        yield return new WaitForSeconds(0.6f);

        currentState = BoutState.Countdown;

        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(true);
            countdownText.text = "EN GARDE";
        }
        yield return new WaitForSeconds(0.9f);

        if (countdownText != null)
            countdownText.text = "READY";
        yield return new WaitForSeconds(0.9f);

        if (countdownText != null)
            countdownText.text = "FENCE";

        currentState = BoutState.Fencing;
        yield return new WaitForSeconds(1.0f);

        if (countdownText != null)
            countdownText.gameObject.SetActive(false);

        _countdownRoutine = null;
    }

    // Called when a player scores
    public void OnPlayerHit(PlayerController attacker)
    {
        if (currentState != BoutState.Fencing || _falseStartTriggered || _haltRoutine != null)
            return;

        _haltRoutine = StartCoroutine(HaltAndScoreRoutine(attacker));
    }

    // Handles halt, scoring, and reset after a touch
    IEnumerator HaltAndScoreRoutine(PlayerController attacker)
    {
        currentState = BoutState.Resolving;

        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(true);
            countdownText.text = "HALT";
        }

        yield return new WaitForSeconds(0.9f);

        if (attacker.name == "Player1")
        {
            _player1Score++;
            countdownText.text = "ATTACK LEFT";
        }
        else
        {
            _player2Score++;
            countdownText.text = "ATTACK RIGHT";
        }

        player1ScoreUI.text = _player1Score.ToString();
        player2ScoreUI.text = _player2Score.ToString();

        yield return new WaitForSeconds(1.2f);

        ResetAllPlayers();
        yield return new WaitForSeconds(0.6f);

        _haltRoutine = null;

        StartCountdown();
    }

    // Resets all players to spawn positions
    void ResetAllPlayers()
    {
        foreach (var player in _registeredPlayers)
        {
            player.ResetPlayer();
        }
    }
}