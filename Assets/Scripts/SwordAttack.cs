using UnityEngine;
using System.Collections;

public class SwordAttack : MonoBehaviour
{
    [Header("Positions")]
    public Vector2 restLocalPosition = new Vector2(0.6f, 0f);
    public Vector2 thrustLocalPosition = new Vector2(1.2f, 0f);

    [Header("Timing")]
    public float thrustOutTime = 0.08f;
    public float thrustBackTime = 0.06f;

    private bool isAttacking;
    private BoxCollider2D hitbox;
    private PlayerController owner;

    void Awake()
    {
        hitbox = GetComponent<BoxCollider2D>();
        hitbox.enabled = false;

        owner = transform.root.GetComponent<PlayerController>();
        transform.localPosition = restLocalPosition;
    }

    public void StartAttack()
    {
        if (isAttacking) return;
        StartCoroutine(ThrustRoutine());
    }

    IEnumerator ThrustRoutine()
    {
        isAttacking = true;

        // Thrust
        hitbox.enabled = true;
        yield return MoveSword(restLocalPosition, thrustLocalPosition, thrustOutTime);

        // Retract
        hitbox.enabled = false;
        yield return MoveSword(thrustLocalPosition, restLocalPosition, thrustBackTime);

        isAttacking = false;
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
        if (!hitbox.enabled) return;

        PlayerController victim =
            other.GetComponentInParent<PlayerController>();

        if (victim == null) return;
        if (victim == owner) return;

        hitbox.enabled = false;
        GameManager.Instance.OnPlayerHit(owner, victim);
        Debug.Log($"{owner.name} hit {other.name}");
    }
}
