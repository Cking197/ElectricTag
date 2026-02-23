using UnityEngine;
using System.Collections;

public class SwordAttack : MonoBehaviour
{
    [Header("Positions")]
    public Vector2 restLocalPosition = new Vector2(0.6f, 0f);   // Default sword position
    public Vector2 thrustLocalPosition = new Vector2(1.2f, 0f); // Sword fully extended position

    [Header("Timing")]
    public float thrustOutTime = 0.08f;   // Time to extend sword
    public float thrustBackTime = 0.06f;  // Time to retract sword

    [Header("Angling")]
    private float _currentAngle = 0f;  // Current sword angle
    private float _attackAngle = 0f;   // Angle locked in when attack starts

    public float AttackAngle => _attackAngle;

    private bool _isAttacking;
    private BoxCollider2D _hitbox;
    private PlayerController _owner;

    public bool IsAttacking => _isAttacking;

    void Awake()
    {
        _hitbox = GetComponent<BoxCollider2D>();
        _hitbox.enabled = true;

        _owner = transform.root.GetComponent<PlayerController>();
        transform.localPosition = restLocalPosition;
    }

    // Start a sword attack if not already attacking
    public void StartAttack()
    {
        if (_isAttacking) return;
        StartCoroutine(ThrustRoutine());
    }

    // Set the visual angle of the sword
    public void SetAngle(float angleDegrees)
    {
        _currentAngle = angleDegrees;

        // Rotate the sword sprite around its pivot
        transform.localRotation = Quaternion.Euler(0f, 0f, angleDegrees);
    }

    // Enable or disable the hitbox
    public void SetHitboxEnabled(bool enabled)
    {
        if (_hitbox != null)
        {
            _hitbox.enabled = enabled;
        }
    }

    // Cancel an ongoing attack and reset sword
    public void CancelAttack()
    {
        if (!_isAttacking)
            return;

        // Stop the thrust
        StopAllCoroutines();

        // Reset state
        _isAttacking = false;
        _hitbox.enabled = false;

        // Snap sword back to rest position
        transform.localPosition = restLocalPosition;

        Debug.Log($"{_owner.name}'s attack was cancelled");
    }

    // Handles sword thrust and retraction
    IEnumerator ThrustRoutine()
    {
        _isAttacking = true;

        // Lock in the attack angle at the start
        _attackAngle = _currentAngle;

        // Calculate direction based on attack angle
        Vector2 direction = new Vector2(Mathf.Cos(_attackAngle * Mathf.Deg2Rad),
                                         Mathf.Sin(_attackAngle * Mathf.Deg2Rad));

        // Thrust distance is how far we extend from rest
        float thrustDistance = thrustLocalPosition.magnitude;

        // Calculate thrust position: start from rest, extend along angle
        Vector2 angleThrustPos = (Vector2)restLocalPosition + (direction * thrustDistance);

        // Extend sword
        yield return MoveSword(restLocalPosition, angleThrustPos, thrustOutTime);

        // Retract sword
        yield return MoveSword(angleThrustPos, restLocalPosition, thrustBackTime);

        _isAttacking = false;
    }

    // Smoothly move sword between positions along the attack angle
    IEnumerator MoveSword(Vector2 from, Vector2 to, float duration)
    {
        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            transform.localPosition = Vector2.Lerp(from, to, t / duration);
            yield return null;
        }

        transform.localPosition = to;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        PlayerController victim = other.GetComponentInParent<PlayerController>();
        if (victim == null || victim == _owner) return;

        if (GameManager.Instance != null && GameManager.Instance.currentState != GameManager.BoutState.Fencing)
            return;

        Debug.Log($"{_owner.name}'s blade contacted {victim.name}'s body");

        if (victim.IsInParryWindow())
        {
            if (victim.DoesParryMatchAttack(_attackAngle))
            {
                Debug.Log($"{victim.name} SUCCESSFULLY PARRIED {_owner.name}'s attack!");
                GameManager.Instance.OnSuccessfulParry(_owner, victim);
                return;
            }
            else
            {
                Debug.Log($"{victim.name} parried but WRONG ANGLE!");
            }
        }

        GameManager.Instance.OnPlayerHit(_owner);
        Debug.Log($"{_owner.name} scored on {victim.name}");
    }
}