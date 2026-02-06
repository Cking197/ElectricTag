using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float stepDistance = 0.16f;
    public float fullStickThreshold = 0.85f;
    public float deadzone = 0.2f;
    public float slowSpeed = 0.9f;
    public float moderateSpeed = 1.4f;
    public float fastSpeed = 2.2f;
    public string fastStepButton = "FastStep";

    private bool _isStepping;
    private bool _fastLatched;
    private Vector3 _stepTarget;
    private float _stepSpeed;

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

        float axis = Input.GetAxis("Horizontal");
        float absAxis = Mathf.Abs(axis);

        if (absAxis < deadzone)
        {
            _fastLatched = false;
            return;
        }

        float direction = Mathf.Sign(axis);

        bool isFullStick = absAxis >= fullStickThreshold;
        if (!_fastLatched && !string.IsNullOrEmpty(fastStepButton) && Input.GetButton(fastStepButton))
        {
            _fastLatched = true;
        }

        float stepAmount = direction * stepDistance;
        _stepTarget = transform.position + new Vector3(stepAmount, 0.0f, 0.0f);
        _stepSpeed = _fastLatched ? fastSpeed : (isFullStick ? moderateSpeed : slowSpeed);
        _isStepping = true;
    }
}
