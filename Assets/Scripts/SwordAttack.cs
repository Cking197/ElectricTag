using UnityEngine;
using System.Collections;
using UnityEngine.Events;
using UnityEngine.Serialization;

public class SwordAttack : MonoBehaviour
{
    [Header("Positions")]
    public Vector2 restLocalPosition = new Vector2(0.6f, 0f);
    public Vector2 thrustLocalPosition = new Vector2(1.2f, 0f);

    [Header("Timing")]
    public float thrustOutTime = 0.08f;
    public float thrustBackTime = 0.06f;

    private bool _isAttacking;
    private BoxCollider2D _hitbox;
    private PlayerController _owner;

    void Awake()
    {
        _hitbox = GetComponent<BoxCollider2D>();
        _hitbox.enabled = false;

        _owner = transform.root.GetComponent<PlayerController>();
        transform.localPosition = restLocalPosition;
    }

    public void StartAttack()
    {
        if (_isAttacking) return;
        StartCoroutine(ThrustRoutine());
    }

    IEnumerator ThrustRoutine()
    {
        _isAttacking = true;

        // Thrust
        _hitbox.enabled = true;
        yield return MoveSword(restLocalPosition, thrustLocalPosition, thrustOutTime);

        // Retract
        _hitbox.enabled = false;
        yield return MoveSword(thrustLocalPosition, restLocalPosition, thrustBackTime);

        _isAttacking = false;
    }

    IEnumerator MoveSword(Vector2 from, Vector2 to, float duration)
    {
        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            float lerp = t / duration;
            transform.localPosition = Vector2.Lerp(from, to, lerp);
            yield return null;
        }

        transform.localPosition = to;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!_hitbox.enabled) return;

        PlayerController victim =
            other.GetComponentInParent<PlayerController>();

        if (victim == null) return;
        if (victim == _owner) return;

        _hitbox.enabled = false;
        GameManager.Instance.OnPlayerHit(_owner, victim);
        Debug.Log($"{_owner.name} hit {other.name}");
    }
}
