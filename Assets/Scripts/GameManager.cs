using UnityEngine;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using UnityEngine.Serialization;

public class GameManager : MonoBehaviour
{
    [Header("UI References")] public GameObject Player1UI;
    public GameObject Player2UI;
    public TextMeshProUGUI countdownText;
    public static GameManager Instance; // Singleton reference

    [Header("Score")] private TextMeshProUGUI _player1ScoreUI; // Player 1 score display
    private TextMeshProUGUI _player2ScoreUI; // Player 2 score display

    [Header("Card UI")] private Image _p1WarningIcon;
    private Image _p1YellowIcon;
    private Image _p1RedIcon;
    private Image _p2WarningIcon;
    private Image _p2YellowIcon;
    private Image _p2RedIcon;

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

    public enum ScoreReason
    {
        Attack,
        OutOfBounds,
        RedCard
    }

    public enum CardLevel
    {
        None,
        Warning,
        Yellow,
        Red
    }

    public BoutState currentState = BoutState.WaitingForPlayers;

    // Coroutines
    private Coroutine _countdownRoutine;
    private Coroutine _falseStartRoutine;
    private Coroutine _haltRoutine;

    // False start tracking
    private bool _falseStartTriggered;
    private PlayerController _falseStartOffender;

    private Dictionary<PlayerController, CardLevel> _cardStates =
        new Dictionary<PlayerController, CardLevel>();

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        // set score texts
        _player1ScoreUI = Player1UI.transform.Find("Score").GetComponent<TextMeshProUGUI>();
        _player2ScoreUI = Player2UI.transform.Find("Score").GetComponent<TextMeshProUGUI>();

        // set card icons
        _p1WarningIcon = Player1UI.transform.Find("Warning").GetComponent<Image>();
        _p1YellowIcon = Player1UI.transform.Find("Yellow").GetComponent<Image>();
        _p1RedIcon = Player1UI.transform.Find("Red").GetComponent<Image>();
        _p2WarningIcon = Player2UI.transform.Find("Warning").GetComponent<Image>();
        _p2YellowIcon = Player2UI.transform.Find("Yellow").GetComponent<Image>();
        _p2RedIcon = Player2UI.transform.Find("Red").GetComponent<Image>();

        _p1WarningIcon.enabled = false;
        _p1YellowIcon.enabled = false;
        _p1RedIcon.enabled = false;

        _p2WarningIcon.enabled = false;
        _p2YellowIcon.enabled = false;
        _p2RedIcon.enabled = false;
    }

// Register player and start countdown when 2 players exist
    public void RegisterPlayer(PlayerController player)
    {
        if (_registeredPlayers.Contains(player)) return;

        _registeredPlayers.Add(player);

        if (!_cardStates.ContainsKey(player))
            _cardStates[player] = CardLevel.None;

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

        countdownText.gameObject.SetActive(true);
        countdownText.text = "HALT";
        countdownText.color = Color.red;

        yield return new WaitForSeconds(0.6f);

        PlayerController offender = _falseStartOffender;
        bool isLeft = offender.name == "Player1";
        string side = isLeft ? "LEFT" : "RIGHT";

        CardLevel currentLevel = _cardStates[offender];

        switch (currentLevel)
        {
            case CardLevel.None:
                _cardStates[offender] = CardLevel.Warning;
                countdownText.text = $"WARNING FOR {side}";
                UpdateCardUI(offender, CardLevel.Warning);
                break;

            case CardLevel.Warning:
                _cardStates[offender] = CardLevel.Yellow;
                countdownText.text = $"YELLOW CARD ON {side}";
                UpdateCardUI(offender, CardLevel.Yellow);
                break;

            case CardLevel.Yellow:
            case CardLevel.Red:
                _cardStates[offender] = CardLevel.Red;
                countdownText.text = $"RED CARD ON{side}";
                UpdateCardUI(offender, CardLevel.Red);

                PlayerController opponent =
                    _registeredPlayers.Find(p => p != offender);

                if (opponent != null)
                {
                    yield return StartCoroutine(
                        HaltAndScoreRoutine(opponent, ScoreReason.RedCard, offender));
                }

                break;
        }

        countdownText.color = Color.red;

        yield return new WaitForSeconds(1.2f);

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
            countdownText.text = "En Garde...";
            countdownText.color = Color.white;
        }

        yield return new WaitForSeconds(0.9f);

        if (countdownText != null)
            countdownText.text = "Ready...";
        countdownText.color = Color.yellow;
        yield return new WaitForSeconds(0.9f);

        if (countdownText != null)
            countdownText.text = "FENCE!";
        countdownText.color = Color.green;

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
            _haltRoutine = StartCoroutine(
                HaltAndScoreRoutine(scorer, ScoreReason.Attack));
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
    IEnumerator HaltAndScoreRoutine(
        PlayerController scorer,
        ScoreReason reason,
        PlayerController offender = null)
    {
        currentState = BoutState.Resolving;

        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(true);
            countdownText.text = "HALT";
            countdownText.color = Color.red;
        }

        yield return new WaitForSeconds(0.9f);

        bool scorerIsLeft = scorer.name == "Player1";

        // Increment score
        if (scorerIsLeft)
            _player1Score++;
        else
            _player2Score++;

        _player1ScoreUI.text = _player1Score.ToString();
        _player2ScoreUI.text = _player2Score.ToString();

        // Determine announcement side
        string leftRightForScorer = scorerIsLeft ? "LEFT" : "RIGHT";
        string message = "";

        switch (reason)
        {
            case ScoreReason.Attack:
                message = $"ATTACK {leftRightForScorer}";
                break;

            case ScoreReason.OutOfBounds:
                if (offender != null)
                {
                    bool offenderIsLeft = offender.name == "Player1";
                    string offenderSide = offenderIsLeft ? "LEFT" : "RIGHT";
                    message = $"OUT {offenderSide}";
                }

                break;

            case ScoreReason.RedCard:
                if (offender != null)
                {
                    bool offenderIsLeft = offender.name == "Player1";
                    string offenderSide = offenderIsLeft ? "LEFT" : "RIGHT";
                    message = $"RED {offenderSide}";
                }

                break;
        }

        countdownText.text = message;
        countdownText.color = Color.red;

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
            // Transfer right of way to the opponent
            PlayerController opponent = null;
            foreach (var p in _registeredPlayers)
            {
                if (p != player)
                {
                    opponent = p;
                    break;
                }
            }

            if (opponent != null)
            {
                AssignRightOfWay(opponent);
            }
            else
            {
                ClearRightOfWay();
            }
        }
    }

    public void OnPlayerLeftStrip(PlayerController offender)
    {
        if (currentState != BoutState.Fencing || _haltRoutine != null)
            return;

        PlayerController opponent = null;

        foreach (var p in _registeredPlayers)
        {
            if (p != offender)
            {
                opponent = p;
                break;
            }
        }

        if (opponent != null)
        {
            _haltRoutine = StartCoroutine(HaltAndScoreRoutine(opponent, ScoreReason.OutOfBounds, offender));
        }
    }

    private void UpdateCardUI(PlayerController player, CardLevel level)
    {
        bool isLeft = player.name == "Player1";

        Image warning = isLeft ? _p1WarningIcon : _p2WarningIcon;
        Image yellow = isLeft ? _p1YellowIcon : _p2YellowIcon;
        Image red = isLeft ? _p1RedIcon : _p2RedIcon;

        // Stacking version (recommended)
        warning.enabled = level >= CardLevel.Warning;
        yellow.enabled = level >= CardLevel.Yellow;
        red.enabled = level >= CardLevel.Red;
        Debug.Log("Updating UI for: " + player.name);
    }
}