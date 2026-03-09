using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Serialization;

public class SwordAttack : MonoBehaviour
{
    [FormerlySerializedAs("_swordSprite")]
    [SerializeField]
    private SpriteRenderer swordSprite;

    private Color _defaultColor = Color.black;

    [Header("Positions")]
    public Vector2
        restLocalPosition = new Vector2(0.6f, 0f); // Sword handle position relative to player when angle is 0

    [Header("Pivot")]
    public Vector2 pivotOffset = new Vector2(-0.3f, 0f); // Pivot (shoulder/arm) position relative to player center

    public float armLength = 0.5f; // Distance from pivot to sword handle

    [Header("Timing")] public float windUpTime = 0.05f; // Time to rotate sword to attack angle before thrusting
    public float thrustOutTime = 0.08f; // Time to extend sword
    public float thrustBackTime = 0.12f; // Time to retract sword (slower)

    [Header("Thrust")] public float thrustDistance = 0.6f; // How far the sword extends on a thrust
    public float bladeLength = 0.5f; // Length of blade from handle to tip (used to compute thrust Y)

    [Header("Angling")] private float _currentAngle = 0f; // Current sword angle (set by right stick or attack input)
    private float _attackAngle = 0f; // Angle locked in when attack starts

    public float AttackAngle => _attackAngle;

    [Header("Blade Block")] public float blockKnockbackDistance = 0.4f;

    private bool _isAttacking;
    private static float _nextBlockTime;
    private bool _hitLanded;
    private BoxCollider2D _hitbox;
    private PlayerController _owner;
    private int _facingDirection;
    private float _cachedZ;

    public bool IsAttacking => _isAttacking;
    [SerializeField] private AudioSource audioSource;
    [Header("Audio")][SerializeField] private AudioClip[] clashClips;
    private float _nextClashTime;
    [SerializeField] private float clashCooldown = 0.05f;
    [SerializeField] private AudioClip[] thrustClips;
    [SerializeField] private float thrustCooldown = 0.05f;
    private float _nextThrustTime;
    private int _lastThrustIndex = -1;

    // Tracks colliders whose trigger logic has already been handled this contact
    private readonly HashSet<Collider2D> _activeCollisions = new HashSet<Collider2D>();


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

        _defaultColor = swordSprite.color;
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f; // 2D sound
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

    // Compute forward direction for a given angle, accounting for facing
    private Vector2 GetForward(float angleDegrees)
    {
        float rad = angleDegrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(rad) * _facingDirection, Mathf.Sin(rad));
    }

    // Compute handle world position for a given angle
    private Vector2 GetHandlePos(float angleDegrees)
    {
        Vector2 flippedPivot = new Vector2(pivotOffset.x * _facingDirection, pivotOffset.y);
        Vector2 pivotWorld = (Vector2)_owner.transform.position + flippedPivot;
        return pivotWorld + GetForward(angleDegrees) * armLength;
    }

    // Position sword by angle + extension along that angle (used for idle and wind-up/retract)
    private void ApplyPivotPosition(float angleDegrees, float extension)
    {
        if (_owner == null) return;

        Vector2 handlePos = GetHandlePos(angleDegrees);
        Vector2 finalPos = handlePos + GetForward(angleDegrees) * extension;

        transform.position = new Vector3(finalPos.x, finalPos.y, _cachedZ);
        transform.rotation = Quaternion.Euler(0f, 0f, angleDegrees * _facingDirection);
    }

    // Position sword at explicit world XY with explicit rotation (used during straight thrust)
    private void ApplyDirectPosition(float worldX, float worldY, float angleDegrees)
    {
        transform.position = new Vector3(worldX, worldY, _cachedZ);
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

    // Handles wind-up rotation, straight thrust, and arcing retraction
    IEnumerator ThrustRoutine()
    {
        _isAttacking = true;
        PlayThrust();

        // Lock in the attack angle at the start
        _attackAngle = _currentAngle;

        // Compute tip Y at rest angle — this is the Y the thrust will travel along
        Vector2 forward = GetForward(_attackAngle);
        Vector2 handlePos = GetHandlePos(_attackAngle);
        float thrustY = handlePos.y + forward.y * bladeLength;

        // Compute the handle X at full extension (straight thrust, so angle = 0)
        // The thrust travels horizontally at thrustY
        Vector2 restHandlePos = GetHandlePos(0f);
        float thrustStartX = GetHandlePos(_attackAngle).x;
        float thrustEndX = thrustStartX + _facingDirection * thrustDistance;

        // --- Wind-up: smoothly rotate from current angle to 0° (straight) ---
        float t = 0f;
        while (t < windUpTime)
        {
            t += Time.deltaTime;
            float blendedAngle = Mathf.Lerp(_attackAngle, 0f, t / windUpTime);
            // Also lerp Y toward thrustY as sword straightens
            Vector2 hp = GetHandlePos(blendedAngle);
            float blendedY = Mathf.Lerp(hp.y, thrustY, t / windUpTime);
            ApplyDirectPosition(hp.x, blendedY, blendedAngle);
            yield return null;
        }

        ApplyDirectPosition(GetHandlePos(0f).x, thrustY, 0f);

        // Cache the straight handle X as thrust start
        thrustStartX = GetHandlePos(0f).x;
        thrustEndX = thrustStartX + _facingDirection * thrustDistance;

        // --- Thrust out: extend straight forward at thrustY ---
        t = 0f;
        while (t < thrustOutTime)
        {
            t += Time.deltaTime;
            float x = Mathf.Lerp(thrustStartX, thrustEndX, t / thrustOutTime);
            ApplyDirectPosition(x, thrustY, 0f);
            yield return null;
        }

        ApplyDirectPosition(thrustEndX, thrustY, 0f);

        yield return null; // Brief pause at full extension before retracting

        // --- Retract: arc back to rest angle/position ---
        t = 0f;
        while (t < thrustBackTime)
        {
            t += Time.deltaTime;
            float p = t / thrustBackTime;
            // Lerp angle back from 0 to _attackAngle
            float retractAngle = Mathf.Lerp(0f, _attackAngle, p);
            // Lerp extension back from thrustDistance to 0
            float extension = Mathf.Lerp(thrustDistance, 0f, p);
            // Lerp Y back from thrustY to rest handle Y
            Vector2 hp = GetHandlePos(retractAngle);
            float retractY = Mathf.Lerp(thrustY, hp.y, p);
            ApplyDirectPosition(hp.x + GetForward(retractAngle).x * extension, retractY, retractAngle);
            yield return null;
        }

        ApplyPivotPosition(_attackAngle, 0f);

        // If no hit occurred, it's a miss
        if (!_hitLanded && GameManager.Instance != null)
        {
            GameManager.Instance.OnAttackMissed(_owner);
            Debug.Log($"{_owner.name}'s attack missed!");
        }

        _isAttacking = false;
        _owner?.NotifyAttackFinished();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        // Always clear the cached state for this collider on a fresh Enter,
        // so re-entries (e.g. after a brief separation) are treated as new contacts.
        _activeCollisions.Remove(other);
        HandleTriggerContact(other);
    }

    void OnTriggerStay2D(Collider2D other)
    {
        // Only re-run logic if this collider hasn't been handled yet this contact.
        if (_activeCollisions.Contains(other)) return;
        HandleTriggerContact(other);
    }

    void OnTriggerExit2D(Collider2D other)
    {
        // Clean up so the next Enter starts fresh.
        _activeCollisions.Remove(other);
    }

    // Core contact logic — called from OnTriggerEnter2D and OnTriggerStay2D.
    // Records `other` in _activeCollisions once a decisive action is taken so
    // Stay callbacks don't repeat it.
    private void HandleTriggerContact(Collider2D other)
    {
        if (GameManager.Instance != null && GameManager.Instance.currentState != GameManager.BoutState.Fencing)
            return;

        // --- Blade-on-hilt block ---
        HiltCollider hilt = other.GetComponent<HiltCollider>();
        if (hilt != null)
        {
            // Mark handled immediately — Stay shouldn't retry regardless of whether
            // the cooldown lets this instance act. The static _nextBlockTime still
            // prevents both swords from acting on the same contact.
            _activeCollisions.Add(other);

            if (Time.time < _nextBlockTime) return;
            _nextBlockTime = Time.time + 0.1f;

            PlayClash();

            PlayerController hiltOwner = other.GetComponentInParent<PlayerController>();

            if (hiltOwner == null || hiltOwner == _owner)
                return;

            SwordAttack otherSword = other.GetComponentInParent<SwordAttack>();

            Debug.Log($"Hilt contact: _owner={_owner.name}, hiltOwner={hiltOwner.name}, _isAttacking={_isAttacking}, otherSword={otherSword.name}, otherSword._isAttacking={otherSword.IsAttacking}");

            if (_isAttacking && (otherSword == null || !otherSword._isAttacking))
            {
                if (hiltOwner.IsInParryWindow() && hiltOwner.DoesParryMatchAttack(_attackAngle))
                {
                    Debug.Log($"{hiltOwner.name} SUCCESSFULLY PARRIED {_owner.name}'s attack!");
                    GameManager.Instance.OnSuccessfulParry(_owner, hiltOwner);
                    CancelAttack();
                    _owner.ApplyKnockback(blockKnockbackDistance);
                    return;
                }

                Debug.Log($"{_owner.name} ATTACKED {hiltOwner.name}'s HILT but was blocked!");

                CancelAttack();
                _owner.ApplyKnockback(blockKnockbackDistance);
                _owner.NotifyHiltReaction();
                hiltOwner.ApplyKnockback(blockKnockbackDistance * 0.25f);
                GameManager.Instance.AssignRightOfWay(hiltOwner);
            }
            else if (otherSword != null && otherSword._isAttacking && !_isAttacking)
            {
                otherSword.CancelAttack();
                hiltOwner.ApplyKnockback(blockKnockbackDistance);
                hiltOwner.NotifyHiltReaction();
                _owner.ApplyKnockback(blockKnockbackDistance * 0.25f);
                GameManager.Instance.AssignRightOfWay(_owner);
                Debug.Log($"{hiltOwner.name} ATTACKED {_owner.name}'s HILT but was blocked!");
            }
            else if (_isAttacking && otherSword != null && otherSword._isAttacking)
            {
                CancelAttack();
                otherSword.CancelAttack();
                _owner.ApplyKnockback(blockKnockbackDistance);
                _owner.NotifyHiltReaction();
                hiltOwner.ApplyKnockback(blockKnockbackDistance);
                hiltOwner.NotifyHiltReaction();
                Debug.Log($"Both players attacked and were blocked by each others' hilts!");
                // Both knocked back, RoW cleared via OnRetreat in ApplyKnockback
            }
            else
            {
                if (hiltOwner.IsInParryWindow() && hiltOwner.DoesParryMatchAttack(_attackAngle))
                {
                    Debug.Log($"{hiltOwner.name} SUCCESSFULLY PARRIED {_owner.name}'s passive contact!");
                    GameManager.Instance.OnSuccessfulParry(_owner, hiltOwner);
                    _owner.ApplyKnockback(blockKnockbackDistance);
                    return;
                }

                Debug.Log($"Passive contact! Minimal pushback!");

                _owner.ApplyKnockback(blockKnockbackDistance * 0.25f);
                hiltOwner.ApplyKnockback(blockKnockbackDistance * 0.25f);
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

        // Mark as handled so Stay doesn't fire again until Exit+Enter
        _activeCollisions.Add(other);

        Debug.Log($"{_owner.name}'s blade contacted {victim.name}'s body");
        _hitLanded = true;

        GameManager.Instance.OnPlayerHit(_owner);
        Debug.Log($"{_owner.name} scored on {victim.name}");
    }

    public void SetRightOfWayVisual(bool hasRightOfWay)
    {
        if (swordSprite == null)
            return;

        swordSprite.color = hasRightOfWay ? Color.yellow : _defaultColor;
    }

    private int PlayRandomClip(
        AudioClip[] clips,
        ref float nextTime,
        float cooldown,
        ref int lastIndex,
        float minPitch = 0.95f,
        float maxPitch = 1.05f)
    {
        if (audioSource == null || clips == null || clips.Length == 0)
            return -1;

        if (Time.time < nextTime)
            return -1;

        nextTime = Time.time + cooldown;

        int index;
        do
        {
            index = Random.Range(0, clips.Length);
        } while (index == lastIndex && clips.Length > 1);

        lastIndex = index;

        audioSource.pitch = Random.Range(minPitch, maxPitch);
        audioSource.PlayOneShot(clips[index]);

        return index;
    }

    private int _lastClashIndex = -1;

    private void PlayClash()
    {
        PlayRandomClip(
            clashClips,
            ref _nextClashTime,
            clashCooldown,
            ref _lastClashIndex);
    }

    private void PlayThrust()
    {
        PlayRandomClip(
            thrustClips,
            ref _nextThrustTime,
            thrustCooldown,
            ref _lastThrustIndex,
            0.98f, 1.02f); // tighter pitch for thrust
    }
}