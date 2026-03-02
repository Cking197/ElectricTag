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
    public Vector2 restLocalPosition = new Vector2(0.6f, 0f);   // Sword handle position relative to player when angle is 0

    [Header("Pivot")]
    public Vector2 pivotOffset = new Vector2(-0.3f, 0f);        // Pivot (shoulder/arm) position relative to player center
    public float armLength = 0.5f;                              // Distance from pivot to sword handle

    [Header("Timing")]
    public float windUpTime = 0.05f;      // Time to rotate sword to attack angle before thrusting
    public float thrustOutTime = 0.08f;   // Time to extend sword
    public float thrustBackTime = 0.12f;  // Time to retract sword (slower)

    [Header("Thrust")]
    public float thrustDistance = 0.6f;   // How far the sword extends on a thrust

    [Header("Angling")]
    private float _currentAngle = 0f;     // Current sword angle (set by right stick or attack input)
    private float _attackAngle = 0f;      // Angle locked in when attack starts

    public float AttackAngle => _attackAngle;

    [Header("Blade Block")]
    public float blockKnockbackDistance = 0.4f;

    private bool _isAttacking;
    private bool _hitLanded;
    private BoxCollider2D _hitbox;
    private PlayerController _owner;
    private int _facingDirection;
    private float _cachedZ;

    public bool IsAttacking => _isAttacking;

    void Awake()
    {
        _hitbox = GetComponent<BoxCollider2D>();
        _hitbox.enabled = true;

        _owner = transform.root.GetComponent<PlayerController>();
        _cachedZ = transform.position.z;

        // Zero out local position — world position is fully driven by ApplyPivotPosition
        transform.localPosition = new Vector3(0f, 0f, transform.localPosition.z);

        if (swordSprite == null)
            swordSprite = GetComponentInChildren<SpriteRenderer>();
    }

    void Start()
    {
        // FacingDirection is set in PlayerController.Start via PlayerInput.playerIndex
        // PlayerController.Awake runs before SwordAttack.Start so this is safe
        _facingDirection = _owner != null ? _owner.FacingDirection : 1;
        ApplyPivotPosition(_currentAngle, 0f);
        Debug.Log($"{_owner?.name} sword Start: facingDir={_facingDirection}, cachedZ={_cachedZ}");
    }

    // Called every frame by PlayerController via SetAngle — positions sword around pivot
    public void SetAngle(float angleDegrees)
    {
        _currentAngle = angleDegrees;

        if (!_isAttacking)
            ApplyPivotPosition(_currentAngle, 0f);
    }

    // Compute and set sword world position + rotation based on pivot, angle, and thrust extension
    private void ApplyPivotPosition(float angleDegrees, float extension)
    {
        if (_owner == null) return;

        // Flip pivot X for facing direction
        Vector2 flippedPivot = new Vector2(pivotOffset.x * _facingDirection, pivotOffset.y);

        // Pivot in world space
        Vector2 pivotWorld = (Vector2)_owner.transform.position + flippedPivot;

        // Forward direction: flip X for facing, Y is always as-is (up is up for both players)
        float rad = angleDegrees * Mathf.Deg2Rad;
        Vector2 forward = new Vector2(Mathf.Cos(rad) * _facingDirection, Mathf.Sin(rad));

        // Sword handle sits armLength from pivot, tip extends further by extension
        Vector2 handlePos = pivotWorld + forward * armLength;
        Vector2 finalPos = handlePos + forward * extension;

        // Use cached Z so sword stays in front of background
        transform.position = new Vector3(finalPos.x, finalPos.y, _cachedZ);
        transform.rotation = Quaternion.Euler(0f, 0f, angleDegrees * _facingDirection);
    }

    // Enable or disable the body-hit hitbox
    public void SetHitboxEnabled(bool enabled)
    {
        if (_hitbox != null)
            _hitbox.enabled = enabled;
    }

    // Start a sword attack if not already attacking
    public void StartAttack()
    {
        if (_isAttacking) return;
        _hitLanded = false;
        StartCoroutine(ThrustRoutine());
    }

    // Cancel an ongoing attack and reset sword
    public void CancelAttack()
    {
        if (!_isAttacking)
            return;

        StopAllCoroutines();

        _isAttacking = false;

        // Snap back to current angle at rest
        ApplyPivotPosition(_currentAngle, 0f);

        Debug.Log($"{_owner.name}'s attack was cancelled");
    }

    // Handles wind-up rotation, thrust, and retraction
    IEnumerator ThrustRoutine()
    {
        _isAttacking = true;

        // Lock in the attack angle at the start
        _attackAngle = _currentAngle;

        // --- Wind-up: smoothly rotate to attack angle ---
        float startAngle = _currentAngle;
        float t = 0f;
        while (t < windUpTime)
        {
            t += Time.deltaTime;
            float blendedAngle = Mathf.Lerp(startAngle, _attackAngle, t / windUpTime);
            ApplyPivotPosition(blendedAngle, 0f);
            yield return null;
        }
        ApplyPivotPosition(_attackAngle, 0f);

        // --- Thrust out: extend forward along attack angle ---
        t = 0f;
        while (t < thrustOutTime)
        {
            t += Time.deltaTime;
            float extension = Mathf.Lerp(0f, thrustDistance, t / thrustOutTime);
            ApplyPivotPosition(_attackAngle, extension);
            yield return null;
        }
        ApplyPivotPosition(_attackAngle, thrustDistance);

        // --- Retract: pull back along the same angle, slower ---
        t = 0f;
        while (t < thrustBackTime)
        {
            t += Time.deltaTime;
            float extension = Mathf.Lerp(thrustDistance, 0f, t / thrustBackTime);
            ApplyPivotPosition(_attackAngle, extension);
            yield return null;
        }
        ApplyPivotPosition(_attackAngle, 0f);

        _isAttacking = false;

        // If no hit occurred, it's a miss
        if (!_hitLanded && GameManager.Instance != null)
        {
            GameManager.Instance.OnAttackMissed(_owner);
            Debug.Log($"{_owner.name}'s attack missed!");
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (GameManager.Instance != null && GameManager.Instance.currentState != GameManager.BoutState.Fencing)
            return;

        // --- Blade-on-hilt block ---
        HiltCollider hilt = other.GetComponent<HiltCollider>();
        if (hilt != null)
        {
            PlayerController hiltOwner = other.GetComponentInParent<PlayerController>();
            if (hiltOwner == null || hiltOwner == _owner)
                return;

            SwordAttack otherSword = other.GetComponentInParent<SwordAttack>();

            if (_isAttacking && (otherSword == null || !otherSword._isAttacking))
            {
                Debug.Log($"{_owner.name}'s thrust was blocked by {hiltOwner.name}'s hilt!");
                CancelAttack();
                _owner.ApplyKnockback(blockKnockbackDistance);
            }
            else if (otherSword != null && otherSword._isAttacking && !_isAttacking)
            {
                Debug.Log($"{hiltOwner.name}'s thrust was blocked by {_owner.name}'s hilt!");
                otherSword.CancelAttack();
                hiltOwner.ApplyKnockback(blockKnockbackDistance);
            }
            else
            {
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

        // Cancel hit if blade is also touching victim's hilt (passed through guard)
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