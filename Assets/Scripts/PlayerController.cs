using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [Header("Input")]
    public string moveActionName = "Move";           // Name of movement action
    public string fastStepActionName = "FastStepToggle"; // Name of fast step toggle
    private PlayerInput _playerInput;
    private InputAction _moveAction;
    private InputAction _fastStepAction;

    [Header("Spawn")]
    public float leftSpawnX = -2.4f;
    public float rightSpawnX = 2.4f;
    public float spawnY = 0.5f;
    private Vector3 _spawnPosition;
    private int _facingDirection;

    [Header("Movement")]
    public float stepDistance = 0.16f;
    public float fullStickThreshold = 0.85f;
    public float deadzone = 0.2f;
    public float slowSpeed = 0.9f;
    public float moderateSpeed = 1.4f;
    public float fastSpeed = 2.2f;

    private bool _isStepping;
    private bool _fastLatched;
    private Vector3 _stepTarget;
    private float _stepSpeed;

    private Rigidbody2D _rb;
    private SwordAttack sword;

    void Awake()
    {
        _playerInput = GetComponent<PlayerInput>();
        sword = GetComponentInChildren<SwordAttack>();
        _rb = GetComponent<Rigidbody2D>();
    }

    void OnEnable()
    {
        if (_playerInput != null)
        {
            _moveAction = _playerInput.actions[moveActionName];
            _fastStepAction = _playerInput.actions[fastStepActionName];
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
    }

    void Update()
    {
        // Handle ongoing step movement
        if (_isStepping)
        {
            transform.position = Vector3.MoveTowards(transform.position, _stepTarget, _stepSpeed * Time.deltaTime);

            if (transform.position == _stepTarget)
                _isStepping = false;

            return;
        }

        if (_moveAction == null)
            return;

        float axis = _moveAction.ReadValue<Vector2>().x;
        float absAxis = Mathf.Abs(axis);

        // Ignore small input
        if (absAxis < deadzone)
        {
            _fastLatched = false;
            return;
        }

        // Flag false start if moving during countdown, but still allow movement
        if (GameManager.Instance.currentState == GameManager.BoutState.Countdown)
        {
            GameManager.Instance.OnEarlyMovement(this);
        }

        float direction = Mathf.Sign(axis);
        bool isFullStick = absAxis >= fullStickThreshold;

        if (!_fastLatched && _fastStepAction != null && _fastStepAction.IsPressed())
            _fastLatched = true;

        // Calculate step target and speed
        _stepTarget = transform.position + new Vector3(direction * stepDistance, 0f, 0f);
        _stepSpeed = _fastLatched ? fastSpeed : (isFullStick ? moderateSpeed : slowSpeed);
        _isStepping = true;
    }

    // Trigger sword attack
    public void OnAttack(InputAction.CallbackContext context)
    {
        if (!context.performed || sword == null)
            return;

        Debug.Log($"Attack fired from {gameObject.name}");
        sword.StartAttack();
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
        _fastLatched = false;

        if (_rb != null)
        {
            _rb.linearVelocity = Vector2.zero;
            _rb.angularVelocity = 0f;
        }

        transform.position = _spawnPosition;
        SetFacingDirection(_facingDirection);
    }
}