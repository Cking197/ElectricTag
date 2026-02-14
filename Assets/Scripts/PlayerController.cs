using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
public class PlayerController : MonoBehaviour
{
    [Header("Input")]
    public string moveActionName = "Move";           // Name of movement action
    public string fastStepActionName = "FastStepToggle"; // Name of fast step toggle
    public string parryActionName = "Parry"; // Name of parry action
    private PlayerInput _playerInput;
    private InputAction _moveAction;
    private InputAction _fastStepAction;
    private InputAction _parryAction;

    [Header("Spawn")]
    public float leftSpawnX = -2.4f;
    public float rightSpawnX = 2.4f;
    public float spawnY = 0.5f;
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

        if (_isStunned)
        {
            if (Time.time >= _stunnedUntil)
            {
                _isStunned = false;
                SetPlayerColor(Color.white);
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

    private void StartStep(float direction, bool isFullStick)
    {
        float distance = isFullStick ? fastStepDistance : stepDistance;
        _stepTarget = transform.position + new Vector3(direction * distance, 0f, 0f);
        _stepSpeed = isFullStick ? fastSpeed : moderateSpeed;

        if (isFullStick)
            _stepSpeed = Mathf.Min(_stepSpeed, distance / fastStepMinDurationSeconds);

        _isStepping = true;
        _nextStepTime = Time.time + stepCooldownSeconds;
    }

    // Trigger sword attack
    public void OnAttack(InputAction.CallbackContext context)
    {
        if (!context.performed || _sword == null)
            return;
        
        if (_isStunned)
            return;

        Debug.Log($"Attack fired from {gameObject.name}");
        _sword.StartAttack();
    }
    
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
        
        Debug.Log($"{gameObject.name} initiated parry");
        
        _isParrying = true;
        _parryActiveUntil = Time.time + parryWindowSeconds;
        _nextParryTime = Time.time + parryCooldownSeconds;
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

    public void ApplyStun(float duration)
    {
        _isStunned = true;
        _stunnedUntil = Time.time + duration;
        
        // Stop any movement
        _isStepping = false;

        UpdateVisualState();
        
        Debug.Log($"{gameObject.name} stunned for {duration} seconds");
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
    }
}