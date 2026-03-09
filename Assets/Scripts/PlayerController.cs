using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class PlayerController : MonoBehaviour
{
    [Header("Input")]
    public string moveActionName = "Move";
    public string fastStepActionName = "FastStepToggle";
    public string parryActionName = "Parry";
    public string swordAngleActionName = "SwordAngle";
    private PlayerInput _playerInput;
    private InputAction _moveAction;
    private InputAction _fastStepAction;
    private InputAction _parryAction;
    private InputAction _swordAngleAction;

    [Header("Spawn")]
    public float leftSpawnX = -2.4f;
    public float rightSpawnX = 2.4f;
    public float spawnY = 1f;
    private Vector3 _spawnPosition;
    private int _facingDirection;

    [Header("Movement")]
    public float stepDistance = 0.16f;
    public float fastStepDistance = 0.24f;
    public float dashDistance = 1f;
    public float stepCooldownSeconds = 0.12f;
    public float dashCooldownSeconds = 0.2f;
    public float minStepDurationSeconds = 0.08f;
    public float fastStepMinDurationSeconds = 0.1f;
    public float fullStickThreshold = 0.85f;
    public float deadzone = 0.2f;
    public float moderateSpeed = 1.4f;
    public float fastSpeed = 2.2f;
    public float dashSpeed = 6f;            // Minimum speed floor for dash movement
    public float knockbackDuration = 0.15f; // Duration of knockback travel — increase to slow it down
    public float minPlayerSeparation = 0.5f; // Tune to half a player width in world units

    [Header("Combat State")]
    private bool _isParrying;
    private float _parryActiveUntil;
    public float parryWindowSeconds = 0.2f;
    public float parryCooldownSeconds = 0.4f;
    private float _nextParryTime;
    private bool _isStunned;
    private float _stunnedUntil;
    private float _parryAngle = 0f;
    public float ParryAngle => _parryAngle;

    [Header("Sword Angling")]
    public float maxSwordAngleDegrees = 37.5f;
    private float _currentSwordAngle = 0f;
    public float parryAngleToleranceDegrees = 25f;

    public float CurrentSwordAngle => _currentSwordAngle;
    public int FacingDirection => _facingDirection;

    private bool _isStepping;
    private Vector3 _stepTarget;
    private float _stepSpeed;
    private float _nextStepTime;
    private float _nextDashTime;
    private bool _dashHeld;

    [HideInInspector]
    public bool IsAttacking => _sword != null && _sword.IsAttacking;
    public bool IsParrying => _isParrying;

    private Rigidbody2D _rb;
    private SwordAttack _sword;

    // Animator state name constants — match these exactly in the Animator Controller
    private static readonly int StateIdle = Animator.StringToHash("Idle");
    private static readonly int StateWalk = Animator.StringToHash("Walk");
    private static readonly int StateAttack = Animator.StringToHash("Attack");
    private static readonly int StateLunge = Animator.StringToHash("Lunge");
    private static readonly int StateBackdash = Animator.StringToHash("Backdash");
    private static readonly int StateReact = Animator.StringToHash("React");

    [Header("Animation")]
    public float reactionDuration = 0.2f;

    private int _currentAnimState = -1;
    private bool _isReacting;
    private bool _isLunging;
    private bool _isBackdashing;

    private Animator _animator;

    void Awake()
    {
        _playerInput = GetComponent<PlayerInput>();
        _sword = GetComponentInChildren<SwordAttack>();
        _rb = GetComponent<Rigidbody2D>();
        _animator = GetComponent<Animator>();
    }

    void OnEnable()
    {
        if (_playerInput != null)
        {
            _moveAction = _playerInput.actions[moveActionName];
            _fastStepAction = _playerInput.actions[fastStepActionName];
            _parryAction = _playerInput.actions[parryActionName];
            _swordAngleAction = _playerInput.actions[swordAngleActionName];
        }
    }

    void Start()
    {
        if (_playerInput == null)
            _playerInput = GetComponent<PlayerInput>();

        if (_playerInput != null)
        {
            _facingDirection = _playerInput.playerIndex == 0 ? 1 : -1;
            gameObject.name = "Player" + (_playerInput.playerIndex + 1).ToString();

            float spawnX = _playerInput.playerIndex == 0 ? leftSpawnX : rightSpawnX;
            _spawnPosition = new Vector3(spawnX, spawnY, transform.position.z);

            ResetPlayer();
        }

        GameManager.Instance.RegisterPlayer(this);
        var targetGroup = FindAnyObjectByType<CinemachineTargetGroup>();
        if (targetGroup != null)
        {
            targetGroup.AddMember(transform, 1f, 0.5f);
        }
    }

    void Update()
    {
        GameManager gameManager = GameManager.Instance;

        if (_isParrying)
        {
            Debug.Log($"{gameObject.name} is parrying, time: {Time.time}, expires at: {_parryActiveUntil}");
        }

        if (_isParrying && Time.time >= _parryActiveUntil)
        {
            _isParrying = false;
            UpdateVisualState();
            Debug.Log($"Parry from {gameObject.name} stopped");
        }

        UpdateSwordAngle();

        if (_isStunned)
        {
            if (Time.time >= _stunnedUntil)
            {
                _isStunned = false;
                SetPlayerColor(Color.white);

                if (_sword != null)
                    _sword.SetHitboxEnabled(true);

                Debug.Log($"{gameObject.name} stun ended");
            }
            else
            {
                UpdateAnimator(0f);
                return;
            }
        }

        if (gameManager != null && !gameManager.CanPlayersMove())
        {
            _isStepping = false;
            UpdateAnimator(0f);
            return;
        }

        if (_moveAction == null)
            return;

        float axis = _moveAction.ReadValue<Vector2>().x;
        float absAxis = Mathf.Abs(axis);

        TryHandleDash(absAxis, axis);

        if (UpdateStepMovement())
        {
            float animSpeed = (_isLunging || _isBackdashing) ? 0f : ((_stepTarget.x - transform.position.x) * _facingDirection >= 0f ? absAxis : -absAxis);
            UpdateAnimator(animSpeed);
            return;
        }

        UpdateAnimator(0f);

        if (Time.time < _nextStepTime)
            return;

        if (absAxis < deadzone)
            return;

        if (gameManager != null && gameManager.currentState == GameManager.BoutState.Countdown)
            gameManager.OnEarlyMovement(this);

        float direction = Mathf.Sign(axis);
        bool isFullStick = absAxis >= fullStickThreshold;

        StartStep(direction, isFullStick);
    }

    // Plays a state immediately if it isn't already playing
    private void PlayAnimState(int stateHash)
    {
        if (_animator == null || _currentAnimState == stateHash) return;
        _currentAnimState = stateHash;
        _animator.Play(stateHash);
    }

    // Called every frame — highest priority wins, falls through to lower
    private void UpdateAnimator(float speed)
    {
        if (_animator == null) return;

        if (IsAttacking) { PlayAnimState(StateAttack); return; }
        if (_isReacting) { PlayAnimState(StateReact); return; }
        if (_isLunging) { PlayAnimState(StateLunge); return; }
        if (_isBackdashing) { PlayAnimState(StateBackdash); return; }
        if (Mathf.Abs(speed) > 0.1f) { PlayAnimState(StateWalk); return; }
        PlayAnimState(StateIdle);
    }

    // Called by SwordAttack at the end of ThrustRoutine so the lunge clip resets
    public void NotifyAttackFinished()
    {
        // Force re-evaluation on next UpdateAnimator call
        _currentAnimState = -1;
    }

    private void TryHandleDash(float absAxis, float axis)
    {
        if (_fastStepAction == null)
            return;

        bool dashPressed = _fastStepAction.IsPressed();

        if (!dashPressed)
            _dashHeld = false;

        if (dashPressed && !_dashHeld && Time.time >= _nextDashTime)
        {
            float dashDirection = absAxis >= deadzone ? Mathf.Sign(axis) : _facingDirection;
            _stepTarget = transform.position + new Vector3(dashDirection * dashDistance, 0f, 0f);
            _stepSpeed = Mathf.Max(dashSpeed, dashDistance / minStepDurationSeconds);
            _isStepping = true;
            _nextDashTime = Time.time + dashCooldownSeconds;
            _dashHeld = true;

            bool isForward = (dashDirection * _facingDirection) > 0f;
            _isLunging = isForward;
            _isBackdashing = !isForward;

            NotifyMovementDirection(dashDirection);
            if (GameManager.Instance != null &&
                GameManager.Instance.currentState == GameManager.BoutState.Countdown)
            {
                GameManager.Instance.OnEarlyMovement(this);
            }
        }
    }

    private bool UpdateStepMovement()
    {
        if (!_isStepping)
            return false;

        // Clamp step target to maintain minimum separation from opponent
        PlayerController opponent = GetOpponent();
        if (opponent != null)
        {
            float opponentX = opponent.transform.position.x;
            // The closest X we're allowed to reach, facing the opponent
            float clampedX = opponentX - _facingDirection * minPlayerSeparation;
            // Only clamp if moving toward the opponent
            if (_facingDirection == 1)
                _stepTarget.x = Mathf.Min(_stepTarget.x, clampedX);
            else
                _stepTarget.x = Mathf.Max(_stepTarget.x, clampedX);
        }

        transform.position = Vector3.MoveTowards(transform.position, _stepTarget, _stepSpeed * Time.deltaTime);

        if (Vector3.SqrMagnitude(transform.position - _stepTarget) < 0.0001f)
        {
            transform.position = _stepTarget;
            _isStepping = false;
            _isLunging = false;
            _isBackdashing = false;
        }

        return true;
    }

    private PlayerController GetOpponent()
    {
        if (GameManager.Instance == null) return null;
        foreach (var p in GameManager.Instance.RegisteredPlayers)
        {
            if (p != this) return p;
        }
        return null;
    }

    private void UpdateVisualState()
    {
        if (_isStunned)
            SetPlayerColor(Color.red);
        // else if (_isParrying)
        // SetPlayerColor(Color.green);
        else
            SetPlayerColor(Color.white);
    }

    private void UpdateSwordAngle()
    {
        if (_swordAngleAction == null || _sword == null)
            return;

        if (IsAttacking)
            return;

        Vector2 stickInput = _swordAngleAction.ReadValue<Vector2>();

        if (stickInput.magnitude < 0.2f)
            _currentSwordAngle = 0f;
        else
            _currentSwordAngle = stickInput.y * maxSwordAngleDegrees;

        _sword.SetAngle(_currentSwordAngle);
    }

    private void StartStep(float direction, bool isFullStick)
    {
        float distance = isFullStick ? fastStepDistance : stepDistance;
        _stepTarget = transform.position + new Vector3(direction * distance, 0f, 0f);
        _stepSpeed = isFullStick ? fastSpeed : moderateSpeed;

        if (isFullStick)
            _stepSpeed = Mathf.Min(_stepSpeed, distance / fastStepMinDurationSeconds);

        _isStepping = true;
        _nextStepTime = Time.time + stepCooldownSeconds;

        NotifyMovementDirection(direction);
    }

    private void NotifyMovementDirection(float movementX)
    {
        if (GameManager.Instance == null)
            return;

        float dot = movementX * _facingDirection;

        if (dot > 0f)
            GameManager.Instance.OnOffensiveAction(this);
        else if (dot < 0f)
            GameManager.Instance.OnRetreat(this);
    }

    public void OnAttack(InputAction.CallbackContext context)
    {
        if (!context.performed || _sword == null)
            return;

        if (_isStunned || _isParrying)
            return;

        Debug.Log($"Attack fired from {gameObject.name}");
        _sword.StartAttack();
        GameManager.Instance.OnOffensiveAction(this);
    }

    public void OnParry(InputAction.CallbackContext context)
    {
        if (!context.performed)
            return;

        if (_isStunned || IsAttacking)
            return;

        if (Time.time < _nextParryTime)
            return;

        Debug.Log($"{gameObject.name} initiated parry at angle {_currentSwordAngle}");

        _isParrying = true;
        _parryActiveUntil = Time.time + parryWindowSeconds;
        _nextParryTime = Time.time + parryCooldownSeconds;
        _parryAngle = _currentSwordAngle;
        UpdateVisualState();
    }

    public bool IsInParryWindow()
    {
        bool result = _isParrying && Time.time < _parryActiveUntil;
        Debug.Log($"{gameObject.name} parry checked, they are {(result ? "" : "not ")}parrying");
        return result;
    }

    public bool DoesParryMatchAttack(float attackAngle)
    {
        float angleDifference = Mathf.Abs(Mathf.DeltaAngle(_parryAngle, attackAngle));
        bool matches = angleDifference <= parryAngleToleranceDegrees;
        Debug.Log($"{gameObject.name} parry check: parry={_parryAngle}°, attack={attackAngle}°, diff={angleDifference}°, matches={matches}");
        return matches;
    }

    public void ApplyStun(float duration)
    {
        _isStunned = true;
        _stunnedUntil = Time.time + duration;

        if (_sword != null)
            _sword.SetHitboxEnabled(false);

        _isStepping = false;
        UpdateVisualState();
        Debug.Log($"{gameObject.name} stunned for {duration} seconds");
    }

    public void ApplyKnockback(float distance)
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnRetreat(this);

        float knockbackDirection = -_facingDirection;
        _stepTarget = transform.position + new Vector3(knockbackDirection * distance, 0f, 0f);
        _stepSpeed = Mathf.Max(dashSpeed, distance / knockbackDuration);
        _isStepping = true;

        Debug.Log($"{gameObject.name} knocked back {distance} units");
    }

    private void SetPlayerColor(Color color)
    {
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null) sr.color = color;
    }

    public void CancelAttack()
    {
        if (_sword != null)
            _sword.CancelAttack();
    }

    private void SetFacingDirection(int direction)
    {
        Vector3 scale = transform.localScale;
        scale.x = Mathf.Abs(scale.x) * direction;
        transform.localScale = scale;
    }

    public void ResetPlayer()
    {
        _isStepping = false;
        _dashHeld = false;

        if (_rb != null)
        {
            _rb.linearVelocity = Vector2.zero;
            _rb.angularVelocity = 0f;
        }

        SpriteRenderer[] allSprites = GetComponentsInChildren<SpriteRenderer>();
        foreach (SpriteRenderer sprite in allSprites)
            sprite.enabled = true;

        transform.position = _spawnPosition;
        SetFacingDirection(_facingDirection);

        _isStunned = false;
        _isParrying = false;
        UpdateVisualState();

        if (_sword != null)
            _sword.SetHitboxEnabled(true);

        _isLunging = false;
        _isBackdashing = false;
        _isReacting = false;
        _currentAnimState = -1;
        if (_animator != null)
            _animator.Play(StateIdle);
    }

    public void NotifyHiltReaction()
    {
        if (_animator == null) return;
        StopCoroutine("ReactionRoutine");
        StartCoroutine("ReactionRoutine");
    }

    private IEnumerator ReactionRoutine()
    {
        _isReacting = true;
        _currentAnimState = -1;
        yield return new WaitForSeconds(reactionDuration);
        _isReacting = false;
        _currentAnimState = -1;
    }

    public void NotifyRightOfWayChanged(bool hasRoW)
    {
        if (_sword != null)
            _sword.SetRightOfWayVisual(hasRoW);
    }
}