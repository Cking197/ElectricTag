using UnityEngine;
using System.Collections;
using UnityEngine.Serialization;

public class SwordAttack : MonoBehaviour
{
    [FormerlySerializedAs("_swordSprite")]
    [SerializeField]
    private SpriteRenderer swordSprite;
    private Color _defaultColor = Color.black;

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

    [Header("Blade Block")]
    public float blockKnockbackDistance = 0.4f;     // How far attacker is knocked back on a blade block

    private bool _isAttacking;
    private bool _hitLanded;
    private BoxCollider2D _hitbox;
    private PlayerController _owner;

    public bool IsAttacking => _isAttacking;

    void Awake()
    {
        _hitbox = GetComponent<BoxCollider2D>();
        _hitbox.enabled = true;

        _owner = transform.root.GetComponent<PlayerController>();
        transform.localPosition = restLocalPosition;

        if (swordSprite == null)
            swordSprite = GetComponentInChildren<SpriteRenderer>();
    }

    // Start a sword attack if not already attacking
    public void StartAttack()
    {
        if (_isAttacking) return;
        _hitLanded = false;
        StartCoroutine(ThrustRoutine());
    }

    // Set the visual angle of the sword
    public void SetAngle(float angleDegrees)
    {
        _currentAngle = angleDegrees;
        transform.localRotation = Quaternion.Euler(0f, 0f, angleDegrees);
    }

    // Enable or disable the body-hit hitbox
    public void SetHitboxEnabled(bool enabled)
    {
        if (_hitbox != null)
            _hitbox.enabled = enabled;
    }

    // Cancel an ongoing attack and reset sword
    public void CancelAttack()
    {
        if (!_isAttacking)
            return;

        StopAllCoroutines();

        _isAttacking = false;

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

        // If no hit or block occurred, it's a miss
        if (!_hitLanded && GameManager.Instance != null)
        {
            GameManager.Instance.OnAttackMissed(_owner);
            Debug.Log($"{_owner.name}'s attack missed!");
        }
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
        if (GameManager.Instance != null && GameManager.Instance.currentState != GameManager.BoutState.Fencing)
            return;

        // --- Blade-on-hilt block ---
        // Fires when this sword's blade collider hits the opponent's hilt child GameObject
        HiltCollider hilt = other.GetComponent<HiltCollider>();
        if (hilt != null)
        {
            // Make sure the hilt belongs to a different player
            PlayerController hiltOwner = other.GetComponentInParent<PlayerController>();
            if (hiltOwner == null || hiltOwner == _owner)
                return;

            SwordAttack otherSword = other.GetComponentInParent<SwordAttack>();

            if (_isAttacking && (otherSword == null || !otherSword._isAttacking))
            {
                // This sword is attacking into an idle hilt — knock this owner back
                Debug.Log($"{_owner.name}'s thrust was blocked by {hiltOwner.name}'s hilt!");
                CancelAttack();
                _owner.ApplyKnockback(blockKnockbackDistance);
            }
            else if (otherSword != null && otherSword._isAttacking && !_isAttacking)
            {
                // Other sword is attacking, this hilt is defending — knock other owner back
                Debug.Log($"{hiltOwner.name}'s thrust was blocked by {_owner.name}'s hilt!");
                otherSword.CancelAttack();
                hiltOwner.ApplyKnockback(blockKnockbackDistance);
            }
            else
            {
                // Neither or both attacking — knock both back
                Debug.Log($"{_owner.name} and {hiltOwner.name} clashed hilts!");
                if (_isAttacking) CancelAttack();
                if (otherSword != null && otherSword._isAttacking) otherSword.CancelAttack();
                _owner.ApplyKnockback(blockKnockbackDistance);
                hiltOwner.ApplyKnockback(blockKnockbackDistance);
            }
            return;
        }

        // --- Blade-on-body hit ---
        if (!other.CompareTag("Player")) return;

        PlayerController victim = other.GetComponentInParent<PlayerController>();
        if (victim == null || victim == _owner) return;

        HiltCollider victimHilt = victim.GetComponentInChildren<HiltCollider>();
        if (victimHilt != null && Physics2D.IsTouching(_hitbox, victimHilt.GetComponent<Collider2D>()))
        {
            Debug.Log($"{_owner.name}'s blade hit {victim.name}'s body but was touching hilt — ignoring!");
            return;
        }

        Debug.Log($"{_owner.name}'s blade contacted {victim.name}'s body");
        _hitLanded = true;

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

    public void SetRightOfWayVisual(bool hasRightOfWay)
    {
        if (swordSprite == null)
            return;

        swordSprite.color = hasRightOfWay ? Color.yellow : _defaultColor;
    }
}