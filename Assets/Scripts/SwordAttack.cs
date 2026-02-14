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

    private bool _isAttacking;
    private BoxCollider2D _hitbox;
    private PlayerController _owner;

    public bool IsAttacking => _isAttacking;

    void Awake()
    {
        _hitbox = GetComponent<BoxCollider2D>();
        _hitbox.enabled = false;

        _owner = transform.root.GetComponent<PlayerController>();
        transform.localPosition = restLocalPosition;
    }

    // Start a sword attack if not already attacking
    public void StartAttack()
    {
        if (_isAttacking) return;
        StartCoroutine(ThrustRoutine());
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

        // Extend sword and enable hitbox
        _hitbox.enabled = true;
        yield return MoveSword(restLocalPosition, thrustLocalPosition, thrustOutTime);

        // Retract sword and disable hitbox
        _hitbox.enabled = false;
        yield return MoveSword(thrustLocalPosition, restLocalPosition, thrustBackTime);

        _isAttacking = false;
    }

    // Smoothly move sword between positions
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

    // Detect hits on opponent
    void OnTriggerEnter2D(Collider2D other)
    {
        if (!_hitbox.enabled) return;

        // Ignore if the opponent is the owner or invalid
        PlayerController victim = other.GetComponentInParent<PlayerController>();
        if (victim == null || victim == _owner) return;

        // Ignore hits if the bout is in a false start
        if (GameManager.Instance != null && GameManager.Instance.currentState != GameManager.BoutState.Fencing)
        {
            return;
        }

        _hitbox.enabled = false;

        // Check if other is parrying
        if (victim.IsInParryWindow())
        {
            Debug.Log($"{victim.name} PARRIED {_owner.name}'s attack!");

            GameManager.Instance.OnSuccessfulParry(_owner, victim);
            return;
        }

        GameManager.Instance.OnPlayerHit(_owner);
        Debug.Log($"{_owner.name} hit {other.name}");
    }
}