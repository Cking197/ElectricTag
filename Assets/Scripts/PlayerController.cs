using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class PlayerController : MonoBehaviour
{
    [Header("Input")] public string moveActionName = "Move";
    public string fastStepActionName = "FastStepToggle";
    public string parryActionName = "Parry";
    public string swordAngleActionName = "SwordAngle";
    private PlayerInput _playerInput;
    private InputAction _moveAction;
    private InputAction _fastStepAction;
    private InputAction _parryAction;
    private InputAction _swordAngleAction;

    [Header("Spawn")] public float leftSpawnX = -2.4f;
    public float rightSpawnX = 2.4f;
    public float spawnY = 1f;
    private Vector3 _spawnPosition;
    private int _facingDirection;

    [Header("Movement")] public float stepDistance = 0.16f;
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
    public float dashSpeed = 6f; // Minimum speed floor for dash movement
    public float knockbackDuration = 0.15f; // Duration of knockback travel — increase to slow it down
    public float minPlayerSeparation = 0.5f; // Tune to half a player width in world units

    [Header("Combat State")] private bool _isParrying;
    private float _parryActiveUntil;
    public float parryWindowSeconds = 0.2f;
    public float parryCooldownSeconds = 0.4f;
    private float _nextParryTime;
    private bool _isStunned;
    private float _stunnedUntil;
    private float _parryAngle = 0f;
    public float ParryAngle => _parryAngle;

    [Tooltip("How long the attacker is locked out of attacking after being parried.")]
    public float parryAttackLockSeconds = 0.5f;

    private float _attackLockedUntil;

    [Header("Sword Angling")] public float maxSwordAngleDegrees = 37.5f;
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

    [HideInInspector] public bool IsAttacking => _sword != null && _sword.IsAttacking;
    public bool IsParrying => _isParrying;

    private Rigidbody2D _rb;
    private SwordAttack _sword;

    [Header("Animation")] private static readonly int ParamIsMoving = Animator.StringToHash("isMoving");
    private static readonly int TriggerAttack = Animator.StringToHash("Attack");
    private static readonly int TriggerLunge = Animator.StringToHash("Lunge");
    private static readonly int TriggerBackdash = Animator.StringToHash("Backdash");
    private static readonly int TriggerReact = Animator.StringToHash("React");
    public float reactionDuration = 0.2f;
    private float _dashLingerUntil;

    private int _currentAnimState = -1;

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

        if (_isParrying && Time.time >= _parryActiveUntil)
        {
            _isParrying = false;
            UpdateVisualState();
            _sword?.SetParrySprite(false);
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
            }
            else
            {
                UpdateAnimator();
                return;
            }
        }

        if (gameManager != null && !gameManager.CanPlayersMove())
        {
            _isStepping = false;
            UpdateAnimator();
            return;
        }

        if (_moveAction == null)
            return;

        float axis = _moveAction.ReadValue<Vector2>().x;
        float absAxis = Mathf.Abs(axis);

        TryHandleDash(absAxis, axis);

        bool stepped = UpdateStepMovement();

        UpdateAnimator();

        if (stepped)
            return;

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
    private void UpdateAnimator()
    {
        if (_animator == null) return;

        bool moving = false;

        if (_isStepping)
        {
            float dist = Mathf.Abs(_stepTarget.x - transform.position.x);
            moving = dist > 0.01f;
        }

        _animator.SetBool(ParamIsMoving, moving);
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

            if (isForward)
                _animator.SetTrigger(TriggerLunge);
            else
                _animator.SetTrigger(TriggerBackdash);

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

        if (_isStunned || _isParrying || Time.time < _attackLockedUntil)
            return;

        Debug.Log($"Attack fired from {gameObject.name}");
        _sword.StartAttack();
        _animator.SetTrigger(TriggerAttack);
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
        _sword?.SetParrySprite(true);
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
        Debug.Log(
            $"{gameObject.name} parry check: parry={_parryAngle}°, attack={attackAngle}°, diff={angleDifference}°, matches={matches}");
        return matches;
    }

    public void LockAttack(float duration)
    {
        _attackLockedUntil = Mathf.Max(_attackLockedUntil, Time.time + duration);
        Debug.Log($"{gameObject.name} attack locked for {duration}s");
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
        _attackLockedUntil = 0f;
        _sword?.SetParrySprite(false);
        UpdateVisualState();

        if (_sword != null)
            _sword.SetHitboxEnabled(true);

        _currentAnimState = -1;
        if (_animator != null)
            _animator.Rebind();
    }

    public void NotifyHiltReaction()
    {
        if (_animator == null) return;
        StopCoroutine("ReactionRoutine");
        StartCoroutine("ReactionRoutine");
    }

    private IEnumerator ReactionRoutine()
    {
        _animator.SetTrigger(TriggerReact);
        yield return new WaitForSeconds(reactionDuration);
    }

    public void NotifyRightOfWayChanged(bool hasRoW)
    {
        if (_sword != null)
            _sword.SetRightOfWayVisual(hasRoW);
    }
}