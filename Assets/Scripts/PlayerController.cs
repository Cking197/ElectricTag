using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [Header("Input")]
    public string moveActionName = "Move";
    public string fastStepActionName = "FastStepToggle";
    private PlayerInput _playerInput;
    private InputAction _moveAction;
    private InputAction _fastStepAction;

    [Header("Spawn")]
    public float leftSpawnX = -2.4f;
    public float rightSpawnX = 2.4f;

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

    void Awake()
    {
        _playerInput = GetComponent<PlayerInput>();
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
        {
            _playerInput = GetComponent<PlayerInput>();
        }

        if (_playerInput != null)
        {
            float spawnX = _playerInput.playerIndex == 0 ? leftSpawnX : rightSpawnX;
            Vector3 position = transform.position;
            float halfHeight = 0.0f;
            Collider2D collider2D = GetComponent<Collider2D>();
            if (collider2D != null)
            {
                halfHeight = collider2D.bounds.size.y * 0.5f;
            }


            transform.position = new Vector3(spawnX, halfHeight, position.z);
        }
    }

    void Update()
    {
        if (_isStepping)
        {
            transform.position = Vector3.MoveTowards(
                transform.position,
                _stepTarget,
                _stepSpeed * Time.deltaTime
            );

            if (transform.position == _stepTarget)
            {
                _isStepping = false;
            }

            return;
        }

        if (_moveAction == null)
        {
            return;
        }

        float axis = _moveAction.ReadValue<Vector2>().x;
        float absAxis = Mathf.Abs(axis);

        if (absAxis < deadzone)
        {
            _fastLatched = false;
            return;
        }

        float direction = Mathf.Sign(axis);

        bool isFullStick = absAxis >= fullStickThreshold;
        if (!_fastLatched && _fastStepAction != null && _fastStepAction.IsPressed())
        {
            _fastLatched = true;
        }

        float stepAmount = direction * stepDistance;
        _stepTarget = transform.position + new Vector3(stepAmount, 0.0f, 0.0f);
        _stepSpeed = _fastLatched ? fastSpeed : (isFullStick ? moderateSpeed : slowSpeed);
        _isStepping = true;
    }
}
