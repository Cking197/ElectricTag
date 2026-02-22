using UnityEngine;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Serialization;

public class GameManager : MonoBehaviour
{
    [Header("References")] public static GameManager Instance; // Singleton reference
    public TextMeshProUGUI player1ScoreUI; // Player 1 score display
    public TextMeshProUGUI player2ScoreUI; // Player 2 score display
    public TextMeshProUGUI countdownText; // Countdown or referee messages

    [Header("Player State")] private int _player1Score;
    private int _player2Score;
    private List<PlayerController> _registeredPlayers = new List<PlayerController>();

    [Header("Movement Lock")] public float resetMovementLockSeconds = 0.1f;
    private float _movementLockUntil;

    [Header("Combat")] public float parryStunDuration = 0.3f;

    [Header("Right Of Way")] public float initiativeDuration = 0.35f;

    private PlayerController _rightOfWayHolder;
    private Coroutine _rightOfWayExpireRoutine;

    private List<PlayerController> _pendingHitAttackers = new List<PlayerController>();
    private Coroutine _hitResolutionRoutine;
    public float simultaneousHitWindow = 0.12f;

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
        if (Time.time < _movementLockUntil)
            return false;

        return currentState == BoutState.Countdown || currentState == BoutState.Fencing;
    }

    public void LockMovement(float seconds)
    {
        _movementLockUntil = Mathf.Max(_movementLockUntil, Time.time + seconds);
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

    private IEnumerator ResolveHitsAfterWindow()
    {
        yield return new WaitForSeconds(simultaneousHitWindow);

        PlayerController scorer = null;

        if (_pendingHitAttackers.Count == 1)
        {
            scorer = _pendingHitAttackers[0];
        }
        else if (_pendingHitAttackers.Count >= 2)
        {
            // If both hit → RoW decides
            if (_rightOfWayHolder != null &&
                _pendingHitAttackers.Contains(_rightOfWayHolder))
            {
                scorer = _rightOfWayHolder;
            }
            else
            {
                // No active RoW → no score (the simultaneous scenario)
                scorer = null;
            }
        }

        _pendingHitAttackers.Clear();
        _hitResolutionRoutine = null;

        if (scorer != null)
            _haltRoutine = StartCoroutine(HaltAndScoreRoutine(scorer));
    }

    // Called when a player scores
    public void OnPlayerHit(PlayerController attacker)
    {
        if (currentState != BoutState.Fencing || _falseStartTriggered || _haltRoutine != null)
            return;

        if (!_pendingHitAttackers.Contains(attacker))
            _pendingHitAttackers.Add(attacker);

        if (_hitResolutionRoutine == null)
            _hitResolutionRoutine = StartCoroutine(ResolveHitsAfterWindow());
    }

    // Called when a player successfully parries an attack
    public void OnSuccessfulParry(PlayerController attacker, PlayerController parrier)
    {
        if (currentState != BoutState.Fencing || _falseStartTriggered || _haltRoutine != null)
            return;

        Debug.Log($"{parrier.name} parried {attacker.name} - stunning attacker");

        attacker.ApplyStun(parryStunDuration);
        attacker.CancelAttack();
        AssignRightOfWay(parrier);
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
        LockMovement(resetMovementLockSeconds);

        ClearRightOfWay();

        foreach (var player in _registeredPlayers)
        {
            player.ResetPlayer();
        }

        LockMovement(resetMovementLockSeconds);
    }

    public bool HasActiveRightOfWay(PlayerController player)
    {
        return _rightOfWayHolder == player;
    }

    private void ClearRightOfWay()
    {
        if (_rightOfWayHolder != null)
            _rightOfWayHolder.NotifyRightOfWayChanged(false);

        _rightOfWayHolder = null;

        if (_rightOfWayExpireRoutine != null)
        {
            StopCoroutine(_rightOfWayExpireRoutine);
            _rightOfWayExpireRoutine = null;
        }
    }

    private void AssignRightOfWay(PlayerController player)
    {
        if (_rightOfWayHolder == player)
        {
            RestartRightOfWayTimer();
            return;
        }

        if (_rightOfWayHolder != null)
            _rightOfWayHolder.NotifyRightOfWayChanged(false);

        _rightOfWayHolder = player;

        if (_rightOfWayHolder != null)
            _rightOfWayHolder.NotifyRightOfWayChanged(true);

        RestartRightOfWayTimer();
    }

    private void RestartRightOfWayTimer()
    {
        if (_rightOfWayExpireRoutine != null)
            StopCoroutine(_rightOfWayExpireRoutine);

        _rightOfWayExpireRoutine = StartCoroutine(RightOfWayExpireRoutine());
    }

    private IEnumerator RightOfWayExpireRoutine()
    {
        yield return new WaitForSeconds(initiativeDuration);

        ClearRightOfWay();
    }

    public void OnOffensiveAction(PlayerController player)
    {
        if (currentState != BoutState.Fencing)
            return;

        if (_rightOfWayHolder == null)
        {
            // No active RoW, assign to player
            AssignRightOfWay(player);
        }
        else if (_rightOfWayHolder == player)
        {
            // Player already has RoW, refresh timer
            RestartRightOfWayTimer();
        }
        // else opponent has RoW → do nothing
    }

    public void OnRetreat(PlayerController player)
    {
        if (currentState != BoutState.Fencing)
            return;

        if (_rightOfWayHolder == player)
        {
            ClearRightOfWay();
        }
    }

    public void OnAttackMissed(PlayerController player)
    {
        if (currentState != BoutState.Fencing)
            return;

        if (_rightOfWayHolder == player)
        {
            ClearRightOfWay();
        }
    }
}