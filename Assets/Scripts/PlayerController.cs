using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
public class PlayerController : MonoBehaviour
{
    [Header("Input")]
    public string moveActionName = "Move";           // Name of movement action
    public string fastStepActionName = "FastStepToggle"; // Name of fast step toggle
    public string parryActionName = "Parry"; // Name of parry action
    public string swordAngleActionName = "SwordAngle";  // et cetera
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
    public float dashSpeed = 5f;

    [Header("Combat State")]
    private bool _isParrying;
    private float _parryActiveUntil;
    public float parryWindowSeconds = 0.2f;   // Active time
    public float parryCooldownSeconds = 0.4f; // Cooldown before you can parry again
    private float _nextParryTime;
    private bool _isStunned;
    private float _stunnedUntil;
    private float _parryAngle = 0f;  // angle locked in on parry
    public float ParryAngle => _parryAngle;  // getter

    [Header("Sword Angling")]
    public float maxSwordAngleDegrees = 37.5f;
    private float _currentSwordAngle = 0f;
    public float parryAngleToleranceDegrees = 25f;

    public float CurrentSwordAngle => _currentSwordAngle;  // Public getter
    public int FacingDirection => _facingDirection;             // Public getter

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

    void Awake()
    {
        _playerInput = GetComponent<PlayerInput>();
        _sword = GetComponentInChildren<SwordAttack>();
        _rb = GetComponent<Rigidbody2D>();
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
                {
                    _sword.SetHitboxEnabled(true);
                }

                Debug.Log($"{gameObject.name} stun ended");
            }
            else
            {
                // Still stunned - can't do anything
                return;
            }
        }

        if (gameManager != null && !gameManager.CanPlayersMove())
        {
            _isStepping = false;
            return;
        }

        if (gameManager != null && !gameManager.CanPlayersMove())
        {
            _isStepping = false;
            return;
        }

        if (_moveAction == null)
            return;

        float axis = _moveAction.ReadValue<Vector2>().x;
        float absAxis = Mathf.Abs(axis);

        TryHandleDash(absAxis, axis);

        if (UpdateStepMovement())
            return;

        if (Time.time < _nextStepTime)
            return;

        // Ignore small input
        if (absAxis < deadzone)
            return;

        // Flag false start if moving during countdown, but still allow movement
        if (gameManager != null && gameManager.currentState == GameManager.BoutState.Countdown)
            gameManager.OnEarlyMovement(this);

        float direction = Mathf.Sign(axis);
        bool isFullStick = absAxis >= fullStickThreshold;

        StartStep(direction, isFullStick);
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

        transform.position = Vector3.MoveTowards(transform.position, _stepTarget, _stepSpeed * Time.deltaTime);

        if (transform.position == _stepTarget)
            _isStepping = false;

        return true;
    }

    // Update visual state based on current player state
    private void UpdateVisualState()
    {
        if (_isStunned)
        {
            SetPlayerColor(Color.red);
        }
        else if (_isParrying)
        {
            SetPlayerColor(Color.green);
        }
        else
        {
            SetPlayerColor(Color.white);
        }
    }

    // Update sword angle based on right stick input
    private void UpdateSwordAngle()
    {
        if (_swordAngleAction == null || _sword == null)
            return;

        // Can't change angle while attacking
        if (IsAttacking)
            return;

        Vector2 stickInput = _swordAngleAction.ReadValue<Vector2>();

        // Map stick Y directly to sword angle: up = positive angle, down = negative
        if (stickInput.magnitude < 0.2f)
            _currentSwordAngle = 0f;
        else
            _currentSwordAngle = stickInput.y * maxSwordAngleDegrees;

        // Apply rotation to sword
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
        {
            GameManager.Instance.OnOffensiveAction(this);
        }
        else if (dot < 0f)
        {
            GameManager.Instance.OnRetreat(this);
        }
    }

    // Trigger sword attack
    public void OnAttack(InputAction.CallbackContext context)
    {
        if (!context.performed || _sword == null)
            return;

        if (_isStunned)
            return;

        if (_isParrying)
            return;

        Debug.Log($"Attack fired from {gameObject.name}");
        _sword.StartAttack();
        GameManager.Instance.OnOffensiveAction(this);
    }

    // Trigger parry
    public void OnParry(InputAction.CallbackContext context)
    {
        if (!context.performed)
            return;

        if (_isStunned)
            return;

        if (IsAttacking)
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

    // Helper for external parry check
    public bool IsInParryWindow()
    {
        bool yes_or_no = _isParrying && Time.time < _parryActiveUntil;
        if (yes_or_no)
        {
            Debug.Log($"{gameObject.name} parry checked, they are parrying");
        }
        else
        {
            Debug.Log($"{gameObject.name} parry checked, they are not parrying");
        }
        return yes_or_no;
    }

    // Check if a parry angle matches an attack angle relative to tolerance
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
        {
            _sword.SetHitboxEnabled(false);
        }

        // Stop any movement
        _isStepping = false;

        UpdateVisualState();

        Debug.Log($"{gameObject.name} stunned for {duration} seconds");
    }

    public void ApplyKnockback(float distance)
    {
        // Give up ROW on knockback
        if (GameManager.Instance != null)
            GameManager.Instance.OnRetreat(this);

        // Knock the player backward relative to their facing direction
        float knockbackDirection = -_facingDirection;
        _stepTarget = transform.position + new Vector3(knockbackDirection * distance, 0f, 0f);
        _stepSpeed = Mathf.Max(dashSpeed, distance / minStepDurationSeconds);
        _isStepping = true;

        Debug.Log($"{gameObject.name} knocked back {distance} units");
    }

    // Change player color
    private void SetPlayerColor(Color color)
    {
        SpriteRenderer player = GetComponent<SpriteRenderer>();
        player.color = color;
    }

    // Cancel ongoing attack
    public void CancelAttack()
    {
        if (_sword != null)
        {
            _sword.CancelAttack();
        }
    }

    // Adjust player facing
    private void SetFacingDirection(int direction)
    {
        Vector3 scale = transform.localScale;
        scale.x = Mathf.Abs(scale.x) * direction;
        transform.localScale = scale;
    }

    // Reset position, facing, and movement state
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
        {
            sprite.enabled = true;
        }

        transform.position = _spawnPosition;
        SetFacingDirection(_facingDirection);

        _isStunned = false;
        _isParrying = false;
        UpdateVisualState();

        if (_sword != null)
        {
            _sword.SetHitboxEnabled(true);
        }
    }
    public void NotifyRightOfWayChanged(bool hasRoW)
    {
        if (_sword != null)
            _sword.SetRightOfWayVisual(hasRoW);
    }
}